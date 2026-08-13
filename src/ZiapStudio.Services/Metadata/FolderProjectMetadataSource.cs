namespace ZiapStudio.Services.Metadata;

internal sealed class FolderProjectMetadataSource : IProjectMetadataSource
{
    private readonly FileSystemService _fileSystem;

    public FolderProjectMetadataSource(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public Task<ProjectMetadataContribution?> ReadAsync(
        ProjectDetection detection,
        CancellationToken cancellationToken)
    {
        var directoryName = _fileSystem.GetDirectoryName(detection.ProjectPath);
        return Task.FromResult<ProjectMetadataContribution?>(new ProjectMetadataContribution
        {
            Id = directoryName,
            Name = directoryName,
        });
    }
}
