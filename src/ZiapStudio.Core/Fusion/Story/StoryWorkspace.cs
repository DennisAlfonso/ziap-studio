using ZiapStudio.Core.Localization;

namespace ZiapStudio.Core.Fusion.Story;

/// <summary>
/// Read-only semantic projection of RPG Maker event commands. Source command data is
/// retained on every block so future authoring work does not need to re-infer it.
/// </summary>
public sealed record StoryWorkspace
{
    public IReadOnlyList<StoryMap> Maps { get; init; } = [];
    public IReadOnlyList<StoryCommonEvent> CommonEvents { get; init; } = [];
    public IReadOnlyList<StoryDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record StoryMap
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    /// <summary>RPG Maker's stable ordering metadata from MapInfos.json.</summary>
    public int Order { get; init; }
    /// <summary>RPG Maker parent map identifier, retained without mutating MapInfos.json.</summary>
    public int ParentId { get; init; }
    /// <summary>Initial navigator expansion hint supplied by RPG Maker.</summary>
    public bool RpgMakerExpanded { get; init; }
    public required string SourcePath { get; init; }
    public IReadOnlyList<StoryEvent> Events { get; init; } = [];

    public string DisplayName => $"{Id:000} — {Name}";
}

public sealed record StoryEvent
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public IReadOnlyList<StoryPage> Pages { get; init; } = [];

    public string DisplayName => $"Event {Id} — {Name}";
}

public sealed record StoryPage
{
    public required int Number { get; init; }
    public required int Trigger { get; init; }
    public string ConditionsSummary { get; init; } = "Nessuna condizione";
    public StoryCommandListTarget? CommandListTarget { get; init; }
    public IReadOnlyList<StoryBlock> Blocks { get; init; } = [];

    public string DisplayName => $"Page {Number}";
}

public sealed record StoryCommonEvent
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public int Trigger { get; init; }
    public int SwitchId { get; init; }
    public StoryCommandListTarget? CommandListTarget { get; init; }
    public IReadOnlyList<StoryBlock> Blocks { get; init; } = [];

    public string DisplayName => $"{Id:000} — {Name}";
}

public enum StoryBlockKind
{
    Dialogue,
    Choices,
    Comment,
    Script,
    PluginCommand,
    MovementRoute,
    Animation,
    Audio,
    Screen,
    Picture,
    System,
    Actor,
    Battle,
    Wait,
    SwitchVariable,
    Transfer,
    ControlFlow,
    Raw,
}

public sealed record StoryBlock
{
    public required StoryBlockKind Kind { get; init; }
    public required string Title { get; init; }
    public string Summary { get; init; } = string.Empty;
    public int CommandStartIndex { get; init; }
    public int CommandEndIndex { get; init; }
    public int Indent { get; init; }
    public string RawText { get; init; } = string.Empty;
    public string DisplayText { get; init; } = string.Empty;
    public string RawParameters { get; init; } = "[]";
    public IReadOnlyList<StoryRawCommand> SourceCommands { get; init; } = [];
    public IReadOnlyList<string> Details { get; init; } = [];
    public IReadOnlyList<LocalizationReferenceOrigin> LocalizationOrigins { get; init; } = [];
    /// <summary>Read-only RPG Maker resource identity retained for future navigation/graph work.</summary>
    public int? ResourceId { get; init; }
    /// <summary>Read-only command target identity where the command schema has one.</summary>
    public int? TargetId { get; init; }
    public string? LabelName { get; init; }
    public int? CommonEventId { get; init; }
    /// <summary>Authoritative RPG Maker duration in frames, never the derived seconds display.</summary>
    public int? FrameDuration { get; init; }
    /// <summary>Zero-based option identity from a Show Choices command when this is a choice branch.</summary>
    public int? ChoiceIndex { get; init; }
    /// <summary>Command index of the structurally associated Show Choices command, when known.</summary>
    public int? ChoiceSourceCommandIndex { get; init; }
    /// <summary>Marks a structural use of another narrative value, not an independent authored string.</summary>
    public bool IsDerivedStructuralUsage { get; init; }

    public string KindText => Kind switch
    {
        StoryBlockKind.Dialogue => "DIALOGO",
        StoryBlockKind.Choices => "SCELTE",
        StoryBlockKind.Comment => "COMMENTO",
        StoryBlockKind.Script => "SCRIPT",
        StoryBlockKind.PluginCommand => "PLUGIN",
        StoryBlockKind.MovementRoute => "MOVIMENTO",
        StoryBlockKind.Animation => "ANIMAZIONE",
        StoryBlockKind.Audio => "AUDIO",
        StoryBlockKind.Screen => "SCREEN",
        StoryBlockKind.Picture => "PICTURE",
        StoryBlockKind.System => "SYSTEM",
        StoryBlockKind.Actor => "ACTOR",
        StoryBlockKind.Battle => "BATTLE",
        StoryBlockKind.Wait => "WAIT",
        StoryBlockKind.SwitchVariable => "LOGICA",
        StoryBlockKind.Transfer => "TRASFERIMENTO",
        StoryBlockKind.ControlFlow => "CONTROL FLOW",
        _ => "RAW",
    };

    public string CommandRangeText => CommandStartIndex == CommandEndIndex
        ? $"Command {CommandStartIndex}"
        : $"Commands {CommandStartIndex}–{CommandEndIndex}";

    /// <summary>Compact, bounded presentation depth derived directly from RPG Maker indent.</summary>
    public int DisplayIndent => Math.Clamp(Indent, 0, 6);

    public string IndentText => Indent <= 0 ? "root" : $"depth {Indent}";

    public bool ShowsNarrativeLocalization =>
        Kind is StoryBlockKind.Dialogue or StoryBlockKind.Choices && LocalizationOrigins.Count > 0;

    public string LocalizationOriginText => LocalizationOrigins.Count == 0
        ? "—"
        : string.Join(", ", LocalizationOrigins.Select(origin =>
            $"{origin.SourceFile} · {origin.Path}"));
}

public sealed record StoryRawCommand
{
    public required int Code { get; init; }
    public required int Indent { get; init; }
    public required string Parameters { get; init; }
}

public enum StoryDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record StoryDiagnostic
{
    public required string Code { get; init; }
    public required StoryDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
    public string? SourcePath { get; init; }
    public int? MapId { get; init; }
    public int? EventId { get; init; }
    public int? Page { get; init; }

    public string SeverityText => Severity == StoryDiagnosticSeverity.Error ? "Errore" : "Avviso";
}
