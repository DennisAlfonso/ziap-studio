using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Fusion.Puzzles;

namespace ZiapStudio.Services.Documents;

public sealed class FusionPuzzleDocumentProvider : IDocumentProvider
{
    private readonly FusionPuzzleWorkspaceService _workspaceService;

    public FusionPuzzleDocumentProvider(FusionPuzzleWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    public bool CanOpen(DocumentDescriptor descriptor) =>
        descriptor.Kind == DocumentKind.ProjectIntegration &&
        descriptor.ResourceId.Scheme.Equals("fusionpuzzle", StringComparison.OrdinalIgnoreCase) &&
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
                "Impossibile leggere il workspace Fusion Puzzle.",
                exception);
        }
    }
}
