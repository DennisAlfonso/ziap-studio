namespace ZiapStudio.Core.Fusion.Bosses;

public sealed record FusionBossArenaDefinition
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string EncounterId { get; init; } = string.Empty;

    public IReadOnlyList<FusionBossMapScene> Maps { get; init; } = [];
}

public sealed record FusionBossMapScene
{
    public int MapId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public int Width { get; init; }

    public int Height { get; init; }

    public int TileWidth { get; init; } = 48;

    public int TileHeight { get; init; } = 48;

    public int ScrollType { get; init; }

    public int TilesetId { get; init; }

    public string TilesetName { get; init; } = string.Empty;

    public IReadOnlyList<string> TilesetNames { get; init; } = [];

    public IReadOnlyList<int> TilesetFlags { get; init; } = [];

    public IReadOnlyList<int> MapData { get; init; } = [];

    public string ParallaxName { get; init; } = string.Empty;

    public bool ParallaxLoopX { get; init; }

    public bool ParallaxLoopY { get; init; }

    public double ParallaxSx { get; init; }

    public double ParallaxSy { get; init; }

    public IReadOnlyList<FusionBossMapMarker> Markers { get; init; } = [];
}

public enum FusionBossMapMarkerKind
{
    Anchor,
    Role,
}

public sealed record FusionBossMapMarker
{
    public FusionBossMapMarkerKind Kind { get; init; }

    public string Id { get; init; } = string.Empty;

    public int EventId { get; init; }

    public string EventName { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }
}
