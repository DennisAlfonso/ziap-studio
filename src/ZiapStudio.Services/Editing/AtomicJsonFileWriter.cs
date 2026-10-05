using System.Text.Json;
using System.Security.Cryptography;
using ZiapStudio.Services.ProjectSafety;

namespace ZiapStudio.Services.Editing;

public sealed class AtomicJsonFileWriter
{
    private readonly FileSystemService _fileSystem;
    private readonly ProjectWriteCoordinator? _coordinator;

    public AtomicJsonFileWriter(
        FileSystemService fileSystem,
        ProjectWriteCoordinator? coordinator = null)
    {
        _fileSystem = fileSystem;
        _coordinator = coordinator;
    }

    public async Task WriteAsync(
        string destinationPath,
        ReadOnlyMemory<byte> contents,
        string expectedContentHash,
        CancellationToken cancellationToken = default)
    {
        if (_coordinator is not null)
        {
            await _coordinator.WriteAsync(new ProjectWriteRequest
            {
                ProjectRoot = RequireActiveProjectRoot(),
                TargetPath = destinationPath,
                Operation = ProjectWriteOperation.ReplaceExisting,
                Contents = contents,
                ExpectedContentHash = expectedContentHash,
                ValidateJson = true,
            }, cancellationToken);
            return;
        }

        await WriteExistingWithoutCoordinatorAsync(destinationPath, contents, expectedContentHash, cancellationToken);
    }

    public async Task WriteNewAsync(
        string destinationPath,
        ReadOnlyMemory<byte> contents,
        CancellationToken cancellationToken = default)
    {
        if (_coordinator is not null)
        {
            await _coordinator.WriteAsync(new ProjectWriteRequest
            {
                ProjectRoot = RequireActiveProjectRoot(),
                TargetPath = destinationPath,
                Operation = ProjectWriteOperation.CreateNew,
                Contents = contents,
                ValidateJson = true,
            }, cancellationToken);
            return;
        }

        var directory = Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("Destinazione non valida.");
        _fileSystem.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.ziap-tmp-{Guid.NewGuid():N}");
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

            if (_fileSystem.FileExists(destinationPath))
            {
                throw new ExternalDocumentModificationException(
                    $"{Path.GetFileName(destinationPath)} è stato creato esternamente.");
            }

            _fileSystem.MoveFile(temporaryPath, destinationPath, overwrite: false);
        }
        finally
        {
            if (_fileSystem.FileExists(temporaryPath))
            {
                _fileSystem.DeleteFile(temporaryPath);
            }
        }
    }

    private async Task WriteExistingWithoutCoordinatorAsync(
        string destinationPath,
        ReadOnlyMemory<byte> contents,
        string expectedContentHash,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("Destinazione non valida."),
            $".{Path.GetFileName(destinationPath)}.ziap-tmp-{Guid.NewGuid():N}");
        try
        {
            await _fileSystem.WriteAllBytesWithFlushAsync(temporaryPath, contents, cancellationToken);
            var temporaryBytes = await _fileSystem.ReadAllBytesAsync(temporaryPath, cancellationToken);
            using var validationDocument = JsonDocument.Parse(temporaryBytes);

            var currentBytes = await _fileSystem.ReadAllBytesAsync(destinationPath, cancellationToken);
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

    private string RequireActiveProjectRoot() => _coordinator?.ActiveProjectRoot ?? throw new ProjectWriteException(
        ProjectWriteFailure.InvalidProjectPath,
        "Nessun progetto è registrato nel coordinatore di scrittura.");
}
