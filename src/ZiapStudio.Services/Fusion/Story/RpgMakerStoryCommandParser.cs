using System.Text.Json;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Fusion.Story;

/// <summary>
/// Conservatively folds the command stream used by RPG Maker MZ into displayable
/// blocks. It intentionally leaves unsupported commands as Raw blocks.
/// </summary>
public sealed class RpgMakerStoryCommandParser
{
    private readonly CompositeLocalizationTextResolver _textResolver;

    public RpgMakerStoryCommandParser(CompositeLocalizationTextResolver textResolver)
    {
        _textResolver = textResolver;
    }

    public async Task<IReadOnlyList<StoryBlock>> ParseAsync(
        string projectPath,
        IReadOnlyList<RpgMakerEventCommand> commands,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location,
        CancellationToken cancellationToken = default)
    {
        var blocks = new List<StoryBlock>();
        for (var index = 0; index < commands.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = commands[index];
            switch (command.Code)
            {
                case 101:
                {
                    var parsed = await ParseDialogueAsync(projectPath, commands, index, cancellationToken);
                    blocks.Add(parsed.Block);
                    index = parsed.EndIndex;
                    break;
                }
                case 108:
                {
                    var parsed = await ParseTextContinuationAsync(
                        projectPath, commands, index, 408,
                        StoryBlockKind.Comment, "Commento", cancellationToken);
                    blocks.Add(parsed.Block);
                    index = parsed.EndIndex;
                    break;
                }
                case 355:
                {
                    var parsed = await ParseTextContinuationAsync(
                        projectPath, commands, index, 655,
                        StoryBlockKind.Script, "Script", cancellationToken);
                    blocks.Add(parsed.Block);
                    index = parsed.EndIndex;
                    break;
                }
                case 357:
                {
                    var parsed = await ParsePluginCommandAsync(projectPath, commands, index, cancellationToken);
                    blocks.Add(parsed.Block);
                    index = parsed.EndIndex;
                    break;
                }
                case 205:
                    blocks.Add(ParseMovementRoute(commands, ref index, diagnostics, location));
                    break;
                case 102:
                    blocks.Add(await ParseChoicesAsync(projectPath, command, index, cancellationToken));
                    break;
                case 402:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.ControlFlow,
                        "Branch scelta", ReadString(command.Parameters, 1) ?? "Opzione", [
                            $"Indice scelta: {ReadInt(command.Parameters, 0)?.ToString() ?? "?"}",
                        ]));
                    break;
                case 403:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.ControlFlow,
                        "Cancel branch", "Ramo annullamento"));
                    break;
                case 404:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.ControlFlow,
                        "Fine scelte", "Fine delle scelte"));
                    break;
                case 230:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.Wait,
                        "Wait", $"{ReadInt(command.Parameters, 0) ?? 0} frame"));
                    break;
                case 121:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.SwitchVariable,
                        "Switch", DescribeSwitch(command.Parameters)));
                    break;
                case 122:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.SwitchVariable,
                        "Variabile", DescribeVariable(command.Parameters)));
                    break;
                case 123:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.SwitchVariable,
                        "Self Switch", DescribeSelfSwitch(command.Parameters)));
                    break;
                case 201:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.Transfer,
                        "Transfer player", DescribeTransfer(command.Parameters)));
                    break;
                case 111:
                case 411:
                case 412:
                case 112:
                case 413:
                case 113:
                case 115:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.ControlFlow,
                        GetControlFlowTitle(command.Code), DescribeControlFlow(command)));
                    break;
                case 241:
                case 242:
                case 245:
                case 246:
                case 249:
                case 250:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.Audio,
                        GetAudioTitle(command.Code), DescribeAudio(command)));
                    break;
                default:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.Raw,
                        $"Raw command {command.Code}", "Comando RPG Maker non ancora interpretato."));
                    break;
            }
        }

        return blocks;
    }

    private async Task<(StoryBlock Block, int EndIndex)> ParseDialogueAsync(
        string projectPath,
        IReadOnlyList<RpgMakerEventCommand> commands,
        int start,
        CancellationToken cancellationToken)
    {
        var index = start;
        var first = commands[index];
        var grouped = new List<RpgMakerEventCommand> { first };
        var rawLines = new List<string>();
        while (index + 1 < commands.Count && commands[index + 1].Code == 401)
        {
            grouped.Add(commands[++index]);
            rawLines.Add(ReadString(commands[index].Parameters, 0) ?? string.Empty);
        }

        var rawSpeaker = ReadString(first.Parameters, 4) ?? string.Empty;
        var speaker = await _textResolver.ResolveAsync(projectPath, rawSpeaker, cancellationToken: cancellationToken);
        var resolvedLines = new List<CompositeLocalizationText>();
        foreach (var line in rawLines)
        {
            resolvedLines.Add(await _textResolver.ResolveAsync(projectPath, line, cancellationToken: cancellationToken));
        }

        var allOrigins = speaker.Origins.Concat(resolvedLines.SelectMany(line => line.Origins)).ToArray();
        var displayText = string.Join(Environment.NewLine, resolvedLines.Select(line => line.DisplayText));
        return (CreateBlock(
            grouped,
            start,
            index,
            StoryBlockKind.Dialogue,
            string.IsNullOrWhiteSpace(speaker.DisplayText) ? "Dialogo" : speaker.DisplayText,
            displayText,
            rawText: string.Join(Environment.NewLine, rawLines),
            details:
            [
                $"Face: {ReadString(first.Parameters, 0) ?? "—"} · index {ReadInt(first.Parameters, 1) ?? 0}",
                $"Background: {ReadInt(first.Parameters, 2) ?? 0} · Position: {ReadInt(first.Parameters, 3) ?? 2}",
                $"Speaker raw: {rawSpeaker}",
            ],
            origins: allOrigins), index);
    }

    private async Task<(StoryBlock Block, int EndIndex)> ParseTextContinuationAsync(
        string projectPath,
        IReadOnlyList<RpgMakerEventCommand> commands,
        int start,
        int continuationCode,
        StoryBlockKind kind,
        string title,
        CancellationToken cancellationToken)
    {
        var index = start;
        var grouped = new List<RpgMakerEventCommand> { commands[index] };
        var lines = new List<string> { ReadString(commands[index].Parameters, 0) ?? string.Empty };
        while (index + 1 < commands.Count && commands[index + 1].Code == continuationCode)
        {
            grouped.Add(commands[++index]);
            lines.Add(ReadString(commands[index].Parameters, 0) ?? string.Empty);
        }

        var resolved = new List<CompositeLocalizationText>();
        foreach (var line in lines)
        {
            resolved.Add(await _textResolver.ResolveAsync(projectPath, line, cancellationToken: cancellationToken));
        }
        return (CreateBlock(grouped, start, index, kind, title,
            string.Join(Environment.NewLine, resolved.Select(text => text.DisplayText)),
            string.Join(Environment.NewLine, lines),
            origins: resolved.SelectMany(text => text.Origins).ToArray()), index);
    }

    private async Task<(StoryBlock Block, int EndIndex)> ParsePluginCommandAsync(
        string projectPath,
        IReadOnlyList<RpgMakerEventCommand> commands,
        int start,
        CancellationToken cancellationToken)
    {
        var index = start;
        var grouped = new List<RpgMakerEventCommand> { commands[index] };
        while (index + 1 < commands.Count && commands[index + 1].Code == 657)
        {
            grouped.Add(commands[++index]);
        }

        var first = grouped[0];
        var plugin = ReadString(first.Parameters, 0) ?? "Plugin";
        var command = ReadString(first.Parameters, 1) ?? "Command";
        var values = new List<CompositeLocalizationText>();
        foreach (var rawText in EnumerateStrings(first.Parameters))
        {
            var resolved = await _textResolver.ResolveAsync(projectPath, rawText, cancellationToken: cancellationToken);
            if (resolved.Tokens.Count > 0)
            {
                values.Add(resolved);
            }
        }

        var localizedSummary = values.Select(value => value.DisplayText)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return (CreateBlock(grouped, start, index, StoryBlockKind.PluginCommand,
            $"{plugin} · {command}", localizedSummary ?? "Plugin command",
            details: ["I parametri raw restano disponibili nell'inspector."],
            origins: values.SelectMany(value => value.Origins).ToArray()), index);
    }

    private static StoryBlock ParseMovementRoute(
        IReadOnlyList<RpgMakerEventCommand> commands,
        ref int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location)
    {
        var start = index;
        var grouped = new List<RpgMakerEventCommand> { commands[index] };
        while (index + 1 < commands.Count && commands[index + 1].Code == 505)
        {
            grouped.Add(commands[++index]);
        }

        var first = grouped[0];
        var target = ReadInt(first.Parameters, 0) ?? 0;
        var route = GetElement(first.Parameters, 1);
        var routeCommands = route is { ValueKind: JsonValueKind.Object }
            ? ReadMovementCommands(route.Value).ToArray()
            : [];
        if (route is null)
        {
            diagnostics.Add(location.CreateDiagnostic(
                "story.movement-route-incomplete",
                StoryDiagnosticSeverity.Warning,
                "Movement Route senza definizione route; il comando raw è stato conservato."));
        }

        var routeFlags = route is { ValueKind: JsonValueKind.Object }
            ? $"wait: {ReadBoolean(route.Value, "wait")} · repeat: {ReadBoolean(route.Value, "repeat")} · skippable: {ReadBoolean(route.Value, "skippable")}" 
            : "Route non leggibile";
        return CreateBlock(grouped, start, index, StoryBlockKind.MovementRoute,
            "Movement Route", $"Target {DescribeRouteTarget(target)} · {routeCommands.Length} movimenti",
            details: [routeFlags, ..routeCommands]);
    }

    private async Task<StoryBlock> ParseChoicesAsync(
        string projectPath,
        RpgMakerEventCommand command,
        int index,
        CancellationToken cancellationToken)
    {
        var choices = GetElement(command.Parameters, 0) is { ValueKind: JsonValueKind.Array } values
            ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString() ?? string.Empty).ToArray()
            : [];
        var resolved = new List<CompositeLocalizationText>();
        foreach (var choice in choices)
        {
            resolved.Add(await _textResolver.ResolveAsync(projectPath, choice, cancellationToken: cancellationToken));
        }
        return CreateBlock([command], index, index, StoryBlockKind.Choices, "Scelte",
            string.Join(" · ", resolved.Select(value => value.DisplayText)),
            rawText: string.Join(" · ", choices),
            details:
            [
                $"Cancel: {ReadInt(command.Parameters, 1)?.ToString() ?? "—"}",
                $"Default: {ReadInt(command.Parameters, 2)?.ToString() ?? "—"}",
            ],
            origins: resolved.SelectMany(value => value.Origins).ToArray());
    }

    private static StoryBlock CreateBlock(
        RpgMakerEventCommand command,
        int index,
        StoryBlockKind kind,
        string title,
        string summary,
        IReadOnlyList<string>? details = null) => CreateBlock(
            [command], index, index, kind, title, summary, details: details);

    private static StoryBlock CreateBlock(
        IReadOnlyList<RpgMakerEventCommand> commands,
        int start,
        int end,
        StoryBlockKind kind,
        string title,
        string summary,
        string? rawText = null,
        IReadOnlyList<string>? details = null,
        IReadOnlyList<ZiapStudio.Core.Localization.LocalizationReferenceOrigin>? origins = null) => new()
    {
        Kind = kind,
        Title = title,
        Summary = summary,
        CommandStartIndex = start,
        CommandEndIndex = end,
        Indent = commands[0].Indent,
        RawText = rawText ?? string.Empty,
        DisplayText = summary,
        RawParameters = commands[0].Parameters.GetRawText(),
        SourceCommands = commands.Select(command => new StoryRawCommand
        {
            Code = command.Code,
            Indent = command.Indent,
            Parameters = command.Parameters.GetRawText(),
        }).ToArray(),
        Details = details ?? [],
        LocalizationOrigins = origins ?? [],
    };

    private static string DescribeSwitch(JsonElement parameters) =>
        $"Switch {ReadInt(parameters, 0) ?? 0}–{ReadInt(parameters, 1) ?? 0}: " +
        ((ReadInt(parameters, 2) ?? 0) == 0 ? "ON" : "OFF");

    private static string DescribeVariable(JsonElement parameters) =>
        $"Variabile {ReadInt(parameters, 0) ?? 0}–{ReadInt(parameters, 1) ?? 0} · operazione {ReadInt(parameters, 2) ?? 0}";

    private static string DescribeSelfSwitch(JsonElement parameters) =>
        $"Self switch {ReadString(parameters, 0) ?? "?"}: " +
        ((ReadInt(parameters, 1) ?? 0) == 0 ? "ON" : "OFF");

    private static string DescribeTransfer(JsonElement parameters) =>
        $"Map {ReadInt(parameters, 1) ?? 0} · x {ReadInt(parameters, 2) ?? 0} · y {ReadInt(parameters, 3) ?? 0}";

    private static string GetControlFlowTitle(int code) => code switch
    {
        111 => "Conditional branch",
        411 => "Else",
        412 => "End conditional",
        112 => "Loop",
        413 => "Repeat above",
        113 => "Break loop",
        _ => "Exit event processing",
    };

    private static string DescribeControlFlow(RpgMakerEventCommand command) => command.Code switch
    {
        111 => $"Condizione tipo {ReadInt(command.Parameters, 0) ?? 0}",
        411 => "Altrimenti",
        412 => "Fine condizione",
        112 => "Inizio loop",
        413 => "Ripeti dall'inizio del loop",
        113 => "Esci dal loop",
        _ => "Termina elaborazione evento",
    };

    private static string GetAudioTitle(int code) => code switch
    {
        241 => "Play BGM",
        242 => "Fadeout BGM",
        245 => "Play BGS",
        246 => "Fadeout BGS",
        249 => "Play ME",
        _ => "Play SE",
    };

    private static string DescribeAudio(RpgMakerEventCommand command)
    {
        if (command.Code is 242 or 246)
        {
            return $"{ReadInt(command.Parameters, 0) ?? 0} secondi";
        }
        var audio = GetElement(command.Parameters, 0);
        return audio is { ValueKind: JsonValueKind.Object }
            ? ReadString(audio.Value, "name") ?? "(nessun file)"
            : "Audio non leggibile";
    }

    private static IEnumerable<string> ReadMovementCommands(JsonElement route)
    {
        if (!route.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => $"Movement code {ReadInt(item, "code") ?? -1}: {ReadPropertyRaw(item, "parameters")}");
    }

    private static string DescribeRouteTarget(int target) => target switch
    {
        -1 => "Player",
        0 => "Questo evento",
        _ => $"Evento {target}",
    };

    private static string ReadPropertyRaw(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) ? value.GetRawText() : "[]";

    private static IEnumerable<string> EnumerateStrings(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => [value.GetString() ?? string.Empty],
            JsonValueKind.Array => value.EnumerateArray().SelectMany(EnumerateStrings),
            JsonValueKind.Object => value.EnumerateObject().SelectMany(property => EnumerateStrings(property.Value)),
            _ => [],
        };
    }

    private static JsonElement? GetElement(JsonElement parameters, int index)
    {
        if (parameters.ValueKind != JsonValueKind.Array || index < 0 || index >= parameters.GetArrayLength())
        {
            return null;
        }
        return parameters[index];
    }

    private static string? ReadString(JsonElement parameters, int index) =>
        GetElement(parameters, index) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static string? ReadString(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? ReadInt(JsonElement parameters, int index) =>
        GetElement(parameters, index) is { } value && value.TryGetInt32(out var result) ? result : null;

    private static int? ReadInt(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) &&
        value.TryGetInt32(out var result) ? result : null;

    private static bool ReadBoolean(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.True;
}

public sealed record RpgMakerEventCommand(int Code, int Indent, JsonElement Parameters);

public sealed record StoryCommandLocation(string SourcePath, int? MapId, int? EventId, int? Page)
{
    public StoryDiagnostic CreateDiagnostic(string code, StoryDiagnosticSeverity severity, string message) => new()
    {
        Code = code,
        Severity = severity,
        Message = message,
        SourcePath = SourcePath,
        MapId = MapId,
        EventId = EventId,
        Page = Page,
    };
}
