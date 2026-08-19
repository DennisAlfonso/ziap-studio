using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Bosses;
using ZiapStudio.Core.Models;
using ZiapStudio.Core.Preflight;
using ZiapStudio.Services.Fusion.Bosses;
using ZiapStudio.Services.Integrations;

namespace ZiapStudio.Services.Fusion.Preflight.Bosses;

public sealed class FusionBossPreflightProvider : IPreflightProvider
{
    private readonly FusionBossWorkspaceService _workspaceService;
    private readonly FusionBossIntegrationProvider _integrationProvider;

    public FusionBossPreflightProvider(
        FusionBossWorkspaceService workspaceService,
        FusionBossIntegrationProvider integrationProvider)
    {
        _workspaceService = workspaceService;
        _integrationProvider = integrationProvider;
    }

    public string Scope => "FusionBoss";

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
            var descriptor = new DocumentDescriptor
            {
                Id = new DocumentId($"{project.Id}:integration:fusion-boss"),
                DisplayName = "Fusion Boss Battle",
                Kind = DocumentKind.ProjectIntegration,
                ResourceId = new Uri(FusionBossIntegrationProvider.ResourceUri),
                SourcePath = Path.Combine(project.Path, "data"),
            };
            var workspace = await _workspaceService.LoadAsync(
                project,
                descriptor,
                cancellationToken);
            return workspace.Diagnostics
                .Where(diagnostic =>
                    diagnostic.Severity != FusionBossDiagnosticSeverity.Information)
                .Select(CreateIssue)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return
            [
                new PreflightIssue
                {
                    RuleId = "fusion-boss.workspace.invalid",
                    Scope = Scope,
                    RecordId = 0,
                    RecordName = "Fusion Boss Battle",
                    Severity = PreflightSeverity.Error,
                    Message = "I database Fusion Boss Battle non sono leggibili.",
                    Details = exception.Message,
                    NavigationTarget = new Uri(FusionBossIntegrationProvider.ResourceUri),
                },
            ];
        }
    }

    private static PreflightIssue CreateIssue(FusionBossDiagnostic diagnostic)
    {
        var identity = string.Join(
            ':',
            diagnostic.Database?.ToString() ?? "workspace",
            diagnostic.CollectionId ?? string.Empty,
            diagnostic.RecordId ?? diagnostic.Code);
        return new PreflightIssue
        {
            RuleId = $"fusion-boss.{diagnostic.Code}",
            Scope = "FusionBoss",
            RecordId = StableId(identity),
            RecordName = diagnostic.RecordId ?? diagnostic.Database?.ToString() ?? "Fusion Boss Battle",
            Severity = diagnostic.Severity == FusionBossDiagnosticSeverity.Error
                ? PreflightSeverity.Error
                : PreflightSeverity.Warning,
            Message = diagnostic.Message,
            Details = diagnostic.Details,
            NavigationTarget = diagnostic.NavigationTarget,
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
