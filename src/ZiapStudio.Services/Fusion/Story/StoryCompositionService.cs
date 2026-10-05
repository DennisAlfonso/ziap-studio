using System.Text;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Integration.Remote;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Fusion.Story;

/// <summary>
/// Coordinates the deliberately ordered Story mutation. The event file is never
/// changed until an idempotent remote MDV append has returned its authoritative
/// structured path and the local Localization mirror has been written.
/// </summary>
public sealed class StoryCompositionService
{
    private readonly IRemoteLocalizationAuthoringClient _remoteClient;
    private readonly DocumentSnapshotService _snapshotService;
    private readonly PublishedLocalizationFileWriter _mirrorWriter;
    private readonly LocalizationService _localizationService;
    private readonly StoryCompositionPlanner _planner;
    private readonly StoryCommandListWriter _commandWriter;
    private readonly StoryCompositionRecoveryStore _recoveryStore;

    public StoryCompositionService(
        IRemoteLocalizationAuthoringClient remoteClient,
        DocumentSnapshotService snapshotService,
        PublishedLocalizationFileWriter mirrorWriter,
        LocalizationService localizationService,
        StoryCompositionPlanner planner,
        StoryCommandListWriter commandWriter,
        StoryCompositionRecoveryStore recoveryStore)
    {
        _remoteClient = remoteClient;
        _snapshotService = snapshotService;
        _mirrorWriter = mirrorWriter;
        _localizationService = localizationService;
        _planner = planner;
        _commandWriter = commandWriter;
        _recoveryStore = recoveryStore;
    }

    /// <summary>Reads a fresh remote snapshot for an already locally valid plan, without mutating either side.</summary>
    public async Task<StoryCompositionPlan> PrepareAsync(
        ZiapProject project,
        StoryCompositionPlan plan,
        CancellationToken cancellationToken = default)
    {
        await _commandWriter.ValidateInsertionAsync(project.Path, plan, cancellationToken);
        var snapshot = await _remoteClient.GetAuthoringFileAsync(
            project.Id, plan.Locale, plan.LocalizationFile, cancellationToken);
        return _planner.AttachRemoteBase(plan, snapshot.Content, snapshot.CurrentVersionId, snapshot.StagingChecksum);
    }

    public StoryCompositionPreview CreatePreview(StoryCompositionPlan plan) => _planner.CreatePreview(plan);

    public Task<StoryCompositionPlan> BuildPlanAsync(
        StoryCompositionOperationType operationType,
        StoryCommandListTarget target,
        StoryBlock anchor,
        string projectPath,
        string speaker,
        string text,
        CancellationToken cancellationToken = default) => _planner.CreateAsync(
        operationType, target, anchor, projectPath, speaker, text, cancellationToken);

    public async Task<StoryCompositionCommitResult> CommitAsync(
        ZiapProject project,
        StoryCompositionPlan plan,
        string? retainedLockAcquiredAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(plan);
        try
        {
            // This validation occurs before the remote append, so an already changed
            // Map/CommonEvents source cannot create a new remote entry.
            await _commandWriter.ValidateInsertionAsync(project.Path, plan, cancellationToken);
        }
        catch (StoryLocalSourceConflictException exception)
        {
            return StoryCompositionCommitResult.SourceConflict(exception.Message);
        }
        catch (StoryCommandListWriteException exception)
        {
            return StoryCompositionCommitResult.InvalidPlan(exception.Message);
        }

        var lockInfo = await _remoteClient.ClaimLockAsync(
            project.Id, plan.Locale, plan.LocalizationFile, cancellationToken);
        if (!lockInfo.IsOwnedByCurrentUser)
        {
            return StoryCompositionCommitResult.Locked("Il file Localization è bloccato da un altro autore.");
        }

        try
        {
            var preparedPlan = plan;
            if (preparedPlan.ExpectedArrayLength is null ||
                string.IsNullOrWhiteSpace(preparedPlan.RemoteCurrentVersionId))
            {
                var snapshot = await _remoteClient.GetAuthoringFileAsync(
                    project.Id, plan.Locale, plan.LocalizationFile, cancellationToken);
                preparedPlan = _planner.AttachRemoteBase(
                    plan, snapshot.Content, snapshot.CurrentVersionId, snapshot.StagingChecksum);
            }

            var mirrorPath = GetSafeLocalMirrorPath(project.Path, preparedPlan.Locale, preparedPlan.LocalizationFile);
            var mirrorSnapshot = await _snapshotService.CaptureAsync(mirrorPath, cancellationToken);
            var recovery = NewRecovery(project.Id, preparedPlan, mirrorSnapshot);
            await _recoveryStore.UpsertAsync(recovery, cancellationToken);

            LocalizationAppendResult appended;
            try
            {
                appended = await _remoteClient.AppendStagingEntryAsync(new LocalizationAppendRequest
                {
                    ProjectId = project.Id,
                    Locale = preparedPlan.Locale,
                    SourceFile = preparedPlan.LocalizationFile,
                    OperationId = preparedPlan.OperationId,
                    BasedOnVersionId = preparedPlan.RemoteCurrentVersionId!,
                    ExpectedStagingChecksum = preparedPlan.ExpectedStagingChecksum,
                    ExpectedArrayLength = preparedPlan.ExpectedArrayLength!.Value,
                    BranchPath = preparedPlan.LocalizationBranchPath,
                    Template = preparedPlan.TemplateName,
                    Speaker = preparedPlan.Values.Speaker,
                    Text = preparedPlan.Values.Text,
                }, cancellationToken);
            }
            catch (RemoteLocalizationAuthoringException exception) when (
                exception.Failure == RemoteLocalizationAuthoringFailure.Conflict)
            {
                await _recoveryStore.DismissAsync(preparedPlan.OperationId, cancellationToken);
                return StoryCompositionCommitResult.RemoteConflict(exception.Message);
            }
            catch (RemoteLocalizationAuthoringException exception) when (
                exception.Failure == RemoteLocalizationAuthoringFailure.LockedByOther)
            {
                await _recoveryStore.DismissAsync(preparedPlan.OperationId, cancellationToken);
                return StoryCompositionCommitResult.Locked(exception.Message);
            }
            catch (RemoteLocalizationAuthoringException exception) when (
                exception.Failure == RemoteLocalizationAuthoringFailure.Network)
            {
                // The request may have committed server-side after the response
                // was lost. The durable plan retains the same operationId, so a
                // recovery retry asks the server for its idempotent receipt.
                return StoryCompositionCommitResult.RecoveryRequired(
                    "Non è stato possibile confermare l'append remoto. Riprova il recovery: non verrà generato un secondo MDV index.",
                    recovery);
            }

            recovery = recovery with
            {
                State = StoryCompositionState.RemoteAppended,
                AssignedIndex = appended.AssignedIndex,
                AssignedEntryPath = appended.EntryPath,
                CanonicalLocalizationContent = appended.Snapshot.Content,
                CurrentVersionId = appended.Snapshot.CurrentVersionId,
                StagingChecksum = appended.Snapshot.StagingChecksum,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await _recoveryStore.UpsertAsync(recovery, cancellationToken);

            try
            {
                await _mirrorWriter.WriteAsync(
                    mirrorPath,
                    Encoding.UTF8.GetBytes(appended.Snapshot.Content),
                    mirrorSnapshot.ContentHash,
                    cancellationToken);
                _localizationService.Invalidate(project.Path, preparedPlan.Locale, preparedPlan.LocalizationFile);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                ExternalDocumentModificationException)
            {
                return StoryCompositionCommitResult.RecoveryRequired(
                    "Lo staging remoto è stato salvato, ma il mirror Localization locale non è sincronizzato.", recovery);
            }

            recovery = recovery with
            {
                State = StoryCompositionState.LocalMirrorSynchronized,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await _recoveryStore.UpsertAsync(recovery, cancellationToken);
            try
            {
                await _commandWriter.InsertAsync(
                    project.Path,
                    preparedPlan,
                    StoryRpgMakerCommandGenerator.CreateCommittedCommands(preparedPlan, appended.EntryPath),
                    cancellationToken);
            }
            catch (StoryLocalSourceConflictException exception)
            {
                recovery = recovery with {State = StoryCompositionState.SourceConflict, UpdatedAt = DateTimeOffset.UtcNow};
                await _recoveryStore.UpsertAsync(recovery, cancellationToken);
                return StoryCompositionCommitResult.RecoveryRequired(exception.Message, recovery);
            }
            catch (StoryCommandListWriteException exception)
            {
                return StoryCompositionCommitResult.RecoveryRequired(exception.Message, recovery);
            }

            await _recoveryStore.MarkCompletedAsync(preparedPlan.OperationId, cancellationToken);
            return StoryCompositionCommitResult.Succeeded(preparedPlan, appended);
        }
        finally
        {
            if (!string.Equals(lockInfo.AcquiredAt, retainedLockAcquiredAt, StringComparison.Ordinal))
            {
                await BestEffortReleaseAsync(project, plan, lockInfo.AcquiredAt, cancellationToken);
            }
        }
    }

    /// <summary>Removes only command range from the event. Its MDV entry is intentionally retained.</summary>
    public async Task<StoryCompositionCommitResult> UnlinkAsync(
        ZiapProject project,
        StoryCommandListTarget target,
        StoryBlock block,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sourcePath = GetSafeEventPath(project.Path, target.SourceFile);
            var snapshot = await _snapshotService.CaptureAsync(sourcePath, cancellationToken);
            await _commandWriter.RemoveAsync(project.Path, target, snapshot, block, cancellationToken);
            return StoryCompositionCommitResult.SucceededWithoutRemote();
        }
        catch (StoryLocalSourceConflictException exception)
        {
            return StoryCompositionCommitResult.SourceConflict(exception.Message);
        }
        catch (StoryCommandListWriteException exception)
        {
            return StoryCompositionCommitResult.InvalidPlan(exception.Message);
        }
    }

    public Task<IReadOnlyList<StoryCompositionRecoveryRecord>> FindRecoveriesAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default) => _recoveryStore.LoadAsync(project.Id, cancellationToken);

    public Task DismissRecoveryAsync(string operationId, CancellationToken cancellationToken = default) =>
        _recoveryStore.DismissAsync(operationId, cancellationToken);

    /// <summary>Completes an existing remote append without calling append again.</summary>
    public async Task<StoryCompositionCommitResult> CompleteRecoveryAsync(
        ZiapProject project,
        StoryCompositionRecoveryRecord recovery,
        StoryCompositionPlan? rebasedPlan = null,
        CancellationToken cancellationToken = default)
    {
        if (recovery.AssignedEntryPath.Count == 0 || string.IsNullOrWhiteSpace(recovery.CanonicalLocalizationContent))
        {
            var resumed = await CommitAsync(project, recovery.Plan, cancellationToken: cancellationToken);
            if (!resumed.IsSourceConflict)
            {
                return resumed;
            }
            var conflicted = recovery with
            {
                State = StoryCompositionState.SourceConflict,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await _recoveryStore.UpsertAsync(conflicted, cancellationToken);
            return StoryCompositionCommitResult.RecoveryRequired(resumed.Message ?? "Source Conflict", conflicted);
        }
        var plan = rebasedPlan ?? recovery.Plan;
        if (plan.OperationType != recovery.Plan.OperationType ||
            !plan.LocalizationBranchPath.SequenceEqual(recovery.Plan.LocalizationBranchPath))
        {
            return StoryCompositionCommitResult.InvalidPlan("Il nuovo anchor non appartiene alla stessa destination Localization.");
        }
        try
        {
            var commands = StoryRpgMakerCommandGenerator.CreateCommittedCommands(plan, recovery.AssignedEntryPath);
            if (rebasedPlan is null && recovery.State == StoryCompositionState.LocalMirrorSynchronized &&
                await _commandWriter.HasInsertionAsync(project.Path, plan, commands, cancellationToken))
            {
                await _recoveryStore.MarkCompletedAsync(recovery.Plan.OperationId, cancellationToken);
                return StoryCompositionCommitResult.SucceededRecovery(plan, recovery);
            }
            await _commandWriter.ValidateInsertionAsync(project.Path, plan, cancellationToken);
            var mirrorPath = GetSafeLocalMirrorPath(project.Path, plan.Locale, plan.LocalizationFile);
            if (recovery.State != StoryCompositionState.LocalMirrorSynchronized &&
                recovery.State != StoryCompositionState.SourceConflict)
            {
                var mirrorSnapshot = recovery.ExpectedLocalizationMirrorSnapshot ??
                    await _snapshotService.CaptureAsync(mirrorPath, cancellationToken);
                await _mirrorWriter.WriteAsync(
                    mirrorPath,
                    Encoding.UTF8.GetBytes(recovery.CanonicalLocalizationContent),
                    mirrorSnapshot.ContentHash,
                    cancellationToken);
            }
            _localizationService.Invalidate(project.Path, plan.Locale, plan.LocalizationFile);
            await _commandWriter.InsertAsync(
                project.Path, plan, commands, cancellationToken);
            await _recoveryStore.MarkCompletedAsync(recovery.Plan.OperationId, cancellationToken);
            return StoryCompositionCommitResult.SucceededRecovery(plan, recovery);
        }
        catch (ExternalDocumentModificationException)
        {
            return StoryCompositionCommitResult.RecoveryRequired("Il mirror Localization è cambiato esternamente.", recovery);
        }
        catch (StoryLocalSourceConflictException exception)
        {
            return StoryCompositionCommitResult.RecoveryRequired(exception.Message, recovery with
            {State = StoryCompositionState.SourceConflict, UpdatedAt = DateTimeOffset.UtcNow});
        }
        catch (StoryCommandListWriteException exception)
        {
            return StoryCompositionCommitResult.InvalidPlan(exception.Message);
        }
    }

    private static StoryCompositionRecoveryRecord NewRecovery(
        string projectId,
        StoryCompositionPlan plan,
        ZiapStudio.Core.Editing.DocumentSourceSnapshot mirrorSnapshot) => new()
    {
        ProjectId = projectId,
        Plan = plan,
        State = StoryCompositionState.Planned,
        ExpectedLocalizationMirrorSnapshot = mirrorSnapshot,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private async Task BestEffortReleaseAsync(
        ZiapProject project,
        StoryCompositionPlan plan,
        string? acquiredAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await _remoteClient.ReleaseLockAsync(
                project.Id, plan.Locale, plan.LocalizationFile, acquiredAt, cancellationToken);
        }
        catch (RemoteLocalizationAuthoringException)
        {
            // A lock expiry is intentionally the durable fallback after a partial operation.
        }
    }

    private static string GetSafeLocalMirrorPath(string projectPath, string locale, string sourceFile)
    {
        var root = Path.GetFullPath(Path.Combine(projectPath, "locales", locale));
        var candidate = Path.GetFullPath(Path.Combine(root, sourceFile));
        EnsureUnderRoot(root, candidate, "Il mirror Localization è fuori dal progetto.");
        return candidate;
    }

    private static string GetSafeEventPath(string projectPath, string sourceFile)
    {
        var root = Path.GetFullPath(Path.Combine(projectPath, "data"));
        var candidate = Path.GetFullPath(Path.Combine(projectPath, sourceFile));
        EnsureUnderRoot(root, candidate, "La sorgente RPG Maker è fuori dal progetto.");
        return candidate;
    }

    private static void EnsureUnderRoot(string root, string candidate, string message)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed record StoryCompositionCommitResult
{
    public bool IsSuccess { get; init; }
    public bool IsLocked { get; init; }
    public bool IsRemoteConflict { get; init; }
    public bool IsSourceConflict { get; init; }
    public bool RequiresRecovery { get; init; }
    public string? Message { get; init; }
    public StoryCompositionPlan? Plan { get; init; }
    public LocalizationAppendResult? Append { get; init; }
    public StoryCompositionRecoveryRecord? Recovery { get; init; }

    public static StoryCompositionCommitResult Succeeded(StoryCompositionPlan plan, LocalizationAppendResult append) =>
        new() {IsSuccess = true, Plan = plan, Append = append};
    public static StoryCompositionCommitResult SucceededRecovery(StoryCompositionPlan plan, StoryCompositionRecoveryRecord recovery) =>
        new() {IsSuccess = true, Plan = plan, Recovery = recovery};
    public static StoryCompositionCommitResult SucceededWithoutRemote() => new() {IsSuccess = true};
    public static StoryCompositionCommitResult Locked(string message) => new() {IsLocked = true, Message = message};
    public static StoryCompositionCommitResult RemoteConflict(string message) => new() {IsRemoteConflict = true, Message = message};
    public static StoryCompositionCommitResult SourceConflict(string message) => new() {IsSourceConflict = true, Message = message};
    public static StoryCompositionCommitResult InvalidPlan(string message) => new() {Message = message};
    public static StoryCompositionCommitResult RecoveryRequired(string message, StoryCompositionRecoveryRecord recovery) =>
        new() {RequiresRecovery = true, Message = message, Recovery = recovery};
}
