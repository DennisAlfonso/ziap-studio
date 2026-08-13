namespace ZiapStudio.Core.Assets;

public sealed record AssetReference
{
    public RpgMakerAssetKind AssetKind { get; init; }

    public string RawValue { get; init; } = string.Empty;

    public string? ResolvedPath { get; init; }

    public string? RelativePath { get; init; }

    public int? Index { get; init; }

    public AssetResolutionStatus Status { get; init; }

    public string? Diagnostic { get; init; }
}

public enum RpgMakerAssetKind
{
    Icon,
    Face,
    Character,
    SideViewActor,
    EnemyBattler,
    Image,
}

public enum AssetResolutionStatus
{
    Empty,
    Resolved,
    Missing,
    Invalid,
}

public readonly record struct AssetPreviewKey(int EntryId, string FieldKey);

public sealed record AssetPreviewDescriptor
{
    public required string SourcePath { get; init; }

    public int SourceWidth { get; init; }

    public int SourceHeight { get; init; }

    public int CropX { get; init; }

    public int CropY { get; init; }

    public int CropWidth { get; init; }

    public int CropHeight { get; init; }
}

public sealed record AssetPreviewResult
{
    public required AssetReference Reference { get; init; }

    public AssetPreviewDescriptor? Preview { get; init; }

    public bool HasPreview => Preview is not null;
}
