namespace ZiapStudio.Core.Fusion.Puzzles;

public sealed record FusionPuzzlePluginStatus
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public bool IsActive { get; init; }
    public string StatusText => IsActive ? "Attivo" : IsRequired ? "Richiesto" : "Non attivo";
}

public sealed record FusionPuzzleRoleRequirement
{
    public string Role { get; init; } = string.Empty;
    public int Minimum { get; init; }
    public int? Maximum { get; init; }
    public int Found { get; init; }
    public bool IsSatisfied => Found >= Minimum && (Maximum is null || Found <= Maximum);
    public string RequirementText => Maximum == Minimum
        ? $"esatti: {Minimum}"
        : Maximum is null ? $"minimo: {Minimum}" : $"{Minimum}-{Maximum}";
    public string FoundText => $"Trovati: {Found}";
    public string StatusGlyph => IsSatisfied ? "OK" : "!";
}

public sealed record FusionPuzzleSourceDefinition
{
    public string Key { get; init; } = string.Empty;
    public int? Budget { get; init; }
    public string BudgetText => Budget is null ? "Budget non definito" : $"Budget: {Budget}";
}

public sealed record FusionPuzzleSetting
{
    public string Group { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed record FusionPuzzleMapComponent
{
    public string PuzzleId { get; init; } = string.Empty;
    public int MapId { get; init; }
    public string MapName { get; init; } = string.Empty;
    public int EventId { get; init; }
    public string EventName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string? Key { get; init; }
    public string? Value { get; init; }
    public string Source { get; init; } = string.Empty;
    public string MapText => $"Map {MapId:000} - {MapName}";
    public string EventText => $"Evento #{EventId} - {EventName}";
    public string MetadataText => string.Join(" | ", new[]
    {
        string.IsNullOrWhiteSpace(Key) ? null : $"key={Key}",
        string.IsNullOrWhiteSpace(Value) ? null : $"value={Value}",
        Source,
    }.Where(value => value is not null));
}

public sealed record FusionPuzzleBinding
{
    public string Id { get; init; } = string.Empty;
    public string ArenaId { get; init; } = string.Empty;
    public string ProviderInstanceId { get; init; } = string.Empty;
    public string Signal { get; init; } = string.Empty;
    public IReadOnlyList<string> Actions { get; init; } = [];
    public string ActionsText => Actions.Count == 0 ? "Nessuna azione" : string.Join(", ", Actions);
}

public sealed record FusionPuzzleArenaUsage
{
    public string ArenaId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ProviderInstanceId { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public IReadOnlyList<int> MapIds { get; init; } = [];
    public IReadOnlyList<FusionPuzzleBinding> Bindings { get; init; } = [];
    public string MapText => MapIds.Count == 0
        ? "Nessuna mappa"
        : string.Join(", ", MapIds.Select(id => $"Map {id:000}"));
    public string RequirementText => IsRequired ? "Provider richiesto" : "Provider opzionale";
}

public sealed record FusionPuzzleDependency
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public bool IsAvailable { get; init; }
    public string Purpose { get; init; } = string.Empty;
    public string StatusText => IsAvailable ? "Disponibile" : IsRequired ? "Mancante" : "Opzionale";
}

public enum FusionPuzzleCallKind
{
    Startup,
    Validation,
    Execution,
    Signal,
    Completion,
    Cleanup,
}

public sealed record FusionPuzzleCall
{
    public int Order { get; init; }
    public FusionPuzzleCallKind Kind { get; init; }
    public string Caller { get; init; } = string.Empty;
    public string Api { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string OrderText => Order.ToString("00");
    public string KindText => Kind.ToString();
}

public enum FusionPuzzleDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record FusionPuzzleDiagnostic
{
    public string Code { get; init; } = string.Empty;
    public FusionPuzzleDiagnosticSeverity Severity { get; init; }
    public string PuzzleId { get; init; } = string.Empty;
    public int? MapId { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string SeverityText => Severity switch
    {
        FusionPuzzleDiagnosticSeverity.Error => "Errore",
        FusionPuzzleDiagnosticSeverity.Warning => "Avviso",
        _ => "Info",
    };
}

public enum FusionPuzzleGraphNodeKind
{
    Database,
    Startup,
    Validation,
    Provider,
    State,
    Component,
    Signal,
    Completion,
    Cleanup,
    Missing,
}

public sealed record FusionPuzzleGraphNode
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string TechnicalId { get; init; } = string.Empty;
    public FusionPuzzleGraphNodeKind Kind { get; init; }
    public int Level { get; init; }
    public string BadgeText { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Execution { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public bool IsWarning { get; init; }
}

public sealed record FusionPuzzleGraphEdge
{
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed record FusionPuzzleGraph
{
    public IReadOnlyList<FusionPuzzleGraphNode> Nodes { get; init; } = [];
    public IReadOnlyList<FusionPuzzleGraphEdge> Edges { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}

public sealed record FusionPuzzleDefinition
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string BudgetPolicy { get; init; } = string.Empty;
    public bool Repeatable { get; init; }
    public int? DefinitionVersion { get; init; }
    public IReadOnlyList<FusionPuzzleRoleRequirement> Roles { get; init; } = [];
    public IReadOnlyList<FusionPuzzleSourceDefinition> Sources { get; init; } = [];
    public IReadOnlyList<FusionPuzzleSetting> Settings { get; init; } = [];
    public IReadOnlyList<FusionPuzzleMapComponent> Components { get; init; } = [];
    public IReadOnlyList<FusionPuzzleArenaUsage> ArenaUsages { get; init; } = [];
    public IReadOnlyList<FusionPuzzleDependency> Dependencies { get; init; } = [];
    public IReadOnlyList<FusionPuzzleCall> Calls { get; init; } = [];
    public IReadOnlyList<FusionPuzzleDiagnostic> Diagnostics { get; init; } = [];
    public FusionPuzzleGraph Graph { get; init; } = new();
    public int ErrorCount => Diagnostics.Count(issue => issue.Severity == FusionPuzzleDiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(issue => issue.Severity == FusionPuzzleDiagnosticSeverity.Warning);
    public bool IsValid => ErrorCount == 0;
    public string StatusText => IsValid ? "Configurazione leggibile" : $"{ErrorCount} errori";
    public string SummaryText => $"{ProviderId} | {Components.Count} componenti | {ArenaUsages.Count} avvii";
}
