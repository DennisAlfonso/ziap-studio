using System.Text.Json;
using System.Globalization;
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
        RpgMakerStoryResourceResolver? resources = null,
        RpgMakerStoryCommandContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var blocks = new List<StoryBlock>();
        for (var index = 0; index < commands.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = commands[index];
            switch (command.Code)
            {
                case 0:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.ControlFlow,
                        "End Event List", "RPG Maker terminal command."));
                    break;
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
                    blocks.Add(ParseMovementRoute(commands, ref index, diagnostics, location, resources, context));
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
                    blocks.Add(ParseWait(command, index, diagnostics, location));
                    break;
                case 121:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.SwitchVariable,
                        "Switch", DescribeSwitch(command.Parameters, resources)));
                    break;
                case 122:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.SwitchVariable,
                        "Variabile", DescribeVariable(command.Parameters, resources)));
                    break;
                case 123:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.SwitchVariable,
                        "Self Switch", DescribeSelfSwitch(command.Parameters)));
                    break;
                case 201:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.Transfer,
                        "Transfer player", DescribeTransfer(command.Parameters, resources)));
                    break;
                case 111:
                case 411:
                case 412:
                case 112:
                case 413:
                case 113:
                case 115:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.ControlFlow,
                        GetControlFlowTitle(command.Code), DescribeControlFlow(command, resources)));
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
                case 212:
                    blocks.Add(ParseAnimation(command, index, diagnostics, location, resources, context));
                    break;
                case 213:
                    blocks.Add(ParseBalloon(command, index, diagnostics, location, context));
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
        var displayText = string.Join(Environment.NewLine, resolved.Select(text => text.DisplayText));
        var summary = kind == StoryBlockKind.Script
            ? displayText.Split(["\r\n", "\n"], StringSplitOptions.None).FirstOrDefault() ?? string.Empty
            : displayText;
        return (CreateBlock(grouped, start, index, kind, title,
            summary,
            string.Join(Environment.NewLine, lines),
            details: kind == StoryBlockKind.Script ? ["Full script is preserved in Raw text."] : null,
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
        StoryCommandLocation location,
        RpgMakerStoryResourceResolver? resources,
        RpgMakerStoryCommandContext? context)
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
            ? ReadMovementCommands(route.Value, resources).ToArray()
            : [];
        if (route is not { ValueKind: JsonValueKind.Object })
        {
            diagnostics.Add(location.CreateDiagnostic(
                "story.movement-route-incomplete",
                StoryDiagnosticSeverity.Warning,
                "Movement Route senza definizione route; il comando raw è stato conservato."));
            return CreateMalformedKnownBlock(grouped, start, index, "Movement Route", location);
        }

        if (!route.Value.TryGetProperty("list", out var routeList) || routeList.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(location.CreateDiagnostic(
                "story.movement-route-malformed", StoryDiagnosticSeverity.Warning,
                "Movement Route con list non valida; il comando raw è stato conservato."));
            return CreateMalformedKnownBlock(grouped, start, index, "Movement Route", location);
        }

        var routeFlags = $"Wait for completion: {ReadBoolean(route.Value, "wait")} · Repeat: {ReadBoolean(route.Value, "repeat")} · Skippable: {ReadBoolean(route.Value, "skippable")}";
        return CreateBlock(grouped, start, index, StoryBlockKind.MovementRoute,
            "Movement Route", $"Target: {DescribeRouteTarget(target, context)} · {routeCommands.Length} route commands",
            details: [
                $"Target: {DescribeRouteTarget(target, context)}",
                routeFlags,
                ..routeCommands,
            ]);
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

    private static StoryBlock ParseWait(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location)
    {
        var frames = ReadInt(command.Parameters, 0);
        if (frames is null || frames < 0)
        {
            diagnostics.Add(location.CreateDiagnostic(
                "story.wait-malformed", StoryDiagnosticSeverity.Warning,
                "Wait senza frame validi; il comando raw è stato conservato."));
            return CreateMalformedKnownBlock([command], index, index, "Wait", location);
        }

        var seconds = frames.Value / 60d;
        return CreateBlock(command, index, StoryBlockKind.Wait, "Wait",
            $"{frames.Value} frames · ~{seconds.ToString("0.0", CultureInfo.InvariantCulture)} s",
            [$"Frames (authoritative): {frames.Value}", "Display conversion: 60 FPS RPG Maker"]);
    }

    private static string DescribeSwitch(JsonElement parameters, RpgMakerStoryResourceResolver? resources)
    {
        var first = ReadInt(parameters, 0);
        var last = ReadInt(parameters, 1);
        var value = ReadInt(parameters, 2);
        if (first is null || last is null || value is null)
        {
            return "Switch parameters unavailable";
        }

        var firstLabel = resources?.DescribeSwitch(first.Value) ?? $"Switch #{first.Value}";
        var lastLabel = first == last ? string.Empty : $"–{resources?.DescribeSwitch(last.Value) ?? $"Switch #{last.Value}"}";
        return $"{firstLabel}{lastLabel} → {(value.Value == 0 ? "ON" : "OFF")}";
    }

    private static string DescribeVariable(JsonElement parameters, RpgMakerStoryResourceResolver? resources)
    {
        var first = ReadInt(parameters, 0);
        var last = ReadInt(parameters, 1);
        var operation = ReadInt(parameters, 2);
        var operandType = ReadInt(parameters, 3);
        if (first is null || last is null || operation is null || operandType is null)
        {
            return "Variable parameters unavailable";
        }

        var firstLabel = resources?.DescribeVariable(first.Value) ?? $"Variable #{first.Value}";
        var range = first == last ? firstLabel : $"{firstLabel}–{resources?.DescribeVariable(last.Value) ?? $"Variable #{last.Value}"}";
        var verb = operation.Value switch
        {
            0 => "Set",
            1 => "Add",
            2 => "Subtract",
            3 => "Multiply",
            4 => "Divide",
            5 => "Modulo",
            _ => $"Operation {operation.Value}",
        };
        return $"{range} {verb} → {DescribeVariableOperand(parameters, operandType.Value, resources)}";
    }

    private static string DescribeSelfSwitch(JsonElement parameters) =>
        $"Self switch {ReadString(parameters, 0) ?? "?"}: " +
        ((ReadInt(parameters, 1) ?? 0) == 0 ? "ON" : "OFF");

    private static string DescribeTransfer(JsonElement parameters, RpgMakerStoryResourceResolver? resources)
    {
        var designation = ReadInt(parameters, 0);
        var map = ReadInt(parameters, 1);
        var x = ReadInt(parameters, 2);
        var y = ReadInt(parameters, 3);
        if (designation is null || map is null || x is null || y is null)
        {
            return "Transfer parameters unavailable";
        }

        var direct = designation.Value == 0;
        var mapText = direct
            ? resources?.DescribeMap(map.Value) ?? $"Map {map.Value:000}"
            : DescribeVariableReference(map.Value, resources);
        var xText = direct ? x.Value.ToString() : DescribeVariableReference(x.Value, resources);
        var yText = direct ? y.Value.ToString() : DescribeVariableReference(y.Value, resources);
        var direction = ReadInt(parameters, 4) switch
        {
            2 => "Facing Down",
            4 => "Facing Left",
            6 => "Facing Right",
            8 => "Facing Up",
            0 => "Keep direction",
            _ => "Direction unavailable",
        };
        var fade = ReadInt(parameters, 5) switch
        {
            0 => "Fade Black",
            1 => "Fade White",
            2 => "No fade",
            _ => "Fade unavailable",
        };
        return $"{mapText} · X {xText} · Y {yText} · {direction} · {fade}";
    }

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

    private static string DescribeControlFlow(RpgMakerEventCommand command, RpgMakerStoryResourceResolver? resources) => command.Code switch
    {
        111 => DescribeConditional(command.Parameters, resources),
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
            return $"{ReadInt(command.Parameters, 0) ?? 0} s";
        }
        var audio = GetElement(command.Parameters, 0);
        return audio is { ValueKind: JsonValueKind.Object }
            ? $"{ReadString(audio.Value, "name") ?? "(nessun file)"} · Volume {ReadInt(audio.Value, "volume") ?? 90} · Pitch {ReadInt(audio.Value, "pitch") ?? 100} · Pan {ReadInt(audio.Value, "pan") ?? 0}"
            : "Audio non leggibile";
    }

    private static IEnumerable<string> ReadMovementCommands(
        JsonElement route,
        RpgMakerStoryResourceResolver? resources)
    {
        if (!route.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            .Select((item, index) => $"{index + 1}. {DescribeMovementCommand(item, resources)}");
    }

    private static string DescribeRouteTarget(int target, RpgMakerStoryCommandContext? context) =>
        context?.DescribeEvent(target) ?? target switch
        {
            -1 => "Player",
            0 => "This Event",
            _ => $"Event #{target}",
        };

    private static StoryBlock ParseAnimation(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location,
        RpgMakerStoryResourceResolver? resources,
        RpgMakerStoryCommandContext? context)
    {
        var target = ReadInt(command.Parameters, 0);
        var animationId = ReadInt(command.Parameters, 1);
        if (target is null || animationId is null)
        {
            diagnostics.Add(location.CreateDiagnostic(
                "story.animation-malformed", StoryDiagnosticSeverity.Warning,
                "Show Animation con parametri non validi; il comando raw è stato conservato."));
            return CreateMalformedKnownBlock([command], index, index, "Show Animation", location);
        }

        var targetText = DescribeRouteTarget(target.Value, context);
        var animation = resources?.DescribeAnimation(animationId.Value) ?? $"Animation #{animationId.Value}";
        var wait = ReadBoolean(command.Parameters, 2);
        return CreateBlock(command, index, StoryBlockKind.Animation, "Show Animation",
            $"{targetText} · {animation}",
            [$"Target: {targetText}", $"Wait for completion: {wait}"]);
    }

    private static StoryBlock ParseBalloon(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location,
        RpgMakerStoryCommandContext? context)
    {
        var target = ReadInt(command.Parameters, 0);
        var balloon = ReadInt(command.Parameters, 1);
        if (target is null || balloon is null)
        {
            diagnostics.Add(location.CreateDiagnostic(
                "story.balloon-malformed", StoryDiagnosticSeverity.Warning,
                "Show Balloon Icon con parametri non validi; il comando raw è stato conservato."));
            return CreateMalformedKnownBlock([command], index, index, "Show Balloon Icon", location);
        }

        var targetText = DescribeRouteTarget(target.Value, context);
        return CreateBlock(command, index, StoryBlockKind.Animation, "Show Balloon Icon",
            $"{targetText} · {DescribeBalloon(balloon.Value)}",
            [$"Target: {targetText}", $"Wait for completion: {ReadBoolean(command.Parameters, 2)}"]);
    }

    private static StoryBlock CreateMalformedKnownBlock(
        IReadOnlyList<RpgMakerEventCommand> commands,
        int start,
        int end,
        string knownName,
        StoryCommandLocation _) => CreateBlock(
            commands,
            start,
            end,
            StoryBlockKind.Raw,
            $"Raw · Command {commands[0].Code}",
            $"{knownName} has an unsupported or malformed parameter shape.",
            details: ["Raw source is preserved in the Inspector."]);

    private static string DescribeVariableOperand(
        JsonElement parameters,
        int operandType,
        RpgMakerStoryResourceResolver? resources) => operandType switch
    {
        0 => ReadInt(parameters, 4)?.ToString() ?? "constant unavailable",
        1 => DescribeVariableReference(ReadInt(parameters, 4) ?? 0, resources),
        2 => $"random {ReadInt(parameters, 4)?.ToString() ?? "?"}–{ReadInt(parameters, 5)?.ToString() ?? "?"}",
        3 => $"game data (type {ReadInt(parameters, 4)?.ToString() ?? "?"})",
        4 => $"script: {ReadString(parameters, 4) ?? "(empty)"}",
        _ => $"operand type {operandType} · raw params available",
    };

    private static string DescribeVariableReference(int id, RpgMakerStoryResourceResolver? resources) =>
        resources?.DescribeVariable(id) ?? $"Variable #{id}";

    private static string DescribeConditional(JsonElement parameters, RpgMakerStoryResourceResolver? resources)
    {
        var type = ReadInt(parameters, 0);
        if (type is null)
        {
            return "Conditional branch · parameters unavailable";
        }

        return type.Value switch
        {
            0 => $"{DescribeSwitchReference(ReadInt(parameters, 1) ?? 0, resources)} is {((ReadInt(parameters, 2) ?? 0) == 0 ? "ON" : "OFF")}",
            1 => $"{DescribeVariableReference(ReadInt(parameters, 1) ?? 0, resources)} {DescribeComparison(ReadInt(parameters, 4) ?? -1)} {((ReadInt(parameters, 2) ?? 0) == 0 ? ReadInt(parameters, 3)?.ToString() ?? "?" : DescribeVariableReference(ReadInt(parameters, 3) ?? 0, resources))}",
            2 => $"Self Switch {ReadString(parameters, 1) ?? "?"} is {((ReadInt(parameters, 2) ?? 0) == 0 ? "ON" : "OFF")}",
            3 => $"Timer {DescribeComparison(ReadInt(parameters, 2) ?? -1)} {ReadInt(parameters, 1)?.ToString() ?? "?"} s",
            4 => $"{DescribeActorReference(ReadInt(parameters, 1) ?? 0, resources)} · condition {ReadInt(parameters, 2)?.ToString() ?? "?"}",
            5 => $"Enemy #{ReadInt(parameters, 1)?.ToString() ?? "?"} · condition {ReadInt(parameters, 2)?.ToString() ?? "?"}",
            6 => $"Character #{ReadInt(parameters, 1)?.ToString() ?? "?"} faces {DescribeDirection(ReadInt(parameters, 2) ?? 0)}",
            7 => $"Gold {DescribeComparison(ReadInt(parameters, 2) ?? -1)} {ReadInt(parameters, 1)?.ToString() ?? "?"}",
            8 => $"Party has {DescribeItemReference(ReadInt(parameters, 1) ?? 0, resources)}",
            9 => $"Party has {DescribeWeaponReference(ReadInt(parameters, 1) ?? 0, resources)}",
            10 => $"Party has {DescribeArmorReference(ReadInt(parameters, 1) ?? 0, resources)}",
            11 => $"Button {ReadString(parameters, 1) ?? ReadInt(parameters, 1)?.ToString() ?? "?"} is pressed",
            12 => $"Script condition: {ReadString(parameters, 1) ?? "(empty)"}",
            _ => $"Conditional Branch · type {type.Value} · raw params available",
        };
    }

    private static string DescribeComparison(int operation) => operation switch
    {
        0 => "=",
        1 => "≥",
        2 => "≤",
        3 => ">",
        4 => "<",
        5 => "≠",
        _ => "?",
    };

    private static string DescribeSwitchReference(int id, RpgMakerStoryResourceResolver? resources) =>
        resources?.DescribeSwitch(id) ?? $"Switch #{id}";

    private static string DescribeActorReference(int id, RpgMakerStoryResourceResolver? resources) =>
        resources?.DescribeActor(id) ?? $"Actor #{id}";

    private static string DescribeItemReference(int id, RpgMakerStoryResourceResolver? resources) =>
        resources?.DescribeItem(id) ?? $"Item #{id}";

    private static string DescribeWeaponReference(int id, RpgMakerStoryResourceResolver? resources) =>
        resources?.DescribeWeapon(id) ?? $"Weapon #{id}";

    private static string DescribeArmorReference(int id, RpgMakerStoryResourceResolver? resources) =>
        resources?.DescribeArmor(id) ?? $"Armor #{id}";

    private static string DescribeDirection(int direction) => direction switch
    {
        2 => "Down",
        4 => "Left",
        6 => "Right",
        8 => "Up",
        _ => "direction unavailable",
    };

    private static string DescribeBalloon(int balloon) => balloon switch
    {
        1 => "Exclamation",
        2 => "Question",
        3 => "Music Note",
        4 => "Heart",
        5 => "Anger",
        6 => "Sweat",
        7 => "Frustration",
        8 => "Silence",
        9 => "Light Bulb",
        10 => "Zzz",
        _ => $"Balloon #{balloon}",
    };

    private static string DescribeMovementCommand(JsonElement command, RpgMakerStoryResourceResolver? resources)
    {
        var code = ReadInt(command, "code");
        var parameters = command.TryGetProperty("parameters", out var value) ? value : default;
        var first = ReadInt(parameters, 0);
        return code switch
        {
            0 => "End Route",
            1 => "Move Down",
            2 => "Move Left",
            3 => "Move Right",
            4 => "Move Up",
            5 => "Move Lower Left",
            6 => "Move Lower Right",
            7 => "Move Upper Left",
            8 => "Move Upper Right",
            9 => "Move Random",
            10 => "Move Toward Player",
            11 => "Move Away From Player",
            12 => "Move Forward",
            13 => "Move Backward",
            14 => $"Jump {first?.ToString() ?? "?"}, {ReadInt(parameters, 1)?.ToString() ?? "?"}",
            15 => $"Wait {first?.ToString() ?? "?"} frames",
            16 => "Turn Down",
            17 => "Turn Left",
            18 => "Turn Right",
            19 => "Turn Up",
            20 => "Turn 90° Right",
            21 => "Turn 90° Left",
            22 => "Turn 180°",
            23 => "Turn 90° Random",
            24 => "Turn Random",
            25 => "Turn Toward Player",
            26 => $"Switch ON {DescribeSwitchReference(first ?? 0, resources)}",
            27 => $"Switch OFF {DescribeSwitchReference(first ?? 0, resources)}",
            28 => $"Change Speed {first?.ToString() ?? "?"}",
            29 => $"Change Frequency {first?.ToString() ?? "?"}",
            30 => "Walking Animation ON",
            31 => "Walking Animation OFF",
            32 => "Stepping Animation ON",
            33 => "Stepping Animation OFF",
            34 => "Direction Fix ON",
            35 => "Direction Fix OFF",
            36 => "Through ON",
            37 => "Through OFF",
            38 => "Transparency ON",
            39 => "Transparency OFF",
            40 => $"Change Image {ReadString(parameters, 0) ?? "(none)"} · index {ReadInt(parameters, 1)?.ToString() ?? "?"}",
            41 => $"Change Opacity {first?.ToString() ?? "?"}",
            42 => $"Change Blend Mode {first?.ToString() ?? "?"}",
            44 => "Play SE",
            45 => $"Script: {ReadString(parameters, 0) ?? "(empty)"}",
            _ => $"Movement code {code?.ToString() ?? "?"}: {ReadPropertyRaw(command, "parameters")}",
        };
    }

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

    private static bool ReadBoolean(JsonElement parameters, int index) =>
        GetElement(parameters, index) is { ValueKind: JsonValueKind.True };

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
