using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Fusion.Story;

public sealed class FusionStoryWorkspaceService
{
    private readonly FileSystemService _fileSystem;
    private readonly RpgMakerStoryCommandParser _commandParser;

    public FusionStoryWorkspaceService(
        FileSystemService fileSystem,
        RpgMakerStoryCommandParser commandParser)
    {
        _fileSystem = fileSystem;
        _commandParser = commandParser;
    }

    public async Task<FusionStoryWorkspaceDocument> LoadAsync(
        ZiapProject project,
        DocumentDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(descriptor);

        var diagnostics = new List<StoryDiagnostic>();
        var resources = await RpgMakerStoryResourceResolver.LoadAsync(
            _fileSystem, project.Path, diagnostics, cancellationToken);
        var maps = await LoadMapsAsync(project.Path, resources, diagnostics, cancellationToken);
        var commonEvents = await LoadCommonEventsAsync(project.Path, resources, diagnostics, cancellationToken);
        return new FusionStoryWorkspaceDocument
        {
            Descriptor = descriptor,
            ProjectPath = project.Path,
            Workspace = new StoryWorkspace
            {
                Maps = maps,
                CommonEvents = commonEvents,
                Diagnostics = diagnostics
                    .OrderByDescending(diagnostic => diagnostic.Severity)
                    .ThenBy(diagnostic => diagnostic.SourcePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(diagnostic => diagnostic.MapId)
                    .ThenBy(diagnostic => diagnostic.EventId)
                    .ToArray(),
            },
        };
    }

    private async Task<IReadOnlyList<StoryMap>> LoadMapsAsync(
        string projectPath,
        RpgMakerStoryResourceResolver resources,
        ICollection<StoryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var dataPath = Path.Combine(projectPath, "data");
        if (resources.MapInfos.Count == 0)
        {
            return [];
        }

        var mapInfos = resources.MapInfos;
        var maps = new List<StoryMap>(mapInfos.Count);
        foreach (var info in mapInfos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = $"data/Map{info.Id:000}.json";
            var mapPath = Path.Combine(dataPath, $"Map{info.Id:000}.json");
            if (!_fileSystem.FileExists(mapPath))
            {
                diagnostics.Add(FileDiagnostic(
                    "story.map-file-missing", StoryDiagnosticSeverity.Warning,
                    $"{relativePath} non esiste.", relativePath, info.Id));
                continue;
            }

            try
            {
                using var mapDocument = JsonDocument.Parse(
                    await _fileSystem.ReadAllTextAsync(mapPath, cancellationToken));
                if (mapDocument.RootElement.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(FileDiagnostic(
                        "story.map-invalid", StoryDiagnosticSeverity.Error,
                        $"{relativePath} deve contenere un oggetto RPG Maker.", relativePath, info.Id));
                    continue;
                }
                maps.Add(await ParseMapAsync(
                    projectPath,
                    mapDocument.RootElement,
                    info,
                    resources,
                    relativePath,
                    diagnostics,
                    cancellationToken));
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(FileDiagnostic(
                    "story.map-unreadable", StoryDiagnosticSeverity.Error,
                    $"{relativePath} non è leggibile.", relativePath, info.Id));
            }
        }
        var hierarchy = StoryMapHierarchy.Build(maps);
        foreach (var mapId in hierarchy.FallbackRootMapIds.Order())
        {
            var map = maps.First(candidate => candidate.Id == mapId);
            diagnostics.Add(FileDiagnostic(
                "story.map-parent-invalid", StoryDiagnosticSeverity.Warning,
                $"Map {map.Id:000} ha un parentId non valido o ciclico; è mostrata alla radice del navigator.",
                map.SourcePath, map.Id));
        }
        return maps;
    }

    private async Task<StoryMap> ParseMapAsync(
        string projectPath,
        JsonElement map,
        RpgMakerStoryMapInfo info,
        RpgMakerStoryResourceResolver resources,
        string relativePath,
        ICollection<StoryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var events = new List<StoryEvent>();
        if (map.TryGetProperty("events", out var sourceEvents) && sourceEvents.ValueKind == JsonValueKind.Array)
        {
            var eventNames = sourceEvents.EnumerateArray()
                .Select((entry, index) => entry.ValueKind == JsonValueKind.Object
                    ? (Id: ReadInt(entry, "id") ?? index, Name: ReadString(entry, "name"))
                    : (Id: 0, Name: (string?)null))
                .Where(entry => entry.Id > 0 && !string.IsNullOrWhiteSpace(entry.Name))
                .GroupBy(entry => entry.Id)
                .ToDictionary(group => group.Key, group => group.Last().Name!);
            var eventIndex = 0;
            foreach (var sourceEvent in sourceEvents.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sourceEvent.ValueKind == JsonValueKind.Null)
                {
                    eventIndex++;
                    continue;
                }
                if (sourceEvent.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(FileDiagnostic(
                        "story.event-malformed", StoryDiagnosticSeverity.Warning,
                        $"Evento {eventIndex} non è un oggetto; ignorato senza fermare la mappa.",
                        relativePath, info.Id));
                    eventIndex++;
                    continue;
                }
                var eventId = ReadInt(sourceEvent, "id") ?? eventIndex;
                if (eventId <= 0)
                {
                    eventIndex++;
                    continue;
                }
                events.Add(await ParseMapEventAsync(
                    projectPath, sourceEvent, info.Id, eventId, eventNames, resources, relativePath, diagnostics, cancellationToken));
                eventIndex++;
            }
        }
        else
        {
            diagnostics.Add(FileDiagnostic(
                "story.map-events-missing", StoryDiagnosticSeverity.Warning,
                $"{relativePath} non contiene un array events.", relativePath, info.Id));
        }

        return new StoryMap
        {
            Id = info.Id,
            Name = string.IsNullOrWhiteSpace(info.Name) ? $"Map {info.Id:000}" : info.Name,
            Order = info.Order,
            ParentId = info.ParentId,
            RpgMakerExpanded = info.Expanded,
            SourcePath = relativePath,
            Events = events.OrderBy(@event => @event.Id).ToArray(),
        };
    }

    private async Task<StoryEvent> ParseMapEventAsync(
        string projectPath,
        JsonElement sourceEvent,
        int mapId,
        int eventId,
        IReadOnlyDictionary<int, string> eventNames,
        RpgMakerStoryResourceResolver resources,
        string relativePath,
        ICollection<StoryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var pages = new List<StoryPage>();
        if (sourceEvent.TryGetProperty("pages", out var sourcePages) && sourcePages.ValueKind == JsonValueKind.Array)
        {
            var pageNumber = 1;
            foreach (var page in sourcePages.EnumerateArray())
            {
                if (page.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(new StoryDiagnostic
                    {
                        Code = "story.page-malformed",
                        Severity = StoryDiagnosticSeverity.Warning,
                        Message = $"Event {eventId}, page {pageNumber} non è un oggetto.",
                        SourcePath = relativePath,
                        MapId = mapId,
                        EventId = eventId,
                        Page = pageNumber,
                    });
                    pageNumber++;
                    continue;
                }
                var commands = ReadCommands(page, diagnostics, new StoryCommandLocation(
                    relativePath, mapId, eventId, pageNumber));
                var blocks = await _commandParser.ParseAsync(
                    projectPath, commands, diagnostics,
                    new StoryCommandLocation(relativePath, mapId, eventId, pageNumber),
                    resources,
                    new RpgMakerStoryCommandContext { MapId = mapId, EventId = eventId, EventNames = eventNames },
                    cancellationToken);
                pages.Add(new StoryPage
                {
                    Number = pageNumber,
                    Trigger = ReadInt(page, "trigger") ?? 0,
                    ConditionsSummary = DescribeConditions(page),
                    CommandListTarget = new StoryCommandListTarget
                    {
                        Kind = StoryCommandListKind.MapPage,
                        SourceFile = relativePath,
                        MapId = mapId,
                        EventId = eventId,
                        PageNumber = pageNumber,
                    },
                    Blocks = blocks,
                });
                pageNumber++;
            }
        }
        else
        {
            diagnostics.Add(new StoryDiagnostic
            {
                Code = "story.event-pages-missing",
                Severity = StoryDiagnosticSeverity.Warning,
                Message = $"Event {eventId} non contiene pagine.",
                SourcePath = relativePath,
                MapId = mapId,
                EventId = eventId,
            });
        }

        return new StoryEvent
        {
            Id = eventId,
            Name = ReadString(sourceEvent, "name") ?? $"Event {eventId}",
            X = ReadInt(sourceEvent, "x") ?? 0,
            Y = ReadInt(sourceEvent, "y") ?? 0,
            Pages = pages,
        };
    }

    private async Task<IReadOnlyList<StoryCommonEvent>> LoadCommonEventsAsync(
        string projectPath,
        RpgMakerStoryResourceResolver resources,
        ICollection<StoryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var relativePath = "data/CommonEvents.json";
        var path = Path.Combine(projectPath, "data", "CommonEvents.json");
        if (!_fileSystem.FileExists(path))
        {
            return [];
        }
        try
        {
            using var document = JsonDocument.Parse(await _fileSystem.ReadAllTextAsync(path, cancellationToken));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add(FileDiagnostic(
                    "story.common-events-invalid", StoryDiagnosticSeverity.Error,
                    "CommonEvents.json deve contenere un array RPG Maker.", relativePath));
                return [];
            }
            var result = new List<StoryCommonEvent>();
            var fallbackId = 0;
            foreach (var sourceEvent in document.RootElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sourceEvent.ValueKind == JsonValueKind.Null)
                {
                    fallbackId++;
                    continue;
                }
                if (sourceEvent.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(FileDiagnostic(
                        "story.common-event-malformed", StoryDiagnosticSeverity.Warning,
                        $"Common Event {fallbackId} non è un oggetto.", relativePath));
                    fallbackId++;
                    continue;
                }
                var id = ReadInt(sourceEvent, "id") ?? fallbackId;
                fallbackId++;
                if (id <= 0)
                {
                    continue;
                }
                var location = new StoryCommandLocation(relativePath, null, id, null);
                var commands = ReadCommands(sourceEvent, diagnostics, location);
                result.Add(new StoryCommonEvent
                {
                    Id = id,
                    Name = ReadString(sourceEvent, "name") ?? $"Common Event {id}",
                    Trigger = ReadInt(sourceEvent, "trigger") ?? 0,
                    SwitchId = ReadInt(sourceEvent, "switchId") ?? 0,
                    CommandListTarget = new StoryCommandListTarget
                    {
                        Kind = StoryCommandListKind.CommonEvent,
                        SourceFile = relativePath,
                        EventId = id,
                    },
                    Blocks = await _commandParser.ParseAsync(
                        projectPath, commands, diagnostics, location, resources, null, cancellationToken),
                });
            }
            return result.OrderBy(@event => @event.Id).ToArray();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(FileDiagnostic(
                "story.common-events-unreadable", StoryDiagnosticSeverity.Error,
                "CommonEvents.json non è leggibile.", relativePath));
            return [];
        }
    }

    private static IReadOnlyList<RpgMakerEventCommand> ReadCommands(
        JsonElement source,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location)
    {
        if (!source.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(location.CreateDiagnostic(
                "story.command-list-missing", StoryDiagnosticSeverity.Warning,
                "La page/event non contiene un command list valido."));
            return [];
        }
        var commands = new List<RpgMakerEventCommand>();
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !ReadInt(entry, "code").HasValue)
            {
                diagnostics.Add(location.CreateDiagnostic(
                    "story.command-malformed", StoryDiagnosticSeverity.Warning,
                    "Un command record non è leggibile; è stato saltato."));
                continue;
            }
            var parameters = entry.TryGetProperty("parameters", out var sourceParameters)
                ? sourceParameters.Clone()
                : EmptyArray();
            commands.Add(new RpgMakerEventCommand(
                ReadInt(entry, "code")!.Value,
                ReadInt(entry, "indent") ?? 0,
                parameters));
        }
        return commands;
    }

    private static JsonElement EmptyArray()
    {
        using var document = JsonDocument.Parse("[]");
        return document.RootElement.Clone();
    }

    private static string DescribeConditions(JsonElement page)
    {
        if (!page.TryGetProperty("conditions", out var conditions) || conditions.ValueKind != JsonValueKind.Object)
        {
            return "Condizioni non disponibili";
        }
        var active = new List<string>();
        if (ReadBoolean(conditions, "switch1Valid")) active.Add($"Switch {ReadInt(conditions, "switch1Id") ?? 0}");
        if (ReadBoolean(conditions, "switch2Valid")) active.Add($"Switch {ReadInt(conditions, "switch2Id") ?? 0}");
        if (ReadBoolean(conditions, "variableValid")) active.Add($"Variable {ReadInt(conditions, "variableId") ?? 0}");
        if (ReadBoolean(conditions, "selfSwitchValid")) active.Add($"Self switch {ReadString(conditions, "selfSwitchCh") ?? "?"}");
        if (ReadBoolean(conditions, "itemValid")) active.Add($"Item {ReadInt(conditions, "itemId") ?? 0}");
        if (ReadBoolean(conditions, "actorValid")) active.Add($"Actor {ReadInt(conditions, "actorId") ?? 0}");
        return active.Count == 0 ? "Nessuna condizione" : string.Join(" · ", active);
    }

    private static StoryDiagnostic FileDiagnostic(
        string code,
        StoryDiagnosticSeverity severity,
        string message,
        string sourcePath,
        int? mapId = null) => new()
    {
        Code = code,
        Severity = severity,
        Message = message,
        SourcePath = sourcePath,
        MapId = mapId,
    };

    private static string? ReadString(JsonElement source, string propertyName) =>
        source.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? ReadInt(JsonElement source, string propertyName) =>
        source.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;

    private static bool ReadBoolean(JsonElement source, string propertyName) =>
        source.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;
}
