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
    public IReadOnlyList<StoryBlock> Blocks { get; init; } = [];

    public string DisplayName => $"Page {Number}";
}

public sealed record StoryCommonEvent
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public int Trigger { get; init; }
    public int SwitchId { get; init; }
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
    Audio,
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

    public string KindText => Kind switch
    {
        StoryBlockKind.Dialogue => "DIALOGO",
        StoryBlockKind.Choices => "SCELTE",
        StoryBlockKind.Comment => "COMMENTO",
        StoryBlockKind.Script => "SCRIPT",
        StoryBlockKind.PluginCommand => "PLUGIN",
        StoryBlockKind.MovementRoute => "MOVIMENTO",
        StoryBlockKind.Audio => "AUDIO",
        StoryBlockKind.Wait => "WAIT",
        StoryBlockKind.SwitchVariable => "LOGICA",
        StoryBlockKind.Transfer => "TRASFERIMENTO",
        StoryBlockKind.ControlFlow => "CONTROL FLOW",
        _ => "RAW",
    };

    public string CommandRangeText => CommandStartIndex == CommandEndIndex
        ? $"Command {CommandStartIndex}"
        : $"Commands {CommandStartIndex}–{CommandEndIndex}";

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
