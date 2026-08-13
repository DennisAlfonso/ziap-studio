using System.Security.Cryptography;
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

        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Cartella di destinazione non valida.");
        _fileSystem.CreateDirectory(directory);
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
            using var temporaryDocument = JsonDocument.Parse(temporaryBytes);
            if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(contents.Span),
                    SHA256.HashData(temporaryBytes)))
            {
                throw new IOException("La verifica del file temporaneo non è riuscita.");
            }
            if (_fileSystem.FileExists(destinationPath))
            {
                throw new ExternalDocumentModificationException(
                    $"{Path.GetFileName(destinationPath)} è apparso durante il download.");
            }
            _fileSystem.MoveFile(temporaryPath, destinationPath, overwrite: false);
            return true;
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
