using ZiapStudio.Core.Fusion.World;

namespace ZiapStudio.Core.Documents;

public sealed record FusionWorldWorkspaceDocument : StudioDocument
{
    public string ProjectPath { get; init; } = string.Empty;

    public IReadOnlyList<FusionWorldTileset> Tilesets { get; init; } = [];

    public IReadOnlyList<FusionWorldMap> Maps { get; init; } = [];

    public IReadOnlyList<FusionWorldAsset> Assets { get; init; } = [];

    public IReadOnlyList<FusionWorldRegion> Regions { get; init; } = [];

    public string RegionRegistryPath { get; init; } = string.Empty;

    public bool RegionRegistryExists { get; init; }

    public IReadOnlyList<FusionWorldDiagnostic> Diagnostics { get; init; } = [];

    public int UsedTilesetCount => Tilesets.Count(tileset => tileset.IsInUse);

    public int OrganizedCategoryCount => Assets
        .Where(asset => asset.Exists && asset.AssetPath.Contains('/'))
        .Select(asset => asset.AssetPath.Split('/', 2)[0])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    public int MissingAssetCount => Assets.Count(asset => !asset.Exists);

    public int OrphanAssetCount => Assets.Count(asset => asset.IsOrphan);

    public int DuplicateAssetCount => Assets.Count(asset => asset.IsDuplicate);

    public int RegisteredRegionCount => Regions.Count(region => region.IsRegistered);

    public int ActiveRegionCount => Regions.Count(region => region.CellCount > 0);

    public int ErrorCount => Diagnostics.Count(diagnostic =>
        diagnostic.Severity == FusionWorldDiagnosticSeverity.Error);

    public int WarningCount => Diagnostics.Count(diagnostic =>
        diagnostic.Severity == FusionWorldDiagnosticSeverity.Warning);
}
