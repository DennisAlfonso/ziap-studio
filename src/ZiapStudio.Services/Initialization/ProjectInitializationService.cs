using System.Text.Json;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Metadata;

namespace ZiapStudio.Services.Initialization;

public sealed class ProjectInitializationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly FileSystemService _fileSystem;
    private readonly ProjectIdGenerator _projectIdGenerator;

    public ProjectInitializationService(
        FileSystemService fileSystem,
        ProjectIdGenerator projectIdGenerator)
    {
        _fileSystem = fileSystem;
        _projectIdGenerator = projectIdGenerator;
    }

    public async Task InitializeAsync(
        string projectPath,
        ProjectInitializationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!_fileSystem.DirectoryExists(projectPath))
        {
            throw new ProjectInitializationException("La cartella del progetto non esiste più.");
        }

        Validate(options);

        var metadataPath = Path.Combine(
            projectPath,
            ZiapProjectMetadataSource.MetadataRelativePath);
        if (_fileSystem.FileExists(metadataPath))
        {
            throw new ProjectInitializationException(
                "Il progetto è già inizializzato. L'ID esistente non verrà sovrascritto.");
        }

        var metadataDirectory = Path.GetDirectoryName(metadataPath)
            ?? throw new InvalidOperationException("Il percorso metadata non ha una cartella padre.");
        var temporaryPath = Path.Combine(
            metadataDirectory,
            $"project.{Guid.NewGuid():N}.tmp");
        var metadata = new ProjectMetadata
        {
            SchemaVersion = ProjectMetadata.CurrentSchemaVersion,
            Id = options.Id.Trim(),
            Name = options.Name.Trim(),
            Type = options.Type.Trim(),
            Version = NullIfWhiteSpace(options.Version),
            Publisher = NullIfWhiteSpace(options.Publisher),
        };

        try
        {
            _fileSystem.CreateDirectory(metadataDirectory);
            var json = JsonSerializer.Serialize(metadata, JsonOptions);
            await _fileSystem.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            _fileSystem.MoveFile(temporaryPath, metadataPath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProjectInitializationException(
                "Impossibile creare .ziap/project.json nella cartella del progetto.",
                exception);
        }
        finally
        {
            if (_fileSystem.FileExists(temporaryPath))
            {
                _fileSystem.DeleteFile(temporaryPath);
            }
        }
    }

    private void Validate(ProjectInitializationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            throw new ProjectInitializationException("Il nome del progetto è obbligatorio.");
        }

        if (!_projectIdGenerator.IsValid(options.Id))
        {
            throw new ProjectInitializationException(
                "L'ID deve contenere solo lettere minuscole, numeri e trattini singoli.");
        }

        if (string.IsNullOrWhiteSpace(options.Type))
        {
            throw new ProjectInitializationException("Il tipo di progetto è obbligatorio.");
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
