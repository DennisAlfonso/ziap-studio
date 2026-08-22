namespace ZiapStudio.Core.Fusion.Bosses;

public sealed record FusionRuntimeTrace
{
    public string Schema { get; init; } = string.Empty;

    public int SchemaVersion { get; init; }

    public string TraceId { get; init; } = string.Empty;

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    public string StopReason { get; init; } = string.Empty;

    public string ProjectId { get; init; } = string.Empty;

    public string EncounterId { get; init; } = string.Empty;

    public int MapId { get; init; }

    public double Fps { get; init; } = 60;

    public string CaptureProfile { get; init; } = string.Empty;

    public int SampleEveryFrames { get; init; } = 1;

    public bool ActorChangeOnly { get; init; }

    public int ActorHeartbeatFrames { get; init; }

    public bool PrettyPrinted { get; init; }

    public int EventCount { get; init; }

    public int DroppedEvents { get; init; }

    public string SourcePath { get; init; } = string.Empty;

    public IReadOnlyList<FusionRuntimeTraceEvent> Events { get; init; } = [];
}

public sealed record FusionRuntimeTraceEvent
{
    public int Index { get; init; }

    public string Type { get; init; } = string.Empty;

    public double Frame { get; init; }

    public double PhaseFrame { get; init; }

    public double GlobalFrame { get; init; }

    public string EncounterId { get; init; } = string.Empty;

    public string Phase { get; init; } = string.Empty;

    public string SequenceId { get; init; } = string.Empty;

    public string SequenceScope { get; init; } = string.Empty;

    public string SequenceRunId { get; init; } = string.Empty;

    public int? StepIndex { get; init; }

    public int? SkillId { get; init; }

    public int? CastIndex { get; init; }

    public string CastId { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public string ProjectileId { get; init; } = string.Empty;

    public string PreparedAttackId { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public int? DurationFrames { get; init; }

    public int? TelegraphDelayFrames { get; init; }

    public bool HoldUntilCommit { get; init; }

    public int? AttackDirection { get; init; }

    public int? ChainCount { get; init; }

    public int? ColliderIndex { get; init; }

    public int? TargetCount { get; init; }

    public double? RadiusTiles { get; init; }

    public double? CorridorWidthPixels { get; init; }

    public double? ColliderRadiusPixels { get; init; }

    public double? ChainSpacingTiles { get; init; }

    public double? ChainReachTiles { get; init; }

    public FusionRuntimeTracePoint? Point { get; init; }

    public FusionRuntimeTracePoint? Center { get; init; }

    public FusionRuntimeTracePoint? Origin { get; init; }

    public FusionRuntimeTracePoint? End { get; init; }

    public FusionRuntimeTracePoint? Player { get; init; }

    public FusionRuntimeTracePoint? Boss { get; init; }

    public FusionRuntimeTracePoint? Owner { get; init; }

    public FusionRuntimeTracePoint? Target { get; init; }

    public FusionRuntimeTraceGeometry? Geometry { get; init; }

    public IReadOnlyList<FusionRuntimeTracePoint> Centers { get; init; } = [];

    public IReadOnlyList<FusionRuntimeTracePoint> Targets { get; init; } = [];
}

public sealed record FusionRuntimeTracePoint
{
    public string Kind { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public int? Direction { get; init; }

    public bool? Jumping { get; init; }
}

public sealed record FusionRuntimeTraceGeometry
{
    public string Kind { get; init; } = string.Empty;

    public double RadiusTiles { get; init; }

    public double RangeTiles { get; init; }

    public double SpeedPixelsPerFrame { get; init; }

    public double ColliderRadiusPixels { get; init; }

    public int DirectionMode { get; init; }

    public int Direction { get; init; }

    public int ChainCount { get; init; }

    public double ChainSpacingTiles { get; init; }

    public double ChainReachTiles { get; init; }
}

public enum FusionRuntimeFidelityStatus
{
    Aligned,
    Drift,
    Divergent,
    Missing,
}

public sealed record FusionRuntimeAttackComparison
{
    public string SequenceId { get; init; } = string.Empty;

    public string SequenceScope { get; init; } = string.Empty;

    public string SequenceRunId { get; init; } = string.Empty;

    public int StepIndex { get; init; }

    public int SkillId { get; init; }

    public int CastIndex { get; init; }

    public string CastId { get; init; } = string.Empty;

    public double ExpectedStartFrame { get; init; }

    public double ExpectedExecutionFrame { get; init; }

    public double? RuntimeStartFrame { get; init; }

    public double? RuntimeTelegraphFrame { get; init; }

    public double? RuntimeExecutionFrame { get; init; }

    public double? RuntimeColliderFrame { get; init; }

    public double? ExecutionDeltaFrames { get; init; }

    public double? TelegraphColliderOffsetTiles { get; init; }

    public double? ExpectedRadiusTiles { get; init; }

    public double? RuntimeRadiusTiles { get; init; }

    public double? RadiusDeltaTiles { get; init; }

    public double? ExpectedColliderRadiusPixels { get; init; }

    public double? RuntimeColliderRadiusPixels { get; init; }

    public double? ColliderRadiusDeltaPixels { get; init; }

    public int? ExpectedColliderCount { get; init; }

    public int? RuntimeColliderCount { get; init; }

    public int? ColliderCountDelta { get; init; }

    public double? ColliderPathOffsetTiles { get; init; }

    public string AttackLifecycleId { get; init; } = string.Empty;

    public bool IsExpectedExecutionExact { get; init; } = true;

    public double? RuntimeCommitFrame { get; init; }

    public double? RuntimeMovementStartFrame { get; init; }

    public double? RuntimeMovementCompletedFrame { get; init; }

    public double? CommitExecutionDeltaFrames { get; init; }

    public double? LandingImpactDeltaFrames { get; init; }

    public FusionRuntimeFidelityStatus Status { get; init; }

    public string Summary { get; init; } = string.Empty;
}

public sealed record FusionRuntimeTraceEventProjection
{
    public FusionRuntimeTraceEvent Event { get; init; } = new();

    public double SequenceFrame { get; init; }
}

public sealed record FusionRuntimeSequenceAnalysis
{
    public string SequenceId { get; init; } = string.Empty;

    public string SequenceScope { get; init; } = string.Empty;

    public string SequenceRunId { get; init; } = string.Empty;

    public double RuntimeStartFrame { get; init; }

    public IReadOnlyList<FusionRuntimeTraceEventProjection> Events { get; init; } = [];

    public IReadOnlyList<FusionRuntimeAttackComparison> Attacks { get; init; } = [];
}

public sealed record FusionRuntimeTraceLoadResult
{
    public IReadOnlyList<FusionRuntimeTrace> Traces { get; init; } = [];

    public IReadOnlyList<string> Errors { get; init; } = [];
}
