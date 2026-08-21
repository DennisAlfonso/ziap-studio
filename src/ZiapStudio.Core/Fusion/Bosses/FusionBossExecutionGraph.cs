namespace ZiapStudio.Core.Fusion.Bosses;

public sealed record FusionBossExecutionGraph
{
    public string PhaseId { get; init; } = string.Empty;

    public IReadOnlyList<FusionBossExecutionNode> Nodes { get; init; } = [];

    public IReadOnlyList<FusionBossExecutionEdge> Edges { get; init; } = [];

    public string Summary { get; init; } = string.Empty;

    public string WarningSummary { get; init; } = string.Empty;
}

public sealed record FusionBossExecutionNode
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string TechnicalId { get; init; } = string.Empty;

    public FusionBossExecutionNodeKind Kind { get; init; }

    public string? TimelineSourceId { get; init; }

    public FusionBossTimelineSourceKind TimelineSourceKind { get; init; } =
        FusionBossTimelineSourceKind.Sequence;

    public string BadgeText { get; init; } = string.Empty;

    public string StartReason { get; init; } = string.Empty;

    public string CallerSummary { get; init; } = string.Empty;

    public string OutcomeSummary { get; init; } = string.Empty;

    public string DurationSummary { get; init; } = string.Empty;

    public int StepCount { get; init; }

    public bool IsWarning { get; init; }

    public bool IsOrphan { get; init; }

    public bool CanOpenTimeline => !string.IsNullOrWhiteSpace(TimelineSourceId);

    public string StepCountText => StepCount == 1 ? "1 step" : $"{StepCount} step";
}

public enum FusionBossExecutionNodeKind
{
    PhaseEntry,
    PhaseExit,
    PhaseHook,
    AutomaticSequence,
    ManualSequence,
    MissingReference,
}

public sealed record FusionBossExecutionEdge
{
    public string SourceNodeId { get; init; } = string.Empty;

    public string TargetNodeId { get; init; } = string.Empty;

    public FusionBossExecutionEdgeKind Kind { get; init; }

    public string Label { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public int? SourceStepIndex { get; init; }
}

public enum FusionBossExecutionEdgeKind
{
    PhaseTrigger,
    Call,
    Repeat,
}
