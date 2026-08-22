namespace ZiapStudio.Core.Fusion.Bosses;

public sealed record FusionBossEncounterDefinition
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string BossId { get; init; } = string.Empty;

    public string InitialPhaseId { get; init; } = string.Empty;

    public IReadOnlyList<FusionBossPhaseDefinition> Phases { get; init; } = [];
}

public sealed record FusionBossPhaseDefinition
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string PlayerGoal { get; init; } = string.Empty;

    public string DesignerIntent { get; init; } = string.Empty;

    public bool IsInitial { get; init; }

    public IReadOnlyList<string> Mechanics { get; init; } = [];

    public IReadOnlyList<FusionBossTimelineStep> OnEnterSteps { get; init; } = [];

    public IReadOnlyList<FusionBossTimelineStep> OnExitSteps { get; init; } = [];

    public IReadOnlyList<FusionBossTransitionDefinition> Transitions { get; init; } = [];

    public IReadOnlyList<FusionBossSequenceDefinition> Sequences { get; init; } = [];
}

public sealed record FusionBossTransitionDefinition
{
    public string TargetPhaseId { get; init; } = string.Empty;

    public string ConditionSummary { get; init; } = string.Empty;
}

public sealed record FusionBossSequenceDefinition
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string PlayerGoal { get; init; } = string.Empty;

    public string DesignerIntent { get; init; } = string.Empty;

    public string Scope { get; init; } = string.Empty;

    public FusionBossTimelineSourceKind SourceKind { get; init; } =
        FusionBossTimelineSourceKind.Sequence;

    public bool AutoStart { get; init; } = true;

    public IReadOnlyList<FusionBossTimelineStep> Steps { get; init; } = [];

    public int MinimumDurationFrames => Steps.Sum(step => step.DurationFrames ?? 0);

    public bool HasDynamicTiming => Steps.Any(step => step.Kind is
        FusionBossTimelineStepKind.WaitUntil or
        FusionBossTimelineStepKind.Sequence or
        FusionBossTimelineStepKind.RepeatSequence);
}

public enum FusionBossTimelineSourceKind
{
    Sequence,
    PhaseEnter,
    PhaseExit,
}

public enum FusionBossTimelineStepKind
{
    Action,
    Wait,
    WaitUntil,
    Sequence,
    RepeatSequence,
    Guard,
    Unknown,
}

public sealed record FusionBossTimelineStep
{
    public int Index { get; init; }

    public FusionBossTimelineStepKind Kind { get; init; }

    public string Label { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string TechnicalId { get; init; } = string.Empty;

    public string TechnicalDetail { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string IconGlyph { get; init; } = string.Empty;

    public IReadOnlyList<string> Reads { get; init; } = [];

    public IReadOnlyList<string> Writes { get; init; } = [];

    public int EarliestStartFrame { get; init; }

    public bool IsStartExact { get; init; } = true;

    public int? DurationFrames { get; init; }

    public int? TimeoutFrames { get; init; }

    public string? ReferencedSequenceId { get; init; }

    public string AttackLifecycleId { get; init; } = string.Empty;

    public string AttackLifecycleStage { get; init; } = string.Empty;

    public int? LinkedAttackStepIndex { get; init; }

    public FusionBossAttackGeometry? AttackGeometry { get; init; }

    public FusionBossAttackTarget? AttackTarget { get; init; }

    public IReadOnlyList<FusionBossAttackTarget> AttackTargets { get; init; } = [];
}
