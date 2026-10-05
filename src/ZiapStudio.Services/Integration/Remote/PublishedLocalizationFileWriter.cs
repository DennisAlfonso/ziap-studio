using System.Text.Json;
using ZiapStudio.Services.Editing;

namespace ZiapStudio.Services.Integration.Remote;

public sealed class PublishedLocalizationFileWriter
{
    private readonly FileSystemService _fileSystem;
    private readonly AtomicJsonFileWriter _atomicWriter;

    public PublishedLocalizationFileWriter(
        FileSystemService fileSystem,
        AtomicJsonFileWriter atomicWriter)
    {
        _fileSystem = fileSystem;
        _atomicWriter = atomicWriter;
    }

    public async Task<bool> WriteAsync(
        string destinationPath,
        ReadOnlyMemory<byte> contents,
        string? expectedCurrentHash,
        CancellationToken cancellationToken = default)
    {
        using var validationDocument = JsonDocument.Parse(contents);
        var destinationExists = _fileSystem.FileExists(destinationPath);
        if (destinationExists)
        {
            if (string.IsNullOrWhiteSpace(expectedCurrentHash))
            {
                throw new ExternalDocumentModificationException(
                    $"{Path.GetFileName(destinationPath)} è apparso dopo il confronto.");
            }
            await _atomicWriter.WriteAsync(
                destinationPath,
                contents,
                expectedCurrentHash,
                cancellationToken);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedCurrentHash))
        {
            throw new ExternalDocumentModificationException(
                $"{Path.GetFileName(destinationPath)} è stato rimosso dopo il confronto.");
        }

        await _atomicWriter.WriteNewAsync(destinationPath, contents, cancellationToken);
        return true;
    }
}
