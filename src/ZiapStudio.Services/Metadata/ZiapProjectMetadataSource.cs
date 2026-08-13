using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Metadata;

internal sealed class ZiapProjectMetadataSource : IProjectMetadataSource
{
    public const string MetadataRelativePath = ".ziap/project.json";

    private readonly FileSystemService _fileSystem;

    public ZiapProjectMetadataSource(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<ProjectMetadataContribution?> ReadAsync(
        ProjectDetection detection,
        CancellationToken cancellationToken)
    {
        if (!detection.HasZiapMetadata)
        {
            return null;
        }

        var metadata = await MetadataJsonReader.ReadRequiredAsync<ProjectMetadata>(
            _fileSystem,
            Path.Combine(detection.ProjectPath, MetadataRelativePath),
            MetadataRelativePath,
            cancellationToken);

        if (metadata.SchemaVersion < 1)
        {
            throw new ProjectLoadException(
                "Il metadata ZIAP deve dichiarare schemaVersion con valore almeno 1.");
        }

        if (metadata.SchemaVersion > ProjectMetadata.CurrentSchemaVersion)
        {
            throw new ProjectLoadException(
                $"Il metadata usa schemaVersion {metadata.SchemaVersion}, ma questa versione di ZIAP Studio supporta fino alla {ProjectMetadata.CurrentSchemaVersion}.");
        }

        return new ProjectMetadataContribution
        {
            Id = metadata.Id,
            Name = metadata.Name,
            Version = metadata.Version,
            ProjectType = metadata.Type,
            Publisher = metadata.Publisher,
        };
    }
}
