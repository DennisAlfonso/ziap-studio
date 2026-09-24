namespace ZiapStudio.Core.Fusion.World;

public enum FusionWorldDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record FusionWorldDiagnostic
{
    public required string Code { get; init; }
    public required FusionWorldDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
    public string? Details { get; init; }
    public int? TilesetId { get; init; }
    public int? MapId { get; init; }
    public string? AssetPath { get; init; }

    public string SeverityText => Severity == FusionWorldDiagnosticSeverity.Error
        ? "Errore"
        : "Avviso";
}

public sealed record FusionWorldMap
{
    public required int Id { get; init; }
    public required string DisplayName { get; init; }
    public required int TilesetId { get; init; }
    public required string TilesetName { get; init; }
    public required string SourcePath { get; init; }

    // Il renderer condiviso usa lo stesso contratto serializzato delle scene arena.
    // Questi campi restano read-only e rappresentano fedelmente MapXXX.json/Tilesets.json.
    public int MapId => Id;
    public int Width { get; init; }
    public int Height { get; init; }
    public int TileWidth { get; init; } = 48;
    public int TileHeight { get; init; } = 48;
    public int ScrollType { get; init; }
    public IReadOnlyList<string> TilesetNames { get; init; } = [];
    public IReadOnlyList<int> TilesetFlags { get; init; } = [];
    public IReadOnlyList<int> MapData { get; init; } = [];
    public string ParallaxName { get; init; } = string.Empty;
    public bool ParallaxLoopX { get; init; }
    public bool ParallaxLoopY { get; init; }
    public double ParallaxSx { get; init; }
    public double ParallaxSy { get; init; }

    public string IdText => $"Map{Id:000}";
    public string TilesetText => $"{TilesetId:00} · {TilesetName}";
}

public sealed record FusionWorldMapUsage
{
    public required int MapId { get; init; }
    public required string MapName { get; init; }
    public required string SourcePath { get; init; }

    public string DisplayText => $"Map{MapId:000} · {MapName}";
}

public sealed record FusionWorldRegionUsage
{
    public required int MapId { get; init; }
    public required string MapName { get; init; }
    public required string SourcePath { get; init; }
    public required int CellCount { get; init; }

    public string MapText => $"Map{MapId:000} · {MapName}";
    public string CellCountText => CellCount == 1 ? "1 cella" : $"{CellCount} celle";
}

public sealed record FusionWorldRegion
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Owner { get; init; }
    public required bool IsReserved { get; init; }
    public required bool IsRegistered { get; init; }
    public required IReadOnlyList<FusionWorldRegionUsage> Usages { get; init; }

    public int MapCount => Usages.Count;
    public int CellCount => Usages.Sum(usage => usage.CellCount);
    public string IdText => Id.ToString();
    public string MapCountText => MapCount == 1 ? "1 mappa" : $"{MapCount} mappe";
    public string CellCountText => CellCount == 1 ? "1 cella" : $"{CellCount} celle";
    public string StatusText => IsRegistered ? "Registrata" : "Da registrare";
    public string ReservedText => IsReserved ? "Riservata" : "Non riservata";
}

public sealed record FusionWorldTilesetSlot
{
    public required string Slot { get; init; }
    public required string AssetPath { get; init; }
    public required bool Exists { get; init; }

    public string DisplayPath => string.IsNullOrWhiteSpace(AssetPath) ? "—" : AssetPath;
    public string StatusText => string.IsNullOrWhiteSpace(AssetPath)
        ? "Vuoto"
        : Exists ? "Disponibile" : "Mancante";
}

public sealed record FusionWorldTileset
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<FusionWorldTilesetSlot> Slots { get; init; }
    public required IReadOnlyList<FusionWorldMapUsage> Maps { get; init; }

    public int AssetCount => Slots.Count(slot => !string.IsNullOrWhiteSpace(slot.AssetPath));
    public int MissingAssetCount => Slots.Count(slot =>
        !string.IsNullOrWhiteSpace(slot.AssetPath) && !slot.Exists);
    public bool IsInUse => Maps.Count > 0;
    public string IdText => Id.ToString("00");
    public string StatusText => IsInUse ? "In uso" : "Non usato";
    public string MapCountText => Maps.Count == 1 ? "1 mappa" : $"{Maps.Count} mappe";
}

public sealed record FusionWorldAsset
{
    public required string AssetPath { get; init; }
    public required string PhysicalPath { get; init; }
    public required bool Exists { get; init; }
    public long? SizeBytes { get; init; }
    public string? Sha256 { get; init; }
    public required IReadOnlyList<FusionWorldTileset> Tilesets { get; init; }
    public required IReadOnlyList<FusionWorldMapUsage> Maps { get; init; }
    public bool IsOrphan { get; init; }
    public bool IsDuplicate { get; init; }

    public string FileName => Path.GetFileName(AssetPath);
    public string UsageText => Tilesets.Count == 1
        ? "1 tileset"
        : $"{Tilesets.Count} tileset";
    public string MapUsageText => Maps.Count == 1
        ? "1 mappa"
        : $"{Maps.Count} mappe";
    public string SizeText => SizeBytes is null ? "—" : FormatSize(SizeBytes.Value);
    public string HashText => string.IsNullOrWhiteSpace(Sha256) ? "—" : Sha256;
    public string StatusText => !Exists
        ? "Mancante"
        : IsOrphan ? "Orfano"
        : IsDuplicate ? "Duplicato" : "Referenziato";

    private static string FormatSize(long size) => size switch
    {
        < 1024 => $"{size} B",
        < 1024 * 1024 => $"{size / 1024d:0.0} KB",
        _ => $"{size / (1024d * 1024d):0.0} MB",
    };
}
