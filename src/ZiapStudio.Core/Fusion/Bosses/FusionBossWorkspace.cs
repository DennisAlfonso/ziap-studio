namespace ZiapStudio.Core.Fusion.Bosses;

public enum FusionBossDatabaseKind
{
    Combat,
    Encounters,
    Arenas,
    Puzzles,
}

public sealed record FusionBossPluginStatus
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool IsRequired { get; init; }

    public bool IsActive { get; init; }
}

public sealed record FusionBossCollectionSummary
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public IReadOnlyList<string> RecordIds { get; init; } = [];
}

public sealed record FusionBossDatabaseSummary
{
    public FusionBossDatabaseKind Kind { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public bool Exists { get; init; }

    public int? SchemaVersion { get; init; }

    public string? DatabaseVersion { get; init; }

    public IReadOnlyList<FusionBossCollectionSummary> Collections { get; init; } = [];

    public int RecordCount => Collections.Sum(collection => collection.RecordIds.Count);
}

public enum FusionBossDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record FusionBossDiagnostic
{
    public string Code { get; init; } = string.Empty;

    public FusionBossDiagnosticSeverity Severity { get; init; }

    public FusionBossDatabaseKind? Database { get; init; }

    public string? CollectionId { get; init; }

    public string? RecordId { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Details { get; init; }

    public Uri NavigationTarget { get; init; } = new("fusionboss://workspace/");
}
