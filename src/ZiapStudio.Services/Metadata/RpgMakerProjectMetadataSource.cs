using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Metadata;

internal sealed class RpgMakerProjectMetadataSource : IProjectMetadataSource
{
    private const string SystemMetadataRelativePath = "data/System.json";

    private readonly FileSystemService _fileSystem;

    public RpgMakerProjectMetadataSource(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<ProjectMetadataContribution?> ReadAsync(
        ProjectDetection detection,
        CancellationToken cancellationToken)
    {
        if (detection.DetectedProjectType is not (KnownProjectTypes.RpgMakerMz or KnownProjectTypes.RpgMakerMv))
        {
            return null;
        }

        var systemMetadataPath = Path.Combine(detection.ProjectPath, SystemMetadataRelativePath);
        if (!_fileSystem.FileExists(systemMetadataPath))
        {
            return new ProjectMetadataContribution
            {
                ProjectType = detection.DetectedProjectType,
            };
        }

        var metadata = await MetadataJsonReader.ReadRequiredAsync<RpgMakerSystemMetadata>(
            _fileSystem,
            systemMetadataPath,
            SystemMetadataRelativePath,
            cancellationToken);

        return new ProjectMetadataContribution
        {
            Name = metadata.GameTitle,
            ProjectType = detection.DetectedProjectType,
        };
    }

    private sealed class RpgMakerSystemMetadata
    {
        public string? GameTitle { get; init; }
    }
}
