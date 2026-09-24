using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.World;
using ZiapStudio.Core.Models;
using ZiapStudio.Core.Preflight;
using ZiapStudio.Services.Fusion.World;
using ZiapStudio.Services.Integrations;

namespace ZiapStudio.Services.Fusion.Preflight.World;

public sealed class FusionWorldPreflightProvider : IPreflightProvider
{
    private readonly FusionWorldWorkspaceService _workspaceService;
    private readonly FusionWorldIntegrationProvider _integrationProvider;

    public FusionWorldPreflightProvider(
        FusionWorldWorkspaceService workspaceService,
        FusionWorldIntegrationProvider integrationProvider)
    {
        _workspaceService = workspaceService;
        _integrationProvider = integrationProvider;
    }

    public string Scope => "WorldNavigation";

    public async Task<IReadOnlyList<PreflightIssue>> ScanAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        if (!await _integrationProvider.IsAvailableAsync(project, cancellationToken))
        {
            return [];
        }

        try
        {
            var workspace = await _workspaceService.LoadAsync(project, new DocumentDescriptor
            {
                Id = new DocumentId($"{project.Id}:integration:fusion-world"),
                DisplayName = "World & Navigation",
                Kind = DocumentKind.ProjectIntegration,
                ResourceId = new Uri(FusionWorldIntegrationProvider.ResourceUri),
                SourcePath = Path.Combine(project.Path, "data"),
            }, cancellationToken);
            return workspace.Diagnostics.Select(CreateIssue).ToArray();
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return
            [
                new PreflightIssue
                {
                    RuleId = "world.workspace.invalid",
                    Scope = Scope,
                    RecordId = 0,
                    RecordName = "World & Navigation",
                    Severity = PreflightSeverity.Error,
                    Message = "I dati World & Navigation non sono leggibili.",
                    Details = exception.Message,
                    NavigationTarget = new Uri(FusionWorldIntegrationProvider.ResourceUri),
                },
            ];
        }
    }

    private static PreflightIssue CreateIssue(FusionWorldDiagnostic diagnostic)
    {
        var identity = string.Join(':', diagnostic.Code, diagnostic.TilesetId, diagnostic.MapId, diagnostic.AssetPath);
        return new PreflightIssue
        {
            RuleId = $"world.{diagnostic.Code}",
            Scope = "WorldNavigation",
            RecordId = StableId(identity),
            RecordName = diagnostic.AssetPath ??
                (diagnostic.MapId is { } mapId ? $"Map{mapId:000}" :
                diagnostic.TilesetId is { } tilesetId ? $"Tileset {tilesetId}" : "World & Navigation"),
            Severity = diagnostic.Severity == FusionWorldDiagnosticSeverity.Error
                ? PreflightSeverity.Error
                : PreflightSeverity.Warning,
            Message = diagnostic.Message,
            Details = diagnostic.Details,
            NavigationTarget = new Uri(FusionWorldIntegrationProvider.ResourceUri),
        };
    }

    private static int StableId(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in value.ToUpperInvariant())
            {
                hash ^= character;
                hash *= 16777619;
            }
            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
