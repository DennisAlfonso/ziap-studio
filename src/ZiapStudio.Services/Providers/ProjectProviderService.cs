using ZiapStudio.Core.Models;
using ZiapStudio.Services.Integrations;

namespace ZiapStudio.Services.Providers;

public sealed class ProjectProviderService
{
    private readonly IReadOnlyList<IProjectProvider> _providers;
    private readonly ProjectIntegrationService _integrationService;

    public ProjectProviderService(
        FileSystemService fileSystem,
        ProjectIntegrationService? integrationService = null)
    {
        var fileSystemTreeBuilder = new FileSystemTreeBuilder(fileSystem);
        _providers =
        [
            new RpgMakerMzProjectProvider(fileSystem, fileSystemTreeBuilder),
            new GenericProjectProvider(fileSystemTreeBuilder),
        ];
        var pluginRegistry = new RpgMakerPluginRegistryService(fileSystem);
        _integrationService = integrationService ?? new ProjectIntegrationService(
        [
            new FusionAudioIntegrationProvider(pluginRegistry),
            new FusionBossIntegrationProvider(pluginRegistry),
            new FusionPuzzleIntegrationProvider(pluginRegistry),
            new FusionWorldIntegrationProvider(fileSystem),
        ]);
    }

    public async Task<IReadOnlyList<ProjectExplorerNode>> BuildExplorerAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var provider = _providers.First(candidate => candidate.CanHandle(project));
        var roots = await provider.BuildExplorerAsync(project, cancellationToken);
        return await _integrationService.AddExplorerNodesAsync(
            project,
            roots,
            cancellationToken);
    }
}
