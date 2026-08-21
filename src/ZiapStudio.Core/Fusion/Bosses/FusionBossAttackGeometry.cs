namespace ZiapStudio.Core.Fusion.Bosses;

public enum FusionBossAttackGeometryKind
{
    InstantCircle,
    ProjectileCorridor,
}

public enum FusionBossAttackGeometryComparison
{
    ExactPrimaryCollider,
    Partial,
    Divergent,
}

public enum FusionBossAttackColliderKind
{
    Circle,
    Box,
}

public sealed record FusionBossAttackCollider
{
    public FusionBossAttackColliderKind Kind { get; init; }

    public double OffsetXTiles { get; init; }

    public double OffsetYTiles { get; init; }

    public double RadiusTiles { get; init; }

    public double WidthTiles { get; init; }

    public double HeightTiles { get; init; }
}

public sealed record FusionBossAttackGeometry
{
    public int SkillId { get; init; }

    public string SkillName { get; init; } = string.Empty;

    public FusionBossAttackGeometryKind Kind { get; init; }

    public double RadiusTiles { get; init; }

    public double RangeTiles { get; init; }

    public double Speed { get; init; }

    public int DirectionMode { get; init; }

    public double ProjectileColliderRadiusPixels { get; init; }

    public double TelegraphCenterOffsetYTiles { get; init; }

    public double RuntimeCenterOffsetYTiles { get; init; }

    public bool TelegraphEnabled { get; init; }

    public int BaseWarningFrames { get; init; }

    public int CastingFrames { get; init; }

    public int ActionStartDelayFrames { get; init; }

    public int TelegraphDurationFrames { get; init; }

    public int ExecutionDelayFrames { get; init; }

    public int HitRepeatCount { get; init; } = 1;

    public int RepeatOnUseCount { get; init; } = 1;

    public int RepeatDelayMilliseconds { get; init; }

    public IReadOnlyList<FusionBossAttackCollider> ExtraHurtboxes { get; init; } = [];

    public FusionBossAttackGeometryComparison Comparison { get; init; }

    public string ComparisonText { get; init; } = string.Empty;

    public string LimitationText { get; init; } = string.Empty;
}

public sealed record FusionBossAttackTarget
{
    public string CastMode { get; init; } = string.Empty;

    public string CasterRole { get; init; } = string.Empty;

    public string TargetType { get; init; } = string.Empty;

    public string TargetKey { get; init; } = string.Empty;

    public string TargetAnchor { get; init; } = string.Empty;

    public string TargetRole { get; init; } = string.Empty;

    public int? TargetIndex { get; init; }

    public double? TargetX { get; init; }

    public double? TargetY { get; init; }

    public string OriginType { get; init; } = string.Empty;

    public string OriginKey { get; init; } = string.Empty;

    public string OriginAnchor { get; init; } = string.Empty;

    public string OriginRole { get; init; } = string.Empty;

    public double? OriginX { get; init; }

    public double? OriginY { get; init; }

    public bool IsDynamic { get; init; }

    public bool IsOriginDynamic { get; init; }

    public string DisplayText { get; init; } = string.Empty;
}
