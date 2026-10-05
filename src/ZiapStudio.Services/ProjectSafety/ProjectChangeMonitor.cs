using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ZiapStudio.Services.ProjectSafety;

public enum ProjectChangeKind
{
    Changed,
    Created,
    Deleted,
    Renamed,
}

public enum ProjectChangeOrigin
{
    SelfWrite,
    ExternalWrite,
}

public sealed record ProjectFileChange(
    ProjectChangeKind Kind,
    string Path,
    string? OldPath = null);

public sealed record ProjectChangeNotification(
    ProjectFileChange Change,
    ProjectChangeOrigin Origin,
    string? ContentHash);

/// <summary>
/// FileSystemWatcher feeds UI invalidation and stale markers only. The write
/// coordinator still re-reads and hashes targets before every commit.
/// </summary>
public sealed class ProjectChangeMonitor : IDisposable
{
    private readonly FileSystemService _fileSystem;
    private readonly SelfWriteRegistry _selfWrites;
    private readonly bool _startWatching;
    private readonly ConcurrentDictionary<string, ProjectFileChange> _pending = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private string? _projectRoot;
    private int _disposed;

    public ProjectChangeMonitor(
        FileSystemService fileSystem,
        SelfWriteRegistry selfWrites,
        bool startWatching = true)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _selfWrites = selfWrites ?? throw new ArgumentNullException(nameof(selfWrites));
        _startWatching = startWatching;
    }

    public event EventHandler<ProjectChangeNotification>? Changed;

    public void OpenProject(string projectRoot)
    {
        ThrowIfDisposed();
        CloseProject();
        _projectRoot = ProjectPathSafety.NormalizeProjectRoot(projectRoot);
        if (!_startWatching)
        {
            return;
        }
        _watcher = new FileSystemWatcher(_projectRoot)
        {
            IncludeSubdirectories = true,
            Filter = "*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, args) => Queue(ProjectChangeKind.Changed, args.FullPath);
        _watcher.Created += (_, args) => Queue(ProjectChangeKind.Created, args.FullPath);
        _watcher.Deleted += (_, args) => Queue(ProjectChangeKind.Deleted, args.FullPath);
        _watcher.Renamed += (_, args) => Queue(ProjectChangeKind.Renamed, args.FullPath, args.OldFullPath);
        _watcher.Error += (_, _) => { /* watcher is advisory; the next write still hashes live content. */ };
    }

    public void CloseProject()
    {
        _watcher?.Dispose();
        _watcher = null;
        _projectRoot = null;
        _pending.Clear();
    }

    public void Queue(ProjectChangeKind kind, string path, string? oldPath = null)
    {
        if (_projectRoot is null || IsIgnored(path))
        {
            return;
        }

        try
        {
            var normalized = ProjectPathSafety.NormalizeContainedPath(_projectRoot, path);
            var oldNormalized = oldPath is null ? null : ProjectPathSafety.NormalizeContainedPath(_projectRoot, oldPath);
            var change = new ProjectFileChange(kind, normalized, oldNormalized);
            // Coalescing is semantic, not a timing exemption: each distinct
            // final path/kind is processed once during the next UI flush.
            _pending[$"{kind}|{normalized}|{oldNormalized}"] = change;
        }
        catch (ProjectWriteException)
        {
            // A watcher can report a path already gone or outside a just-closed
            // root. It must never make a target path trusted.
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var pending = _pending.ToArray();
        _pending.Clear();
        foreach (var pair in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var change = pair.Value;
            string? hash = null;
            if (change.Kind != ProjectChangeKind.Deleted && _fileSystem.FileExists(change.Path))
            {
                hash = Convert.ToHexString(SHA256.HashData(
                    await _fileSystem.ReadAllBytesAsync(change.Path, cancellationToken)));
            }

            var origin = hash is not null && _selfWrites.IsSelfWrite(change.Path, hash)
                ? ProjectChangeOrigin.SelfWrite
                : ProjectChangeOrigin.ExternalWrite;
            Changed?.Invoke(this, new ProjectChangeNotification(change, origin, hash));
        }
    }

    private static bool IsIgnored(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains($"{Path.DirectorySeparatorChar}.ziap{Path.DirectorySeparatorChar}.write-locks{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(".ziap-tmp-", StringComparison.OrdinalIgnoreCase);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(ProjectChangeMonitor));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            CloseProject();
        }
    }
}
