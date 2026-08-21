namespace ZiapStudio.Core.Fusion.Bosses;

public sealed record FusionBossDocumentation
{
    public string ProjectPath { get; init; } = string.Empty;

    public IReadOnlyList<FusionBossActionApiEntry> Actions { get; init; } = [];

    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

public sealed record FusionBossActionApiEntry
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string IconGlyph { get; init; } = string.Empty;

    public string Signature { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public int SourceLine { get; init; }

    public string SourceLocationText => $"{SourcePath}:{SourceLine}";
}
