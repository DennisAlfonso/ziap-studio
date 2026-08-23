using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Integrations;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Integrations;

public sealed class FusionPuzzleIntegrationProvider : IProjectIntegrationProvider
{
    public const string ResourceUri = "fusionpuzzle://workspace/";
    public const string CorePluginName = "ZDP_FusionPuzzle";
    public const string MovementPluginName = "ZDP_FusionPuzzle_Movement";
    public const string HexellaWeightPluginName = "ZDP_FusionPuzzle_HexellaWeight";

    private readonly RpgMakerPluginRegistryService _pluginRegistry;

    public FusionPuzzleIntegrationProvider(RpgMakerPluginRegistryService pluginRegistry)
    {
        _pluginRegistry = pluginRegistry;
    }

    public string Id => "fusion-puzzle";

    public async Task<bool> IsAvailableAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default) =>
        await _pluginRegistry.IsActiveAsync(project, CorePluginName, cancellationToken);

    public async Task<IReadOnlyList<DocumentDescriptor>> GetDocumentsAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAvailableAsync(project, cancellationToken))
        {
            return [];
        }

        return
        [
            new DocumentDescriptor
            {
                Id = new DocumentId($"{project.Id}:integration:fusion-puzzle"),
                DisplayName = "Fusion Puzzle",
                Kind = DocumentKind.ProjectIntegration,
                ResourceId = new Uri(ResourceUri),
                SourcePath = Path.Combine(project.Path, "data"),
            },
        ];
    }
}
