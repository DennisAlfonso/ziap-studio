using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Integrations;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Integrations;

public sealed class FusionBossIntegrationProvider : IProjectIntegrationProvider
{
    public const string ResourceUri = "fusionboss://workspace/";
    public const string CombatPluginName = "ZDP_FusionCombat";
    public const string EncounterPluginName = "ZDP_FusionEncounter";
    public const string ArenaPluginName = "ZDP_FusionArena";
    public const string PuzzlePluginName = "ZDP_FusionPuzzle";
    public const string TelegraphPluginName = "FHD_EnemyAttackTelegraph";

    public static readonly IReadOnlyList<string> CorePluginNames =
    [
        CombatPluginName,
        EncounterPluginName,
        ArenaPluginName,
    ];

    private readonly RpgMakerPluginRegistryService _pluginRegistry;

    public FusionBossIntegrationProvider(RpgMakerPluginRegistryService pluginRegistry)
    {
        _pluginRegistry = pluginRegistry;
    }

    public string Id => "fusion-boss-battle";

    public async Task<bool> IsAvailableAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        var plugins = await _pluginRegistry.LoadAsync(project, cancellationToken);
        return plugins.Any(plugin => plugin.IsActive && CorePluginNames.Any(required =>
            RpgMakerPluginRegistryService.PluginNameEquals(plugin.Name, required)));
    }

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
                Id = new DocumentId($"{project.Id}:integration:fusion-boss"),
                DisplayName = "Fusion Boss Battle",
                Kind = DocumentKind.ProjectIntegration,
                ResourceId = new Uri(ResourceUri),
                SourcePath = Path.Combine(project.Path, "data"),
            },
        ];
    }
}
