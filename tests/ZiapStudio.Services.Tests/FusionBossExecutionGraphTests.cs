using ZiapStudio.Core.Fusion.Bosses;
using ZiapStudio.Services.Fusion.Bosses;

namespace ZiapStudio.Services.Tests;

public sealed class FusionBossExecutionGraphTests
{
    [Fact]
    public void Build_ExplainsAutomaticRootsNestedCallsAndObservedFailure()
    {
        var phase = new FusionBossPhaseDefinition
        {
            Id = "combat",
            Transitions =
            [
                new FusionBossTransitionDefinition
                {
                    TargetPhaseId = "failure",
                    ConditionSummary = "any(sequenceFailed(rootFlow), sequenceFailed(otherFlow))",
                },
            ],
            Sequences =
            [
                Sequence(
                    "rootFlow",
                    autoStart: true,
                    new FusionBossTimelineStep
                    {
                        Index = 0,
                        Kind = FusionBossTimelineStepKind.RepeatSequence,
                        ReferencedSequenceId = "childCycle",
                        TechnicalDetail = "childCycle · max 8× · finché bossDefeated()",
                    }),
                Sequence(
                    "otherFlow",
                    autoStart: true,
                    new FusionBossTimelineStep
                    {
                        Index = 0,
                        Kind = FusionBossTimelineStepKind.Sequence,
                        ReferencedSequenceId = "missingSequence",
                        TechnicalDetail = "missingSequence · await",
                    }),
                Sequence("childCycle", autoStart: false),
                Sequence("manualOrphan", autoStart: false),
            ],
        };

        var graph = new FusionBossExecutionGraphService().Build(phase);

        Assert.Contains("2 automatici in parallelo", graph.Summary);
        Assert.Contains(graph.Edges, edge =>
            edge.Kind == FusionBossExecutionEdgeKind.PhaseTrigger &&
            edge.TargetNodeId == "sequence:rootFlow");
        var child = Assert.Single(graph.Nodes, node => node.TechnicalId == "childCycle");
        Assert.False(child.IsOrphan);
        Assert.Contains("rootFlow", child.CallerSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("step 1", child.StartReason, StringComparison.OrdinalIgnoreCase);

        var root = Assert.Single(graph.Nodes, node => node.TechnicalId == "rootFlow");
        Assert.Contains("fallimento → failure", root.OutcomeSummary);
    }

    [Fact]
    public void Build_FlagsUnreferencedManualFlowsAndMissingTargets()
    {
        var phase = new FusionBossPhaseDefinition
        {
            Id = "combat",
            Sequences =
            [
                Sequence(
                    "rootFlow",
                    autoStart: true,
                    new FusionBossTimelineStep
                    {
                        Index = 3,
                        Kind = FusionBossTimelineStepKind.Sequence,
                        ReferencedSequenceId = "missingSequence",
                    }),
                Sequence("manualOrphan", autoStart: false),
            ],
        };

        var graph = new FusionBossExecutionGraphService().Build(phase);

        var orphan = Assert.Single(graph.Nodes, node => node.TechnicalId == "manualOrphan");
        Assert.True(orphan.IsOrphan);
        Assert.True(orphan.IsWarning);
        Assert.Contains("API o runtime esterno", orphan.CallerSummary);
        var missing = Assert.Single(graph.Nodes, node =>
            node.Kind == FusionBossExecutionNodeKind.MissingReference);
        Assert.Equal("missingSequence", missing.TechnicalId);
        Assert.True(missing.IsWarning);
        Assert.Contains("senza richiamo dichiarativo", graph.WarningSummary);
        Assert.Contains("sequenza inesistente", graph.WarningSummary);
    }

    private static FusionBossSequenceDefinition Sequence(
        string id,
        bool autoStart,
        params FusionBossTimelineStep[] steps) => new()
    {
        Id = id,
        DisplayName = id,
        Scope = "phase",
        AutoStart = autoStart,
        Steps = steps,
    };
}
