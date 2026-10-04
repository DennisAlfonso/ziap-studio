using System.Text;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Integration.Remote;

/// <summary>
/// Coordinates the safe sequence for Story authoring: remote snapshot/lock, local
/// working state, semantic staging patch, atomic mirror, and cache invalidation.
/// </summary>
public sealed class StoryLocalizationAuthoringService
{
    private readonly IRemoteLocalizationAuthoringClient _remoteClient;
    private readonly DocumentSnapshotService _snapshotService;
    private readonly ExternalModificationDetector _externalModificationDetector;
    private readonly PublishedLocalizationFileWriter _mirrorWriter;
    private readonly LocalizationService _localizationService;

    public StoryLocalizationAuthoringService(
        IRemoteLocalizationAuthoringClient remoteClient,
        DocumentSnapshotService snapshotService,
        ExternalModificationDetector externalModificationDetector,
        PublishedLocalizationFileWriter mirrorWriter,
        LocalizationService localizationService)
    {
        _remoteClient = remoteClient;
        _snapshotService = snapshotService;
        _externalModificationDetector = externalModificationDetector;
        _mirrorWriter = mirrorWriter;
        _localizationService = localizationService;
    }

    public async Task<StoryLocalizationAuthoringSession> BeginAsync(
        ZiapProject project,
        LocalizationReferenceOrigin origin,
        CancellationToken cancellationToken = default)
    {
        ValidateEditableOrigin(project, origin);
        var sourcePath = GetSafeLocalPath(project.Path, origin);
        var localSnapshot = await _snapshotService.CaptureAsync(sourcePath, cancellationToken);
        var lockInfo = await _remoteClient.ClaimLockAsync(
            project.Id, origin.Locale, origin.SourceFile, cancellationToken);
        if (!lockInfo.IsOwnedByCurrentUser)
        {
            return StoryLocalizationAuthoringSession.Locked(origin, lockInfo);
        }

        try
        {
            var snapshot = await _remoteClient.GetAuthoringFileAsync(
                project.Id, origin.Locale, origin.SourceFile, cancellationToken);
            var editSession = new LocalizationEditSession(snapshot, localSnapshot);
            if (!editSession.CanEdit(origin))
            {
                throw new InvalidOperationException(
                    "La reference selezionata non punta a una stringa scalar esistente nello staging remoto.");
            }
            return new StoryLocalizationAuthoringSession(origin, editSession, lockInfo);
        }
        catch
        {
            await BestEffortReleaseAsync(project, origin, lockInfo.AcquiredAt, cancellationToken);
            throw;
        }
    }

    public async Task<StoryLocalizationSaveResult> SaveAsync(
        ZiapProject project,
        StoryLocalizationAuthoringSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(session);
        if (session.State == LocalizationAuthoringState.Conflict)
        {
            return StoryLocalizationSaveResult.Conflict("La base remota deve essere ricaricata prima del salvataggio.");
        }
        if (session.EditSession is null || !session.EditSession.IsDirty)
        {
            return StoryLocalizationSaveResult.NoChanges;
        }
        if (await _externalModificationDetector.HasChangedAsync(
                session.EditSession.LocalSourceSnapshot, cancellationToken))
        {
            session.State = LocalizationAuthoringState.Conflict;
            return StoryLocalizationSaveResult.Conflict("Il mirror locale è stato modificato esternamente.");
        }

        LocalizationAuthoringSnapshot remoteSnapshot;
        try
        {
            remoteSnapshot = await _remoteClient.PatchStagingAsync(
                project.Id,
                session.Origin.Locale,
                session.Origin.SourceFile,
                session.EditSession.Snapshot.CurrentVersionId,
                session.EditSession.Snapshot.StagingChecksum,
                session.EditSession.BuildPatches(),
                cancellationToken);
        }
        catch (RemoteLocalizationAuthoringException exception) when (
            exception.Failure == RemoteLocalizationAuthoringFailure.Conflict)
        {
            session.State = LocalizationAuthoringState.Conflict;
            return StoryLocalizationSaveResult.Conflict(exception.Message);
        }
        catch (RemoteLocalizationAuthoringException exception) when (
            exception.Failure == RemoteLocalizationAuthoringFailure.LockedByOther)
        {
            session.State = LocalizationAuthoringState.LockedByOther;
            return StoryLocalizationSaveResult.Locked(exception.Message);
        }

        try
        {
            var sourcePath = GetSafeLocalPath(project.Path, session.Origin);
            await _mirrorWriter.WriteAsync(
                sourcePath,
                Encoding.UTF8.GetBytes(remoteSnapshot.Content),
                session.EditSession.LocalSourceSnapshot.ContentHash,
                cancellationToken);
            var localSnapshot = await _snapshotService.CaptureAsync(sourcePath, cancellationToken);
            session.EditSession.AcceptSavedSnapshot(remoteSnapshot, localSnapshot);
            session.State = LocalizationAuthoringState.Staging;
            _localizationService.Invalidate(project.Path, session.Origin.Locale, session.Origin.SourceFile);
            return StoryLocalizationSaveResult.Saved(remoteSnapshot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ExternalDocumentModificationException)
        {
            session.PendingRemoteSnapshot = remoteSnapshot;
            session.State = LocalizationAuthoringState.LocalOutOfSync;
            return StoryLocalizationSaveResult.LocalOutOfSync(
                "Remote staging salvato, ma il mirror locale non è stato aggiornato. " +
                "Riprova la sincronizzazione.");
        }
    }

    public async Task<StoryLocalizationSaveResult> RetryMirrorAsync(
        ZiapProject project,
        StoryLocalizationAuthoringSession session,
        CancellationToken cancellationToken = default)
    {
        if (session.PendingRemoteSnapshot is not { } snapshot || session.EditSession is null)
        {
            return StoryLocalizationSaveResult.NoChanges;
        }
        var sourcePath = GetSafeLocalPath(project.Path, session.Origin);
        try
        {
            await _mirrorWriter.WriteAsync(
                sourcePath,
                Encoding.UTF8.GetBytes(snapshot.Content),
                session.EditSession.LocalSourceSnapshot.ContentHash,
                cancellationToken);
            var updatedSnapshot = await _snapshotService.CaptureAsync(sourcePath, cancellationToken);
            session.EditSession.AcceptSavedSnapshot(snapshot, updatedSnapshot);
            session.PendingRemoteSnapshot = null;
            session.State = LocalizationAuthoringState.Staging;
            _localizationService.Invalidate(project.Path, session.Origin.Locale, session.Origin.SourceFile);
            return StoryLocalizationSaveResult.Saved(snapshot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ExternalDocumentModificationException)
        {
            return StoryLocalizationSaveResult.LocalOutOfSync("Mirror locale ancora non sincronizzabile.");
        }
    }

    public Task<LocalizationLockInfo> RenewAsync(
        ZiapProject project,
        StoryLocalizationAuthoringSession session,
        CancellationToken cancellationToken = default) => _remoteClient.RenewLockAsync(
            project.Id, session.Origin.Locale, session.Origin.SourceFile, session.Lock.AcquiredAt, cancellationToken);

    public Task ReleaseAsync(
        ZiapProject project,
        StoryLocalizationAuthoringSession session,
        CancellationToken cancellationToken = default) => BestEffortReleaseAsync(
            project, session.Origin, session.Lock.AcquiredAt, cancellationToken);

    private async Task BestEffortReleaseAsync(
        ZiapProject project,
        LocalizationReferenceOrigin origin,
        string? acquiredAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await _remoteClient.ReleaseLockAsync(
                project.Id, origin.Locale, origin.SourceFile, acquiredAt, cancellationToken);
        }
        catch (RemoteLocalizationAuthoringException)
        {
            // Expiry is the durable fallback if the normal best-effort release cannot run.
        }
    }

    private static void ValidateEditableOrigin(ZiapProject project, LocalizationReferenceOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(origin);
        if (!origin.Locale.Equals(LocalizationService.DefaultLocale, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Story Authoring consente solo il locale master del progetto.");
        }
        if (origin.Segments.Count == 0 || origin.Segments.Any(segment =>
                segment.PropertyName is { Length: 0 } || segment.ArrayIndex is < 0 ||
                segment.PropertyName is not null && segment.ArrayIndex is not null))
        {
            throw new InvalidOperationException("La reference Localization non ha un path strutturato sicuro.");
        }
    }

    private static string GetSafeLocalPath(string projectPath, LocalizationReferenceOrigin origin)
    {
        var localesRoot = Path.GetFullPath(Path.Combine(projectPath, "locales", origin.Locale));
        var candidate = Path.GetFullPath(Path.Combine(localesRoot, origin.SourceFile));
        var prefix = localesRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Il source file Localization è fuori dalla cartella del progetto.");
        }
        return candidate;
    }
}

public sealed class StoryLocalizationAuthoringSession
{
    public StoryLocalizationAuthoringSession(
        LocalizationReferenceOrigin origin,
        LocalizationEditSession editSession,
        LocalizationLockInfo @lock)
    {
        Origin = origin;
        EditSession = editSession;
        Lock = @lock;
    }

    private StoryLocalizationAuthoringSession(LocalizationReferenceOrigin origin, LocalizationLockInfo @lock)
    {
        Origin = origin;
        Lock = @lock;
        State = LocalizationAuthoringState.LockedByOther;
    }

    public LocalizationReferenceOrigin Origin { get; }
    public LocalizationEditSession? EditSession { get; }
    public LocalizationLockInfo Lock { get; private set; }
    public LocalizationAuthoringState State { get; set; } = LocalizationAuthoringState.Clean;
    public LocalizationAuthoringSnapshot? PendingRemoteSnapshot { get; set; }

    public static StoryLocalizationAuthoringSession Locked(
        LocalizationReferenceOrigin origin,
        LocalizationLockInfo @lock) => new(origin, @lock);

    public void UpdateLock(LocalizationLockInfo @lock)
    {
        Lock = @lock ?? throw new ArgumentNullException(nameof(@lock));
    }
}

public sealed record StoryLocalizationSaveResult
{
    public static StoryLocalizationSaveResult NoChanges { get; } = new();
    public bool RemoteSaved { get; init; }
    public bool LocalMirrorSynchronized { get; init; }
    public bool IsConflict { get; init; }
    public bool IsLocked { get; init; }
    public string? Message { get; init; }
    public LocalizationAuthoringSnapshot? Snapshot { get; init; }

    public static StoryLocalizationSaveResult Saved(LocalizationAuthoringSnapshot snapshot) => new()
    {
        RemoteSaved = true,
        LocalMirrorSynchronized = true,
        Snapshot = snapshot,
    };

    public static StoryLocalizationSaveResult LocalOutOfSync(string message) => new()
    {
        RemoteSaved = true,
        Message = message,
    };

    public static StoryLocalizationSaveResult Conflict(string message) => new()
    {
        IsConflict = true,
        Message = message,
    };

    public static StoryLocalizationSaveResult Locked(string message) => new()
    {
        IsLocked = true,
        Message = message,
    };
}
