using ZiapStudio.Core.Editing;

namespace ZiapStudio.Services.Editing;

public sealed class ExternalModificationDetector
{
    private readonly FileSystemService _fileSystem;
    private readonly DocumentSnapshotService _snapshotService;

    public ExternalModificationDetector(
        FileSystemService fileSystem,
        DocumentSnapshotService snapshotService)
    {
        _fileSystem = fileSystem;
        _snapshotService = snapshotService;
    }

    public async Task<bool> HasChangedAsync(
        DocumentSourceSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.FileExists(snapshot.SourcePath))
        {
            return true;
        }

        var current = await _snapshotService.CaptureAsync(
            snapshot.SourcePath,
            cancellationToken);
        return !string.Equals(
            current.ContentHash,
            snapshot.ContentHash,
            StringComparison.Ordinal);
    }
}
