using System.Security.Cryptography;
using System.Text.Json;
using ZiapStudio.Core.Editing;

namespace ZiapStudio.Services.ProjectSafety;

/// <summary>
/// The sole authority for a final mutation below an opened project root.
/// It combines ownership policy, RPG Maker coexistence, live SHA-256 checks,
/// cross-process coordination, atomic replacement, and watcher correlation.
/// </summary>
public sealed class ProjectWriteCoordinator
{
    private readonly FileSystemService _fileSystem;
    private readonly ProjectFileClassifier _classifier;
    private readonly IRpgMakerProcessMonitor _processMonitor;
    private readonly SelfWriteRegistry _selfWrites;
    private readonly TimeSpan _leaseTimeout;
    private readonly TimeSpan _leaseRetryDelay;
    private readonly Dictionary<string, DocumentSourceSnapshot> _trackedSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeProjectRoot;
    private ProjectSafetyState _safetyState;

    public ProjectWriteCoordinator(
        FileSystemService fileSystem,
        ProjectFileClassifier? classifier = null,
        IRpgMakerProcessMonitor? processMonitor = null,
        SelfWriteRegistry? selfWrites = null,
        TimeSpan? leaseTimeout = null,
        TimeSpan? leaseRetryDelay = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _classifier = classifier ?? new ProjectFileClassifier();
        _processMonitor = processMonitor ?? new RpgMakerProcessMonitor(new SystemRpgMakerProcessProvider());
        _selfWrites = selfWrites ?? new SelfWriteRegistry();
        _leaseTimeout = leaseTimeout ?? TimeSpan.FromSeconds(2);
        _leaseRetryDelay = leaseRetryDelay ?? TimeSpan.FromMilliseconds(50);
        if (_leaseTimeout <= TimeSpan.Zero || _leaseRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseTimeout));
        }
        _processMonitor.RunningChanged += ProcessMonitor_RunningChanged;
    }

    public event EventHandler<ProjectSafetyState>? SafetyStateChanged;

    public string? ActiveProjectRoot => _activeProjectRoot;

    public SelfWriteRegistry SelfWrites => _selfWrites;

    public ProjectSafetyState SafetyState
    {
        get
        {
            _processMonitor.Refresh();
            return _safetyState;
        }
    }

    public void OpenProject(string projectRoot)
    {
        _activeProjectRoot = ProjectPathSafety.NormalizeProjectRoot(projectRoot);
        _trackedSnapshots.Clear();
        SetSafetyState(_processMonitor.IsRunning
            ? ProjectSafetyState.ProtectedCoexistence
            : ProjectSafetyState.Safe);
    }

    public void CloseProject()
    {
        _activeProjectRoot = null;
        _trackedSnapshots.Clear();
        SetSafetyState(ProjectSafetyState.Safe);
    }

    /// <summary>
    /// After RPG Maker closes, callers pass their tracked snapshots. No dirty
    /// document is silently rebased: a changed/deleted source remains a
    /// conflict and protected mode stays in place.
    /// </summary>
    public async Task RevalidateAsync(
        string projectRoot,
        IEnumerable<DocumentSourceSnapshot> trackedSnapshots,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoot = ProjectPathSafety.NormalizeProjectRoot(projectRoot);
        if (!string.Equals(normalizedRoot, _activeProjectRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.InvalidProjectPath,
                "Il progetto non è quello attualmente aperto in Studio.");
        }

        _processMonitor.Refresh();
        if (_processMonitor.IsRunning)
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.RpgMakerActive,
                "RPG Maker MZ è aperto: la rivalidazione è sospesa.");
        }

        var snapshots = _trackedSnapshots.Values
            .Concat(trackedSnapshots)
            .GroupBy(snapshot => Path.GetFullPath(snapshot.SourcePath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
        foreach (var snapshot in snapshots)
        {
            var path = ProjectPathSafety.NormalizeContainedPath(normalizedRoot, snapshot.SourcePath);
            if (!_fileSystem.FileExists(path) ||
                !string.Equals(await ComputeHashAsync(path, cancellationToken), snapshot.ContentHash, StringComparison.Ordinal))
            {
                throw new ProjectWriteException(
                    ProjectWriteFailure.ExternalModification,
                    $"{Path.GetFileName(path)} è cambiato esternamente e richiede una ricarica.");
            }
        }

        SetSafetyState(ProjectSafetyState.Safe);
    }

    public void TrackSnapshot(DocumentSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_activeProjectRoot is null)
        {
            return;
        }

        var path = ProjectPathSafety.NormalizeContainedPath(_activeProjectRoot, snapshot.SourcePath);
        _trackedSnapshots[path] = snapshot with { SourcePath = path };
    }

    public async Task<ProjectWriteResult> WriteAsync(
        ProjectWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = ProjectPathSafety.NormalizeProjectRoot(request.ProjectRoot);
        var target = ProjectPathSafety.NormalizeContainedPath(root, request.TargetPath);
        var ownership = _classifier.Classify(root, target);
        EnsureCurrentProject(root);
        _processMonitor.Refresh();
        EnsureCoexistenceAllowsWrite(ownership, target);
        ValidateRequest(request);

        try
        {
            await using var lease = await AcquireLeaseAsync(root, cancellationToken);
            // Lease acquisition is not the concurrency authority; hash checks
            // happen after it and immediately before replacement.
            await AssertLiveTargetAsync(target, request.Operation, request.ExpectedContentHash, cancellationToken);

            var directory = Path.GetDirectoryName(target) ?? throw new ProjectWriteException(
                ProjectWriteFailure.InvalidProjectPath,
                "La destinazione non ha una cartella padre.");
            Directory.CreateDirectory(directory);
            ProjectPathSafety.NormalizeContainedPath(root, target);

            var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.ziap-tmp-{Guid.NewGuid():N}");
            try
            {
                await _fileSystem.WriteAllBytesWithFlushAsync(temporary, request.Contents, cancellationToken);
                var temporaryBytes = await _fileSystem.ReadAllBytesAsync(temporary, cancellationToken);
                if (request.ValidateJson)
                {
                    using var document = JsonDocument.Parse(temporaryBytes);
                }
                if (request.Validator is not null)
                {
                    await request.Validator(temporaryBytes, cancellationToken);
                }

                var resultingHash = Convert.ToHexString(SHA256.HashData(temporaryBytes));
                // Mandatory second live check narrows the TOCTOU window.
                ProjectPathSafety.NormalizeContainedPath(root, target);
                await AssertLiveTargetAsync(target, request.Operation, request.ExpectedContentHash, cancellationToken);
                var selfWriteOperation = _selfWrites.RegisterPending(target, resultingHash);
                _fileSystem.MoveFile(temporary, target, overwrite: request.Operation == ProjectWriteOperation.ReplaceExisting);

                var committedHash = await ComputeHashAsync(target, cancellationToken);
                if (!string.Equals(committedHash, resultingHash, StringComparison.Ordinal))
                {
                    throw new ProjectWriteException(
                        ProjectWriteFailure.AtomicWriteFailed,
                        $"La verifica dopo il salvataggio di {Path.GetFileName(target)} non è riuscita.");
                }

                _selfWrites.MarkCommitted(target, selfWriteOperation);
                var bytes = await _fileSystem.ReadAllBytesAsync(target, cancellationToken);
                var snapshot = new DocumentSourceSnapshot
                {
                    SourcePath = target,
                    LoadedAtUtc = DateTimeOffset.UtcNow,
                    LastWriteTimeUtc = _fileSystem.GetLastWriteTimeUtc(target),
                    Length = bytes.LongLength,
                    ContentHash = committedHash,
                };
                _trackedSnapshots[target] = snapshot;
                return new ProjectWriteResult(
                    target,
                    ownership,
                    snapshot);
            }
            finally
            {
                if (_fileSystem.FileExists(temporary))
                {
                    _fileSystem.DeleteFile(temporary);
                }
            }
        }
        catch (ProjectWriteException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.AtomicWriteFailed,
                $"Il salvataggio atomico di {Path.GetFileName(target)} non è riuscito.",
                exception);
        }
    }

    public bool IsSelfWrite(string path, string actualContentHash) => _selfWrites.IsSelfWrite(path, actualContentHash);

    private void EnsureCurrentProject(string root)
    {
        if (_activeProjectRoot is null)
        {
            OpenProject(root);
            return;
        }

        if (!string.Equals(_activeProjectRoot, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.InvalidProjectPath,
                "La mutazione non appartiene al progetto attualmente aperto.");
        }
    }

    private void EnsureCoexistenceAllowsWrite(ProjectFileOwnership ownership, string target)
    {
        if (ownership != ProjectFileOwnership.RpgMakerOwned)
        {
            return;
        }

        if (_safetyState == ProjectSafetyState.ProtectedCoexistence)
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.RpgMakerActive,
                $"{Path.GetFileName(target)} non può essere modificato mentre RPG Maker MZ è aperto. Chiudi RPG Maker e rivalida il progetto prima di salvare.");
        }
        if (_safetyState == ProjectSafetyState.RevalidationRequired)
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.RevalidationRequired,
                $"{Path.GetFileName(target)} richiede rivalidazione dopo la chiusura di RPG Maker MZ.");
        }
    }

    private static void ValidateRequest(ProjectWriteRequest request)
    {
        if (request.Operation == ProjectWriteOperation.ReplaceExisting &&
            string.IsNullOrWhiteSpace(request.ExpectedContentHash))
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.ValidationFailed,
                "Ogni sostituzione richiede l'hash della snapshot attesa.");
        }
        if (request.Operation == ProjectWriteOperation.CreateNew &&
            !string.IsNullOrWhiteSpace(request.ExpectedContentHash))
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.ValidationFailed,
                "Una creazione non può dichiarare un hash di file preesistente.");
        }
    }

    private async Task AssertLiveTargetAsync(
        string target,
        ProjectWriteOperation operation,
        string? expectedHash,
        CancellationToken cancellationToken)
    {
        var exists = _fileSystem.FileExists(target);
        if (operation == ProjectWriteOperation.CreateNew)
        {
            if (exists)
            {
                throw new ProjectWriteException(
                    ProjectWriteFailure.ExternalModification,
                    $"{Path.GetFileName(target)} è stato creato da un altro writer.");
            }
            return;
        }

        if (!exists)
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.ExternalModification,
                $"{Path.GetFileName(target)} è stato rimosso da un altro writer.");
        }

        var currentHash = await ComputeHashAsync(target, cancellationToken);
        if (!string.Equals(currentHash, expectedHash, StringComparison.Ordinal))
        {
            throw new ProjectWriteException(
                ProjectWriteFailure.ExternalModification,
                $"{Path.GetFileName(target)} è stato modificato esternamente.");
        }
    }

    private async Task<FileStream> AcquireLeaseAsync(string root, CancellationToken cancellationToken)
    {
        var locksDirectory = Path.Combine(root, ".ziap", ".write-locks");
        Directory.CreateDirectory(locksDirectory);
        var rootHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root)));
        var lockPath = Path.Combine(locksDirectory, rootHash + ".lock");
        var deadline = DateTimeOffset.UtcNow + _leaseTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(_leaseRetryDelay, cancellationToken);
            }
            catch (IOException exception)
            {
                throw new ProjectWriteException(
                    ProjectWriteFailure.ConcurrentStudioWriter,
                    "Un'altra istanza di ZIAP Studio sta salvando questo progetto. Riprova tra poco.",
                    exception);
            }
        }
    }

    private async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken) =>
        Convert.ToHexString(SHA256.HashData(await _fileSystem.ReadAllBytesAsync(path, cancellationToken)));

    private void ProcessMonitor_RunningChanged(object? sender, bool isRunning)
    {
        if (_activeProjectRoot is null)
        {
            return;
        }

        SetSafetyState(isRunning
            ? ProjectSafetyState.ProtectedCoexistence
            : ProjectSafetyState.RevalidationRequired);
    }

    private void SetSafetyState(ProjectSafetyState state)
    {
        if (_safetyState == state)
        {
            return;
        }

        _safetyState = state;
        SafetyStateChanged?.Invoke(this, state);
    }
}
