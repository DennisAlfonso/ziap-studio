using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Fusion.Bosses;

namespace ZiapStudio.Services.Documents;

public sealed class FusionBossDocumentProvider : IDocumentProvider
{
    private readonly FusionBossWorkspaceService _workspaceService;

    public FusionBossDocumentProvider(FusionBossWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    public bool CanOpen(DocumentDescriptor descriptor) =>
        descriptor.Kind == DocumentKind.ProjectIntegration &&
        descriptor.ResourceId.Scheme.Equals("fusionboss", StringComparison.OrdinalIgnoreCase) &&
        descriptor.ResourceId.Host.Equals("workspace", StringComparison.OrdinalIgnoreCase);

    public async Task<StudioDocument> OpenAsync(
        ZiapProject project,
        DocumentDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _workspaceService.LoadAsync(project, descriptor, cancellationToken);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new DocumentLoadException(
                "Impossibile leggere i database Fusion Boss Battle.",
                exception);
        }
    }
}
