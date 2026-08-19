using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Integrations;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Integrations;

public sealed class FusionAudioIntegrationProvider : IProjectIntegrationProvider
{
    public const string PluginName = "ZDP_FusionAudio";
    public const string ResourceUri = "fusionaudio://catalog/";

    private readonly RpgMakerPluginRegistryService _pluginRegistry;

    public FusionAudioIntegrationProvider(RpgMakerPluginRegistryService pluginRegistry)
    {
        _pluginRegistry = pluginRegistry;
    }

    public string Id => "fusion-audio";

    public Task<bool> IsAvailableAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default) =>
        _pluginRegistry.IsActiveAsync(project, PluginName, cancellationToken);

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
                Id = new DocumentId($"{project.Id}:integration:fusion-audio"),
                DisplayName = "Fusion Audio",
                Kind = DocumentKind.ProjectIntegration,
                ResourceId = new Uri(ResourceUri),
                SourcePath = Path.Combine(project.Path, "data", "fusion", "audio.json"),
            },
        ];
    }
}
