using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Fusion.World;

namespace ZiapStudio.Services.Documents;

public sealed class FusionWorldDocumentProvider : IDocumentProvider
{
    private readonly FusionWorldWorkspaceService _workspaceService;

    public FusionWorldDocumentProvider(FusionWorldWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    public bool CanOpen(DocumentDescriptor descriptor) =>
        descriptor.Kind == DocumentKind.ProjectIntegration &&
        descriptor.ResourceId.Scheme.Equals("fusionworld", StringComparison.OrdinalIgnoreCase) &&
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
                "Impossibile leggere i dati World & Navigation.",
                exception);
        }
    }
}
