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
        var labels = commands
            .Where(command => command.Code == 118)
            .Select(command => ReadString(command.Parameters, 0))
            .Where(label => label is not null)
            .Select(label => label!)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < commands.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = commands[index];
            switch (command.Code)
            {
                case 0:
                    // RPG Maker's list sentinel is structural, never a visible timeline card.
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
                case 657:
                    blocks.Add(ParseOrphanPluginContinuation(command, index, diagnostics, location));
                    break;
                case 117:
                    blocks.Add(ParseCommonEvent(command, index, diagnostics, location, resources));
                    break;
                case 118:
                    blocks.Add(ParseLabel(command, index, diagnostics, location));
                    break;
                case 119:
                    blocks.Add(ParseJumpToLabel(command, index, labels, diagnostics, location));
                    break;
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
                case 125:
                case 126:
                case 127:
                case 128:
                case 129:
                    blocks.Add(ParsePartyCommand(command, index, diagnostics, location, resources));
                    break;
                case 134:
                case 135:
                case 136:
                case 137:
                    blocks.Add(ParseSystemAccess(command, index, diagnostics, location));
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
                case 243:
                case 244:
                case 245:
                case 246:
                case 249:
                case 250:
                case 251:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.Audio,
                        GetAudioTitle(command.Code), DescribeAudio(command)));
                    break;
                case 211:
                case 221:
                case 222:
                case 223:
                case 224:
                case 225:
                    blocks.Add(ParseScreenCommand(command, index, diagnostics, location));
                    break;
                case 231:
                case 232:
                case 233:
                case 234:
                case 235:
                    blocks.Add(ParsePictureCommand(command, index, diagnostics, location, resources));
                    break;
                case 214:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.System,
                        "Erase Event", "Erase this event from the current map."));
                    break;
                case 303:
                case 311:
                case 312:
                case 313:
                case 314:
                case 315:
                case 316:
                case 318:
                case 320:
                case 322:
                case 326:
                    blocks.Add(ParseActorCommand(command, index, diagnostics, location, resources));
                    break;
                case 331:
                    blocks.Add(ParseEnemyHp(command, index, diagnostics, location, resources));
                    break;
                case 352:
                case 354:
                    blocks.Add(CreateBlock(command, index, StoryBlockKind.System,
                        command.Code == 352 ? "Open Save Screen" : "Return to Title Screen",
                        command.Code == 352 ? "Open the Save screen." : "Return to the title screen."));
                    break;
                case 356:
                    blocks.Add(ParseLegacyPluginCommand(command, index, diagnostics, location));
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

    private static StoryBlock ParseOrphanPluginContinuation(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location)
    {
        diagnostics.Add(location.CreateDiagnostic(
            "story.plugin-continuation-orphan", StoryDiagnosticSeverity.Warning,
            "Plugin Command Continuation (657) non segue un Plugin Command (357); il source raw è stato conservato."));
        return CreateBlock(command, index, StoryBlockKind.Raw,
            "Orphan Plugin Command Continuation",
            "Continuation 657 senza un Plugin Command 357 precedente.");
    }

    private static StoryBlock ParseCommonEvent(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location,
        RpgMakerStoryResourceResolver? resources)
    {
        var commonEventId = ReadInt(command.Parameters, 0);
        if (commonEventId is not > 0)
        {
            return Malformed(command, index, "Common Event", diagnostics, location);
        }

        if (resources is not null && !resources.HasCommonEvent(commonEventId.Value))
        {
            diagnostics.Add(location.CreateDiagnostic(
                "story.common-event-target-missing", StoryDiagnosticSeverity.Warning,
                $"Common Event #{commonEventId.Value} non è risolvibile nel database; il riferimento è stato conservato."));
        }

        var target = resources?.DescribeCommonEvent(commonEventId.Value) ?? $"Common Event #{commonEventId.Value}";
        return CreateBlock(command, index, StoryBlockKind.ControlFlow, "Common Event", target,
            [$"CommonEventId: {commonEventId.Value}"],
            commonEventId: commonEventId.Value,
            targetId: commonEventId.Value);
    }

    private static StoryBlock ParseLabel(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location)
    {
        var label = ReadString(command.Parameters, 0);
        return label is null
            ? Malformed(command, index, "Label", diagnostics, location)
            : CreateBlock(command, index, StoryBlockKind.ControlFlow, "Label", label,
                [$"Label: {label}"], labelName: label);
    }

    private static StoryBlock ParseJumpToLabel(
        RpgMakerEventCommand command,
        int index,
        IReadOnlySet<string> labels,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location)
    {
        var label = ReadString(command.Parameters, 0);
        if (label is null)
        {
            return Malformed(command, index, "Jump to Label", diagnostics, location);
        }
        if (!labels.Contains(label))
        {
            diagnostics.Add(location.CreateDiagnostic(
                "story.label-target-missing", StoryDiagnosticSeverity.Warning,
                $"Jump to Label punta a \"{label}\", ma non esiste una Label corrispondente nella stessa command list."));
        }
        return CreateBlock(command, index, StoryBlockKind.ControlFlow, "Jump to Label", label,
            [$"Target label: {label}", labels.Contains(label) ? "Target resolved in this command list." : "Target label missing."],
            labelName: label);
    }

    private static StoryBlock ParsePartyCommand(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location,
        RpgMakerStoryResourceResolver? resources)
    {
        if (command.Code == 125)
        {
            var value = DescribeSignedValue(command.Parameters, 0, 1, 2, resources);
            return value is null
                ? Malformed(command, index, "Change Gold", diagnostics, location)
                : CreateBlock(command, index, StoryBlockKind.System, "Change Gold", $"Gold {value}");
        }

        if (command.Code == 129)
        {
            var actorId = ReadInt(command.Parameters, 0);
            var operation = ReadInt(command.Parameters, 1);
            if (actorId is not > 0 || operation is not (0 or 1) || !TryReadBoolean(command.Parameters, 2, out var initialize))
            {
                return Malformed(command, index, "Change Party Member", diagnostics, location);
            }
            ReportMissingResource(resources, resources?.HasActor(actorId.Value) ?? true, "actor", actorId.Value, diagnostics, location);
            var actor = resources?.DescribeActor(actorId.Value) ?? $"Actor #{actorId.Value}";
            return CreateBlock(command, index, StoryBlockKind.Actor, "Party Member",
                $"{(operation == 0 ? "Add" : "Remove")} {actor}",
                [$"Initialize: {initialize}"], resourceId: actorId.Value, targetId: actorId.Value);
        }

        var resourceId = ReadInt(command.Parameters, 0);
        var valueText = DescribeSignedValue(command.Parameters, 1, 2, 3, resources);
        var includeEquipment = false;
        if (command.Code is 127 or 128 && HasElement(command.Parameters, 4) &&
            !TryReadBoolean(command.Parameters, 4, out includeEquipment))
        {
            return Malformed(command, index, "Change inventory", diagnostics, location);
        }
        if (resourceId is not > 0 || valueText is null)
        {
            return Malformed(command, index, "Change inventory", diagnostics, location);
        }

        var (title, resource) = command.Code switch
        {
            126 => ("Change Items", resources?.DescribeItem(resourceId.Value) ?? $"Item #{resourceId.Value}"),
            127 => ("Change Weapons", resources?.DescribeWeapon(resourceId.Value) ?? $"Weapon #{resourceId.Value}"),
            _ => ("Change Armors", resources?.DescribeArmor(resourceId.Value) ?? $"Armor #{resourceId.Value}"),
        };
        var details = command.Code is 127 or 128 ? new[] { $"Include equipment: {includeEquipment}" } : [];
        return CreateBlock(command, index, StoryBlockKind.System, title, $"{resource} {valueText}", details,
            resourceId: resourceId.Value, targetId: resourceId.Value);
    }

    private static StoryBlock ParseSystemAccess(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location)
    {
        var enabled = ReadInt(command.Parameters, 0);
        if (enabled is not (0 or 1))
        {
            return Malformed(command, index, "System access", diagnostics, location);
        }
        var title = command.Code switch
        {
            134 => "Save Access",
            135 => "Menu Access",
            136 => "Encounter Access",
            _ => "Formation Access",
        };
        // rmmz_objects.js: 0 calls disable*, every non-zero editor value calls enable*.
        return CreateBlock(command, index, StoryBlockKind.System, title,
            enabled == 0 ? "Disabled" : "Enabled");
    }

    private static StoryBlock ParseScreenCommand(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location)
    {
        switch (command.Code)
        {
            case 211:
            {
                var transparent = ReadInt(command.Parameters, 0);
                return transparent is not (0 or 1)
                    ? Malformed(command, index, "Change Transparency", diagnostics, location)
                    : CreateBlock(command, index, StoryBlockKind.Screen, "Player Transparency",
                        transparent == 0 ? "Transparency → ON" : "Transparency → OFF");
            }
            case 221:
            case 222:
                return command.Parameters.ValueKind != JsonValueKind.Array || command.Parameters.GetArrayLength() != 0
                    ? Malformed(command, index, command.Code == 221 ? "Fadeout Screen" : "Fadein Screen", diagnostics, location)
                    : CreateBlock(command, index, StoryBlockKind.Screen,
                        command.Code == 221 ? "Fadeout Screen" : "Fadein Screen",
                        command.Code == 221 ? "Fade Out" : "Fade In");
            case 223:
            case 224:
            {
                if (!TryReadTone(command.Parameters, 0, out var tone) ||
                    ReadInt(command.Parameters, 1) is not { } frames || frames < 0 ||
                    !TryReadBoolean(command.Parameters, 2, out var wait))
                {
                    return Malformed(command, index, command.Code == 223 ? "Tint Screen" : "Flash Screen", diagnostics, location);
                }
                var action = command.Code == 223 ? "Tint" : "Flash";
                return CreateBlock(command, index, StoryBlockKind.Screen, $"{action} Screen",
                    $"{action} · {frames} frames · ~{FormatSeconds(frames)} s",
                    [$"R {tone[0]} · G {tone[1]} · B {tone[2]} · Gray {tone[3]}", $"Wait: {wait}", $"Frames (authoritative): {frames}"],
                    frameDuration: frames);
            }
            default:
            {
                var power = ReadInt(command.Parameters, 0);
                var speed = ReadInt(command.Parameters, 1);
                var frames = ReadInt(command.Parameters, 2);
                if (power is null || speed is null || frames is null || frames < 0 || !TryReadBoolean(command.Parameters, 3, out var wait))
                {
                    return Malformed(command, index, "Shake Screen", diagnostics, location);
                }
                return CreateBlock(command, index, StoryBlockKind.Screen, "Shake Screen",
                    $"Shake · Power {power} · Speed {speed} · {frames} frames · ~{FormatSeconds(frames.Value)} s",
                    [$"Wait: {wait}", $"Frames (authoritative): {frames}"], frameDuration: frames);
            }
        }
    }

    private static StoryBlock ParsePictureCommand(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location,
        RpgMakerStoryResourceResolver? resources)
    {
        var pictureId = ReadInt(command.Parameters, 0);
        if (pictureId is not > 0)
        {
            return Malformed(command, index, "Picture", diagnostics, location);
        }

        switch (command.Code)
        {
            case 231:
            {
                var name = ReadString(command.Parameters, 1);
                if (name is null || !TryDescribePicturePosition(command.Parameters, 3, 4, 5, resources, out var position) ||
                    ReadInt(command.Parameters, 6) is not { } scaleX || ReadInt(command.Parameters, 7) is not { } scaleY ||
                    ReadInt(command.Parameters, 8) is not { } opacity)
                {
                    return Malformed(command, index, "Show Picture", diagnostics, location);
                }
                return CreateBlock(command, index, StoryBlockKind.Picture, "Show Picture", $"Show #{pictureId} · {name}",
                    [$"Position: {position}", $"Scale: {scaleX}% × {scaleY}%", $"Opacity: {opacity}"], targetId: pictureId);
            }
            case 232:
            {
                if (!TryDescribePicturePosition(command.Parameters, 3, 4, 5, resources, out var position) ||
                    ReadInt(command.Parameters, 6) is not { } scaleX || ReadInt(command.Parameters, 7) is not { } scaleY ||
                    ReadInt(command.Parameters, 8) is not { } opacity || ReadInt(command.Parameters, 10) is not { } frames || frames < 0 ||
                    !TryReadBoolean(command.Parameters, 11, out var wait))
                {
                    return Malformed(command, index, "Move Picture", diagnostics, location);
                }
                return CreateBlock(command, index, StoryBlockKind.Picture, "Move Picture", $"Move #{pictureId} · {frames} frames · ~{FormatSeconds(frames)} s",
                    [$"Position: {position}", $"Scale: {scaleX}% × {scaleY}%", $"Opacity: {opacity}", $"Wait: {wait}"],
                    targetId: pictureId, frameDuration: frames);
            }
            case 233:
            {
                var speed = ReadInt(command.Parameters, 1);
                return speed is null
                    ? Malformed(command, index, "Rotate Picture", diagnostics, location)
                    : CreateBlock(command, index, StoryBlockKind.Picture, "Rotate Picture", $"Rotate #{pictureId} · Speed {speed}", targetId: pictureId);
            }
            case 234:
            {
                if (!TryReadTone(command.Parameters, 1, out var tone) ||
                    ReadInt(command.Parameters, 2) is not { } frames || frames < 0 ||
                    !TryReadBoolean(command.Parameters, 3, out var wait))
                {
                    return Malformed(command, index, "Tint Picture", diagnostics, location);
                }
                return CreateBlock(command, index, StoryBlockKind.Picture, "Tint Picture", $"Tint #{pictureId} · {frames} frames · ~{FormatSeconds(frames)} s",
                    [$"R {tone[0]} · G {tone[1]} · B {tone[2]} · Gray {tone[3]}", $"Wait: {wait}"],
                    targetId: pictureId, frameDuration: frames);
            }
            default:
                return CreateBlock(command, index, StoryBlockKind.Picture, "Erase Picture", $"Erase #{pictureId}", targetId: pictureId);
        }
    }

    private static StoryBlock ParseActorCommand(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location,
        RpgMakerStoryResourceResolver? resources)
    {
        if (command.Code == 303)
        {
            var directActorId = ReadInt(command.Parameters, 0);
            var maximumLength = ReadInt(command.Parameters, 1);
            if (directActorId is not > 0 || maximumLength is null || maximumLength < 0)
            {
                return Malformed(command, index, "Name Input", diagnostics, location);
            }
            ReportMissingResource(resources, resources?.HasActor(directActorId.Value) ?? true, "actor", directActorId.Value, diagnostics, location);
            var actor = resources?.DescribeActor(directActorId.Value) ?? $"Actor #{directActorId.Value}";
            return CreateBlock(command, index, StoryBlockKind.Actor, "Name Input", $"{actor} · {maximumLength} characters",
                resourceId: directActorId.Value, targetId: directActorId.Value);
        }

        if (command.Code == 320)
        {
            var directActorId = ReadInt(command.Parameters, 0);
            var name = ReadString(command.Parameters, 1);
            if (directActorId is not > 0 || name is null)
            {
                return Malformed(command, index, "Change Name", diagnostics, location);
            }
            ReportMissingResource(resources, resources?.HasActor(directActorId.Value) ?? true, "actor", directActorId.Value, diagnostics, location);
            return CreateBlock(command, index, StoryBlockKind.Actor, "Change Name",
                $"{resources?.DescribeActor(directActorId.Value) ?? $"Actor #{directActorId.Value}"} → {name}",
                resourceId: directActorId.Value, targetId: directActorId.Value);
        }

        if (command.Code == 322)
        {
            var directActorId = ReadInt(command.Parameters, 0);
            var characterName = ReadString(command.Parameters, 1);
            var characterIndex = ReadInt(command.Parameters, 2);
            var faceName = ReadString(command.Parameters, 3);
            var faceIndex = ReadInt(command.Parameters, 4);
            var battlerName = ReadString(command.Parameters, 5);
            if (directActorId is not > 0 || characterName is null || characterIndex is null || faceName is null || faceIndex is null || battlerName is null)
            {
                return Malformed(command, index, "Change Actor Images", diagnostics, location);
            }
            ReportMissingResource(resources, resources?.HasActor(directActorId.Value) ?? true, "actor", directActorId.Value, diagnostics, location);
            var actor = resources?.DescribeActor(directActorId.Value) ?? $"Actor #{directActorId.Value}";
            return CreateBlock(command, index, StoryBlockKind.Actor, "Change Actor Images", actor,
                [$"Character: {characterName} · index {characterIndex}", $"Face: {faceName} · index {faceIndex}", $"Battler: {battlerName}"],
                resourceId: directActorId.Value, targetId: directActorId.Value);
        }

        if (!TryDescribeActorTarget(command.Parameters, resources, out var target, out var actorId))
        {
            return Malformed(command, index, "Actor command", diagnostics, location);
        }
        if (actorId is > 0)
        {
            ReportMissingResource(resources, resources?.HasActor(actorId.Value) ?? true, "actor", actorId.Value, diagnostics, location);
        }

        switch (command.Code)
        {
            case 311:
            case 312:
            case 326:
            {
                var value = DescribeSignedValue(command.Parameters, 2, 3, 4, resources);
                var allowDeath = false;
                if (command.Code == 311 && !TryReadBoolean(command.Parameters, 5, out allowDeath) || value is null)
                {
                    return Malformed(command, index, "Change actor value", diagnostics, location);
                }
                var stat = command.Code switch { 311 => "HP", 312 => "MP", _ => "TP" };
                var details = command.Code == 311 ? new[] { $"Allow knockout: {allowDeath}" } : [];
                return CreateBlock(command, index, StoryBlockKind.Actor, $"Change {stat}", $"{target} · {stat} {value}", details,
                    targetId: actorId);
            }
            case 313:
            {
                var operation = ReadInt(command.Parameters, 2);
                var stateId = ReadInt(command.Parameters, 3);
                if (operation is not (0 or 1) || stateId is not > 0)
                {
                    return Malformed(command, index, "Change State", diagnostics, location);
                }
                ReportMissingResource(resources, resources?.HasState(stateId.Value) ?? true, "state", stateId.Value, diagnostics, location);
                var state = resources?.DescribeState(stateId.Value) ?? $"State #{stateId.Value}";
                return CreateBlock(command, index, StoryBlockKind.Actor, "Change State",
                    $"{target} · {(operation == 0 ? "Add" : "Remove")} {state}",
                    resourceId: stateId.Value, targetId: actorId);
            }
            case 314:
                return CreateBlock(command, index, StoryBlockKind.Actor, "Recover All", target, targetId: actorId);
            case 315:
            case 316:
            {
                var value = DescribeSignedValue(command.Parameters, 2, 3, 4, resources);
                if (value is null || !TryReadBoolean(command.Parameters, 5, out var showLevelUp))
                {
                    return Malformed(command, index, command.Code == 315 ? "Change EXP" : "Change Level", diagnostics, location);
                }
                var stat = command.Code == 315 ? "EXP" : "Level";
                return CreateBlock(command, index, StoryBlockKind.Actor, $"Change {stat}", $"{target} · {stat} {value}",
                    [$"Show level up: {showLevelUp}"], targetId: actorId);
            }
            case 318:
            {
                var operation = ReadInt(command.Parameters, 2);
                var skillId = ReadInt(command.Parameters, 3);
                if (operation is not (0 or 1) || skillId is not > 0)
                {
                    return Malformed(command, index, "Change Skill", diagnostics, location);
                }
                ReportMissingResource(resources, resources?.HasSkill(skillId.Value) ?? true, "skill", skillId.Value, diagnostics, location);
                var skill = resources?.DescribeSkill(skillId.Value) ?? $"Skill #{skillId.Value}";
                return CreateBlock(command, index, StoryBlockKind.Actor, "Change Skill",
                    $"{target} · {(operation == 0 ? "Learn" : "Forget")} {skill}",
                    resourceId: skillId.Value, targetId: actorId);
            }
            default:
                return Malformed(command, index, "Actor command", diagnostics, location);
        }
    }

    private static StoryBlock ParseEnemyHp(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location,
        RpgMakerStoryResourceResolver? resources)
    {
        var enemyIndex = ReadInt(command.Parameters, 0);
        var value = DescribeSignedValue(command.Parameters, 1, 2, 3, resources);
        if (enemyIndex is null || value is null || !TryReadBoolean(command.Parameters, 4, out var allowDeath))
        {
            return Malformed(command, index, "Change Enemy HP", diagnostics, location);
        }
        var target = enemyIndex.Value < 0 ? "Entire Troop" : $"Enemy #{enemyIndex.Value + 1}";
        return CreateBlock(command, index, StoryBlockKind.Battle, "Change Enemy HP", $"{target} · HP {value}",
            [$"Allow death: {allowDeath}", $"Troop index: {enemyIndex.Value}"], targetId: enemyIndex.Value);
    }

    private static StoryBlock ParseLegacyPluginCommand(
        RpgMakerEventCommand command,
        int index,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location)
    {
        var raw = ReadString(command.Parameters, 0);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Malformed(command, index, "Plugin Command MV", diagnostics, location);
        }
        var commandName = raw.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        return CreateBlock(command, index, StoryBlockKind.PluginCommand, "Legacy Plugin Command", commandName,
            [$"Command: {raw}"], rawText: raw);
    }

    private static StoryBlock Malformed(
        RpgMakerEventCommand command,
        int index,
        string knownName,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location)
    {
        diagnostics.Add(location.CreateDiagnostic(
            "story.semantic-command-malformed", StoryDiagnosticSeverity.Warning,
            $"{knownName} ha una parameter shape non valida; il comando raw è stato conservato."));
        return CreateMalformedKnownBlock([command], index, index, knownName, location);
    }

    private static string? DescribeSignedValue(
        JsonElement parameters,
        int operationIndex,
        int operandTypeIndex,
        int operandIndex,
        RpgMakerStoryResourceResolver? resources)
    {
        var operation = ReadInt(parameters, operationIndex);
        var operandType = ReadInt(parameters, operandTypeIndex);
        var operand = ReadInt(parameters, operandIndex);
        if (operation is not (0 or 1) || operandType is not (0 or 1) || operand is null)
        {
            return null;
        }
        if (operandType == 0)
        {
            var signed = operation == 0 ? operand.Value : -operand.Value;
            return signed >= 0 ? $"+{signed}" : signed.ToString(CultureInfo.InvariantCulture);
        }
        var variable = DescribeVariableReference(operand.Value, resources);
        return operation == 0 ? $"+{variable}" : $"-{variable}";
    }

    private static bool TryDescribeActorTarget(
        JsonElement parameters,
        RpgMakerStoryResourceResolver? resources,
        out string target,
        out int? actorId)
    {
        target = string.Empty;
        actorId = null;
        var designation = ReadInt(parameters, 0);
        var value = ReadInt(parameters, 1);
        if (designation is not (0 or 1) || value is null)
        {
            return false;
        }
        if (designation == 1)
        {
            target = $"Actor referenced by {DescribeVariableReference(value.Value, resources)}";
            return true;
        }
        actorId = value.Value;
        target = value.Value == 0
            ? "Entire Party"
            : DescribeActorReference(value.Value, resources);
        return value.Value >= 0;
    }

    private static bool TryDescribePicturePosition(
        JsonElement parameters,
        int designationIndex,
        int xIndex,
        int yIndex,
        RpgMakerStoryResourceResolver? resources,
        out string position)
    {
        position = string.Empty;
        var designation = ReadInt(parameters, designationIndex);
        var x = ReadInt(parameters, xIndex);
        var y = ReadInt(parameters, yIndex);
        if (designation is not (0 or 1) || x is null || y is null)
        {
            return false;
        }
        position = designation == 0
            ? $"X {x} · Y {y}"
            : $"X {DescribeVariableReference(x.Value, resources)} · Y {DescribeVariableReference(y.Value, resources)}";
        return true;
    }

    private static bool TryReadTone(JsonElement parameters, int index, out int[] tone)
    {
        tone = [];
        var element = GetElement(parameters, index);
        if (element is not { ValueKind: JsonValueKind.Array } values || values.GetArrayLength() != 4)
        {
            return false;
        }
        var components = new int[4];
        for (var component = 0; component < components.Length; component++)
        {
            if (values[component].ValueKind != JsonValueKind.Number || !values[component].TryGetInt32(out components[component]))
            {
                return false;
            }
        }
        tone = components;
        return true;
    }

    private static string FormatSeconds(int frames) => (frames / 60d).ToString("0.0", CultureInfo.InvariantCulture);

    private static void ReportMissingResource(
        RpgMakerStoryResourceResolver? resources,
        bool resourceExists,
        string category,
        int id,
        ICollection<StoryDiagnostic> diagnostics,
        StoryCommandLocation location)
    {
        if (resources is not null && !resourceExists)
        {
            diagnostics.Add(location.CreateDiagnostic(
                "story.resource-target-missing", StoryDiagnosticSeverity.Warning,
                $"{category} #{id} non è risolvibile nel database; il riferimento è stato conservato."));
        }
    }

    private static bool TryReadBoolean(JsonElement parameters, int index, out bool value)
    {
        var element = GetElement(parameters, index);
        switch (element?.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
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
        IReadOnlyList<string>? details = null,
        string? rawText = null,
        int? resourceId = null,
        int? targetId = null,
        string? labelName = null,
        int? commonEventId = null,
        int? frameDuration = null) => CreateBlock(
            [command], index, index, kind, title, summary, rawText: rawText, details: details,
            resourceId: resourceId, targetId: targetId, labelName: labelName,
            commonEventId: commonEventId, frameDuration: frameDuration);

    private static StoryBlock CreateBlock(
        IReadOnlyList<RpgMakerEventCommand> commands,
        int start,
        int end,
        StoryBlockKind kind,
        string title,
        string summary,
        string? rawText = null,
        IReadOnlyList<string>? details = null,
        IReadOnlyList<ZiapStudio.Core.Localization.LocalizationReferenceOrigin>? origins = null,
        int? resourceId = null,
        int? targetId = null,
        string? labelName = null,
        int? commonEventId = null,
        int? frameDuration = null) => new()
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
        ResourceId = resourceId,
        TargetId = targetId,
        LabelName = labelName,
        CommonEventId = commonEventId,
        FrameDuration = frameDuration,
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
        243 => "Save BGM",
        244 => "Resume BGM",
        245 => "Play BGS",
        246 => "Fadeout BGS",
        249 => "Play ME",
        250 => "Play SE",
        _ => "Stop SE",
    };

    private static string DescribeAudio(RpgMakerEventCommand command)
    {
        if (command.Code is 243 or 244 or 251)
        {
            return command.Code switch
            {
                243 => "Save the current BGM for later replay.",
                244 => "Replay the saved BGM.",
                _ => "Stop all sound effects.",
            };
        }
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

    private static bool HasElement(JsonElement parameters, int index) => GetElement(parameters, index) is not null;

    private static string? ReadString(JsonElement parameters, int index) =>
        GetElement(parameters, index) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static string? ReadString(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? ReadInt(JsonElement parameters, int index) =>
        GetElement(parameters, index) is { ValueKind: JsonValueKind.Number } value &&
        value.TryGetInt32(out var result) ? result : null;

    private static int? ReadInt(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;

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
