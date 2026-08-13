using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Providers;

public sealed class ProjectProviderService
{
    private readonly IReadOnlyList<IProjectProvider> _providers;

    public ProjectProviderService(FileSystemService fileSystem)
    {
        var fileSystemTreeBuilder = new FileSystemTreeBuilder(fileSystem);
        _providers =
        [
            new RpgMakerMzProjectProvider(fileSystem, fileSystemTreeBuilder),
            new GenericProjectProvider(fileSystemTreeBuilder),
        ];
    }

    public Task<IReadOnlyList<ProjectExplorerNode>> BuildExplorerAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var provider = _providers.First(candidate => candidate.CanHandle(project));
        return provider.BuildExplorerAsync(project, cancellationToken);
    }
}
