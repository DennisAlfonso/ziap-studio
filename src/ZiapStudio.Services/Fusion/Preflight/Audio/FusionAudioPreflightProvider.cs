using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Audio;
using ZiapStudio.Core.Models;
using ZiapStudio.Core.Preflight;
using ZiapStudio.Services.Fusion.Audio;
using ZiapStudio.Services.Integrations;

namespace ZiapStudio.Services.Fusion.Preflight.Audio;

public sealed class FusionAudioPreflightProvider : IPreflightProvider
{
    private readonly FusionAudioCatalogService _catalogService;
    private readonly RpgMakerPluginRegistryService _pluginRegistry;

    public FusionAudioPreflightProvider(
        FusionAudioCatalogService catalogService,
        RpgMakerPluginRegistryService pluginRegistry)
    {
        _catalogService = catalogService;
        _pluginRegistry = pluginRegistry;
    }

    public string Scope => "FusionAudio";

    public async Task<IReadOnlyList<PreflightIssue>> ScanAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        if (!await _pluginRegistry.IsActiveAsync(
            project,
            FusionAudioIntegrationProvider.PluginName,
            cancellationToken))
        {
            return [];
        }

        try
        {
            var descriptor = new DocumentDescriptor
            {
                Id = new DocumentId($"{project.Id}:integration:fusion-audio"),
                DisplayName = "Fusion Audio",
                Kind = DocumentKind.ProjectIntegration,
                ResourceId = new Uri(FusionAudioIntegrationProvider.ResourceUri),
            };
            var document = await _catalogService.LoadAsync(project, descriptor, cancellationToken);
            return document.Diagnostics
                .Where(diagnostic => diagnostic.Severity != FusionAudioDiagnosticSeverity.Information)
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
                    RuleId = "fusion-audio.catalog.invalid",
                    Scope = Scope,
                    RecordId = 0,
                    RecordName = "Fusion Audio",
                    Severity = PreflightSeverity.Error,
                    Message = "data/fusion/audio.json non è leggibile.",
                    Details = exception.Message,
                    NavigationTarget = new Uri(FusionAudioIntegrationProvider.ResourceUri),
                },
            ];
        }
    }

    private static PreflightIssue CreateIssue(FusionAudioDiagnostic diagnostic)
    {
        var eventId = diagnostic.EventId;
        var navigation = string.IsNullOrWhiteSpace(eventId)
            ? new Uri(FusionAudioIntegrationProvider.ResourceUri)
            : new Uri($"fusionaudio://catalog/event/{Uri.EscapeDataString(eventId)}");
        return new PreflightIssue
        {
            RuleId = $"fusion-audio.{diagnostic.Code}",
            Scope = "FusionAudio",
            RecordId = StableId(eventId ?? diagnostic.Code),
            RecordName = eventId ?? "Fusion Audio",
            Severity = diagnostic.Severity == FusionAudioDiagnosticSeverity.Error
                ? PreflightSeverity.Error
                : PreflightSeverity.Warning,
            Message = diagnostic.Message,
            NavigationTarget = navigation,
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
