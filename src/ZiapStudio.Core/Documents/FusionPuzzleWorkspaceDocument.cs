using ZiapStudio.Core.Fusion.Puzzles;

namespace ZiapStudio.Core.Documents;

public sealed record FusionPuzzleWorkspaceDocument : StudioDocument
{
    public string ProjectPath { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public int? SchemaVersion { get; init; }
    public string? DatabaseVersion { get; init; }
    public IReadOnlyList<FusionPuzzlePluginStatus> Plugins { get; init; } = [];
    public IReadOnlyList<FusionPuzzleDefinition> Puzzles { get; init; } = [];
    public IReadOnlyList<FusionPuzzleDiagnostic> Diagnostics { get; init; } = [];
    public int ErrorCount => Diagnostics.Count(issue => issue.Severity == FusionPuzzleDiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(issue => issue.Severity == FusionPuzzleDiagnosticSeverity.Warning);
    public bool IsValid => ErrorCount == 0;
}
