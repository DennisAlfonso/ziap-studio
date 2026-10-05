using System.Diagnostics;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Fusion.Story;

namespace ZiapStudio.Services.Documents;

public sealed class FusionStoryDocumentProvider : IDocumentProvider
{
    private readonly FusionStoryWorkspaceService _workspaceService;

    public FusionStoryDocumentProvider(FusionStoryWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    public bool CanOpen(DocumentDescriptor descriptor) =>
        descriptor.Kind == DocumentKind.ProjectIntegration &&
        descriptor.ResourceId.Scheme.Equals("fusionstory", StringComparison.OrdinalIgnoreCase) &&
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DocumentLoadException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Story & Events loader failed:{Environment.NewLine}{exception}");
            throw new DocumentLoadException("Impossibile leggere Story & Events.", exception);
        }
    }
}
