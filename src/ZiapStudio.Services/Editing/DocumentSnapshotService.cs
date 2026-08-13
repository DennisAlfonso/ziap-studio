using System.Security.Cryptography;
using ZiapStudio.Core.Editing;

namespace ZiapStudio.Services.Editing;

public sealed class DocumentSnapshotService
{
    private readonly FileSystemService _fileSystem;

    public DocumentSnapshotService(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<DocumentSourceSnapshot> CaptureAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var bytes = await _fileSystem.ReadAllBytesAsync(sourcePath, cancellationToken);
        return new DocumentSourceSnapshot
        {
            SourcePath = sourcePath,
            LoadedAtUtc = DateTimeOffset.UtcNow,
            LastWriteTimeUtc = _fileSystem.GetLastWriteTimeUtc(sourcePath),
            Length = bytes.LongLength,
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)),
        };
    }
}
