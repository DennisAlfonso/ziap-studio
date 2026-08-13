using System.Text.Json;
using System.Security.Cryptography;

namespace ZiapStudio.Services.Editing;

public sealed class AtomicJsonFileWriter
{
    private readonly FileSystemService _fileSystem;

    public AtomicJsonFileWriter(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task WriteAsync(
        string destinationPath,
        ReadOnlyMemory<byte> contents,
        string expectedContentHash,
        CancellationToken cancellationToken = default)
    {
        var temporaryPath = $"{destinationPath}.ziap-tmp";
        try
        {
            await _fileSystem.WriteAllBytesWithFlushAsync(
                temporaryPath,
                contents,
                cancellationToken);

            var temporaryBytes = await _fileSystem.ReadAllBytesAsync(
                temporaryPath,
                cancellationToken);
            using var validationDocument = JsonDocument.Parse(temporaryBytes);

            var currentBytes = await _fileSystem.ReadAllBytesAsync(
                destinationPath,
                cancellationToken);
            var currentHash = Convert.ToHexString(SHA256.HashData(currentBytes));
            if (!string.Equals(currentHash, expectedContentHash, StringComparison.Ordinal))
            {
                throw new ExternalDocumentModificationException(
                    $"{Path.GetFileName(destinationPath)} è stato modificato esternamente.");
            }

            _fileSystem.MoveFile(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (_fileSystem.FileExists(temporaryPath))
            {
                _fileSystem.DeleteFile(temporaryPath);
            }
        }
    }
}
