namespace ZiapStudio.Services.Metadata;

internal interface IProjectMetadataSource
{
    Task<ProjectMetadataContribution?> ReadAsync(
        ProjectDetection detection,
        CancellationToken cancellationToken);
}
