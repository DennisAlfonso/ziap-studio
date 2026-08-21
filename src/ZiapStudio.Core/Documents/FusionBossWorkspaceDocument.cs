using ZiapStudio.Core.Fusion.Bosses;

namespace ZiapStudio.Core.Documents;

public sealed record FusionBossWorkspaceDocument : StudioDocument
{
    public IReadOnlyList<FusionBossPluginStatus> Plugins { get; init; } = [];

    public IReadOnlyList<FusionBossDatabaseSummary> Databases { get; init; } = [];

    public IReadOnlyList<FusionBossDiagnostic> Diagnostics { get; init; } = [];

    public IReadOnlyList<FusionBossEncounterDefinition> Encounters { get; init; } = [];

    public IReadOnlyList<FusionBossArenaDefinition> Arenas { get; init; } = [];

    public IReadOnlyList<FusionBossAttackGeometry> AttackGeometries { get; init; } = [];

    public string ProjectPath { get; init; } = string.Empty;

    public bool PluginIsActive => Plugins.Any(plugin => plugin.IsActive);

    public int ErrorCount => Diagnostics.Count(diagnostic =>
        diagnostic.Severity == FusionBossDiagnosticSeverity.Error);

    public int WarningCount => Diagnostics.Count(diagnostic =>
        diagnostic.Severity == FusionBossDiagnosticSeverity.Warning);

    public bool IsValid => ErrorCount == 0;

    public int GetRecordCount(string collectionId) => Databases
        .SelectMany(database => database.Collections)
        .Where(collection => collection.Id.Equals(
            collectionId,
            StringComparison.OrdinalIgnoreCase))
        .Sum(collection => collection.RecordIds.Count);
}
