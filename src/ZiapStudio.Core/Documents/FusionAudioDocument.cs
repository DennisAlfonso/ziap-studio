using ZiapStudio.Core.Fusion.Audio;
using ZiapStudio.Core.Editing;

namespace ZiapStudio.Core.Documents;

public sealed record FusionAudioDocument : StudioDocument
{
    public string SourcePath { get; init; } = string.Empty;

    public FusionAudioCatalog Catalog { get; init; } = new();

    public DocumentSourceSnapshot? SourceSnapshot { get; init; }

    public IReadOnlyList<FusionAudioResolvedAsset> Assets { get; init; } = [];

    public IReadOnlyList<FusionAudioFileOption> AvailableFiles { get; init; } = [];

    public IReadOnlyList<FusionAudioDiagnostic> Diagnostics { get; init; } = [];

    public bool PluginIsActive { get; init; }

    public int EventCount => Catalog.Sounds.Count;

    public int AssetCount => Assets
        .Where(asset => asset.Exists)
        .Select(asset => asset.ResolvedPath)
        .Where(path => path is not null)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    public bool IsValid => Diagnostics.All(diagnostic =>
        diagnostic.Severity != FusionAudioDiagnosticSeverity.Error);
}
