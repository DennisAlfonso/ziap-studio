using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Fusion.Audio;
using ZiapStudio.Services.Integrations;

namespace ZiapStudio.Services.Documents;

public sealed class FusionAudioDocumentProvider : IDocumentProvider
{
    private readonly FusionAudioCatalogService _catalogService;

    public FusionAudioDocumentProvider(FusionAudioCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public bool CanOpen(DocumentDescriptor descriptor) =>
        descriptor.Kind == DocumentKind.ProjectIntegration &&
        descriptor.ResourceId.Scheme.Equals("fusionaudio", StringComparison.OrdinalIgnoreCase) &&
        descriptor.ResourceId.Host.Equals("catalog", StringComparison.OrdinalIgnoreCase);

    public async Task<StudioDocument> OpenAsync(
        ZiapProject project,
        DocumentDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _catalogService.LoadAsync(project, descriptor, cancellationToken);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new DocumentLoadException(
                "Impossibile leggere data/fusion/audio.json.",
                exception);
        }
    }
}
