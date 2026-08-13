namespace ZiapStudio.Services.Metadata;

internal sealed class PackageProjectMetadataSource : IProjectMetadataSource
{
    private const string PackageFileName = "package.json";

    private readonly FileSystemService _fileSystem;

    public PackageProjectMetadataSource(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<ProjectMetadataContribution?> ReadAsync(
        ProjectDetection detection,
        CancellationToken cancellationToken)
    {
        if (!detection.HasPackage)
        {
            return null;
        }

        var metadata = await MetadataJsonReader.ReadRequiredAsync<PackageMetadata>(
            _fileSystem,
            Path.Combine(detection.ProjectPath, PackageFileName),
            PackageFileName,
            cancellationToken);

        return new ProjectMetadataContribution
        {
            PackageName = metadata.Name,
            Version = metadata.Version,
            ProjectType = detection.DetectedProjectType,
        };
    }

    private sealed class PackageMetadata
    {
        public string? Name { get; init; }

        public string? Version { get; init; }
    }
}
