using System.Text.Json;
using ZiapStudio.Core.Fusion.Bosses;
using ZiapStudio.Services.Fusion.Bosses;

namespace ZiapStudio.Services.Tests;

public sealed class FusionRuntimeTraceTests
{
    [Fact]
    public async Task LoadAsync_LoadsSupportedTraceAndReportsInvalidSchema()
    {
        using var workspace = new TestWorkspace();
        var validTrace = new FusionRuntimeTrace
        {
            Schema = "ziap.fusion-runtime-trace/v1",
            SchemaVersion = 1,
            TraceId = "trace-valid",
            EncounterId = "organizationChief",
            MapId = 74,
            CaptureProfile = "balanced",
            SampleEveryFrames = 3,
            ActorChangeOnly = true,
            ActorHeartbeatFrames = 30,
            EventCount = 1,
            Events =
            [
                new FusionRuntimeTraceEvent
                {
                    Index = 1,
                    Type = "trace.started",
                },
            ],
        };
        workspace.WriteFile(
            ".ziap/runtime-traces/valid.json",
            JsonSerializer.Serialize(validTrace, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        workspace.WriteFile(
            ".ziap/runtime-traces/invalid.json",
            """{"schema":"unsupported","schemaVersion":99,"events":[]}""");

        var result = await new FusionRuntimeTraceService().LoadAsync(workspace.RootPath);

        var trace = Assert.Single(result.Traces);
        Assert.Equal("trace-valid", trace.TraceId);
        Assert.Equal(74, trace.MapId);
        Assert.Equal("balanced", trace.CaptureProfile);
        Assert.Equal(3, trace.SampleEveryFrames);
        Assert.True(trace.ActorChangeOnly);
        Assert.EndsWith("valid.json", trace.SourcePath, StringComparison.OrdinalIgnoreCase);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void AnalyzeSequence_UsesLatestRunAndOffsetsRepeatedCasts()
    {
        var sequence = new FusionBossSequenceDefinition
        {
            Id = "shieldedOpening",
            Steps =
            [
                new FusionBossTimelineStep
                {
                    Index = 3,
                    Kind = FusionBossTimelineStepKind.Action,
                    TechnicalId = "alphaAbsSkillBurst",
                    EarliestStartFrame = 20,
                    AttackGeometry = new FusionBossAttackGeometry
                    {
                        SkillId = 104,
                        RadiusTiles = 2,
                        ExecutionDelayFrames = 10,
                        RepeatOnUseCount = 2,
                        RepeatDelayMilliseconds = 1000,
                    },
                },
            ],
        };
        var events = new List<FusionRuntimeTraceEvent>
        {
            Event(1, "sequence.started", 100, "old"),
            Event(2, "cast.requested", 120, "old", 3, 104, 0, "old-cast"),
            Event(3, "cast.executed", 160, "old", 3, 104, 0, "old-cast"),
            Event(4, "sequence.started", 500, "latest"),
            Event(5, "cast.requested", 520, "latest", 3, 104, 0, "cast-0"),
            Event(6, "telegraph.started", 520, "latest", 3, 104, 0, "cast-0") with
            {
                Center = new FusionRuntimeTracePoint { X = 10, Y = 12 },
            },
            Event(7, "cast.executed", 530, "latest", 3, 104, 0, "cast-0"),
            Event(8, "collider.activated", 530, "latest", 3, 104, 0, "cast-0") with
            {
                Center = new FusionRuntimeTracePoint { X = 10, Y = 12 },
                Geometry = new FusionRuntimeTraceGeometry { RadiusTiles = 2 },
            },
            Event(9, "cast.requested", 580, "latest", 3, 104, 1, "cast-1"),
            Event(10, "telegraph.started", 580, "latest", 3, 104, 1, "cast-1") with
            {
                Center = new FusionRuntimeTracePoint { X = 11, Y = 12 },
            },
            Event(11, "cast.executed", 590, "latest", 3, 104, 1, "cast-1"),
            Event(12, "collider.activated", 590, "latest", 3, 104, 1, "cast-1") with
            {
                Center = new FusionRuntimeTracePoint { X = 11, Y = 12 },
                Geometry = new FusionRuntimeTraceGeometry { RadiusTiles = 2 },
            },
        };
        var trace = new FusionRuntimeTrace
        {
            Schema = "ziap.fusion-runtime-trace/v1",
            SchemaVersion = 1,
            Fps = 60,
            Events = events,
        };

        var analysis = new FusionRuntimeTraceService().AnalyzeSequence(
            sequence,
            "shielded",
            trace);

        Assert.Equal("latest", analysis.SequenceRunId);
        Assert.Equal(9, analysis.Events.Count);
        Assert.Collection(
            analysis.Attacks,
            first =>
            {
                Assert.Equal(30, first.ExpectedExecutionFrame);
                Assert.Equal(30, first.RuntimeExecutionFrame);
                Assert.Equal(FusionRuntimeFidelityStatus.Aligned, first.Status);
            },
            second =>
            {
                Assert.Equal(90, second.ExpectedExecutionFrame);
                Assert.Equal(90, second.RuntimeExecutionFrame);
                Assert.Equal(FusionRuntimeFidelityStatus.Aligned, second.Status);
            });
    }

    [Fact]
    public void AnalyzeSequence_TreatsCastVolleyTargetsAsParallelCasts()
    {
        var sequence = new FusionBossSequenceDefinition
        {
            Id = "shieldedOpening",
            Steps =
            [
                new FusionBossTimelineStep
                {
                    Index = 0,
                    Kind = FusionBossTimelineStepKind.Action,
                    TechnicalId = "combat.castVolley",
                    EarliestStartFrame = 0,
                    AttackGeometry = new FusionBossAttackGeometry
                    {
                        SkillId = 98,
                        RadiusTiles = 3,
                        ExecutionDelayFrames = 60,
                        RepeatDelayMilliseconds = 120,
                    },
                },
            ],
        };
        var events = new List<FusionRuntimeTraceEvent>
        {
            Event(1, "sequence.started", 300, "dark-ray-run"),
            Event(2, "cast.requested", 300, "dark-ray-run", 0, 98, 0, "cast-0"),
            Event(3, "cast.executed", 360, "dark-ray-run", 0, 98, 0, "cast-0"),
            Event(4, "collider.activated", 360, "dark-ray-run", 0, 98, 0, "cast-0") with
            {
                Center = new FusionRuntimeTracePoint { X = 8, Y = 27 },
                Geometry = new FusionRuntimeTraceGeometry { RadiusTiles = 3 },
            },
            Event(5, "cast.requested", 300, "dark-ray-run", 0, 98, 1, "cast-1"),
            Event(6, "cast.executed", 360, "dark-ray-run", 0, 98, 1, "cast-1"),
            Event(7, "collider.activated", 360, "dark-ray-run", 0, 98, 1, "cast-1") with
            {
                Center = new FusionRuntimeTracePoint { X = 15, Y = 27 },
                Geometry = new FusionRuntimeTraceGeometry { RadiusTiles = 3 },
            },
            Event(8, "cast.requested", 300, "dark-ray-run", 0, 98, 2, "cast-2"),
            Event(9, "cast.executed", 360, "dark-ray-run", 0, 98, 2, "cast-2"),
            Event(10, "collider.activated", 360, "dark-ray-run", 0, 98, 2, "cast-2") with
            {
                Center = new FusionRuntimeTracePoint { X = 22, Y = 27 },
                Geometry = new FusionRuntimeTraceGeometry { RadiusTiles = 3 },
            },
        };
        var trace = new FusionRuntimeTrace
        {
            Schema = "ziap.fusion-runtime-trace/v1",
            SchemaVersion = 1,
            Fps = 60,
            Events = events,
        };

        var analysis = new FusionRuntimeTraceService().AnalyzeSequence(
            sequence,
            "shielded",
            trace);

        Assert.Equal(3, analysis.Attacks.Count);
        Assert.All(analysis.Attacks, comparison =>
        {
            Assert.Equal(60, comparison.ExpectedExecutionFrame);
            Assert.Equal(60, comparison.RuntimeExecutionFrame);
            Assert.Equal(FusionRuntimeFidelityStatus.Aligned, comparison.Status);
        });
    }

    [Fact]
    public void AnalyzeSequence_ValidatesDirectionalChainCountAndPath()
    {
        var sequence = new FusionBossSequenceDefinition
        {
            Id = "shieldedOpening",
            Steps =
            [
                new FusionBossTimelineStep
                {
                    Index = 0,
                    Kind = FusionBossTimelineStepKind.Action,
                    TechnicalId = "combat.castVolley",
                    EarliestStartFrame = 0,
                    AttackGeometry = new FusionBossAttackGeometry
                    {
                        SkillId = 98,
                        Kind = FusionBossAttackGeometryKind.DirectionalInstantChain,
                        RadiusTiles = 3,
                        RuntimeCenterOffsetYTiles = -0.5,
                        ChainCount = 13,
                        ChainSpacingTiles = 3,
                        ExecutionDelayFrames = 60,
                    },
                },
            ],
        };
        var events = new List<FusionRuntimeTraceEvent>
        {
            Event(1, "sequence.started", 300, "dark-ray-chain"),
            Event(2, "cast.requested", 300, "dark-ray-chain", 0, 98, 0, "cast-chain") with
            {
                Point = new FusionRuntimeTracePoint { X = 8, Y = 27 },
                AttackDirection = 2,
            },
            Event(3, "telegraph.started", 300, "dark-ray-chain", 0, 98, 0, "cast-chain") with
            {
                Center = new FusionRuntimeTracePoint { X = 8, Y = 29.5 },
            },
            Event(4, "cast.executed", 360, "dark-ray-chain", 0, 98, 0, "cast-chain"),
        };
        for (var index = 0; index < 13; index++)
        {
            events.Add(Event(
                5 + index,
                "collider.activated",
                360,
                "dark-ray-chain",
                0,
                98,
                0,
                "cast-chain") with
            {
                ColliderIndex = index,
                Center = new FusionRuntimeTracePoint { X = 8, Y = 29.5 + index * 3 },
                Geometry = new FusionRuntimeTraceGeometry
                {
                    Kind = "directionalInstantChain",
                    RadiusTiles = 3,
                    Direction = 2,
                    ChainCount = 13,
                },
            });
        }
        var trace = new FusionRuntimeTrace
        {
            Schema = "ziap.fusion-runtime-trace/v1",
            SchemaVersion = 1,
            Events = events,
        };

        var comparison = Assert.Single(
            new FusionRuntimeTraceService().AnalyzeSequence(
                sequence,
                "shielded",
                trace).Attacks);

        Assert.Equal(FusionRuntimeFidelityStatus.Aligned, comparison.Status);
        Assert.Equal(13, comparison.ExpectedColliderCount);
        Assert.Equal(13, comparison.RuntimeColliderCount);
        Assert.Equal(0, comparison.ColliderCountDelta);
        Assert.Equal(0, comparison.ColliderPathOffsetTiles);

        var collapsedTrace = trace with
        {
            Events = events.Select(entry => entry.Type == "collider.activated"
                ? entry with
                {
                    Center = new FusionRuntimeTracePoint { X = 8, Y = 29.5 },
                }
                : entry).ToArray(),
        };
        var collapsedComparison = Assert.Single(
            new FusionRuntimeTraceService().AnalyzeSequence(
                sequence,
                "shielded",
                collapsedTrace).Attacks);

        Assert.Equal(FusionRuntimeFidelityStatus.Divergent, collapsedComparison.Status);
        Assert.Equal(36, collapsedComparison.ColliderPathOffsetTiles);
    }

    [Fact]
    public void AnalyzeSequence_ValidatesPreparedImpactAgainstLandingAndCommit()
    {
        var sequence = new FusionBossSequenceDefinition
        {
            Id = "shieldedOpening",
            Steps =
            [
                new FusionBossTimelineStep
                {
                    Index = 1,
                    Kind = FusionBossTimelineStepKind.Action,
                    TechnicalId = "combat.prepareAttack",
                    EarliestStartFrame = 0,
                    AttackLifecycleId = "chiefImpact",
                    AttackLifecycleStage = "prepare",
                    LinkedAttackStepIndex = 5,
                    AttackGeometry = new FusionBossAttackGeometry
                    {
                        SkillId = 95,
                        RadiusTiles = 1,
                        ExecutionDelayFrames = 60,
                    },
                },
                new FusionBossTimelineStep
                {
                    Index = 3,
                    Kind = FusionBossTimelineStepKind.Action,
                    TechnicalId = "combat.moveTo",
                    EarliestStartFrame = 60,
                },
                new FusionBossTimelineStep
                {
                    Index = 5,
                    Kind = FusionBossTimelineStepKind.Action,
                    TechnicalId = "combat.commitAttack",
                    EarliestStartFrame = 60,
                    IsStartExact = false,
                    AttackLifecycleId = "chiefImpact",
                    AttackLifecycleStage = "commit",
                    LinkedAttackStepIndex = 1,
                },
            ],
        };
        var trace = new FusionRuntimeTrace
        {
            Schema = "ziap.fusion-runtime-trace/v1",
            SchemaVersion = 1,
            Events =
            [
                Event(1, "sequence.started", 100, "impact-run"),
                Event(2, "cast.requested", 100, "impact-run", 1, 95, 0, "impact-cast") with
                {
                    PreparedAttackId = "chiefImpact",
                    Role = "boss",
                    Point = new FusionRuntimeTracePoint { X = 17, Y = 40 },
                },
                Event(3, "telegraph.started", 100, "impact-run", 1, 95, 0, "impact-cast") with
                {
                    PreparedAttackId = "chiefImpact",
                    Center = new FusionRuntimeTracePoint { X = 17, Y = 39.5 },
                },
                Event(4, "movement.started", 160, "impact-run", 3) with
                {
                    Role = "boss",
                    Point = new FusionRuntimeTracePoint { X = 17, Y = 40 },
                },
                Event(5, "movement.completed", 188, "impact-run", 4) with
                {
                    Role = "boss",
                    Point = new FusionRuntimeTracePoint { X = 17, Y = 40 },
                },
                Event(6, "attack.commit.requested", 188, "impact-run", 5) with
                {
                    PreparedAttackId = "chiefImpact",
                    Role = "boss",
                },
                Event(7, "collider.activated", 188, "impact-run", 1, 95, 0, "impact-cast") with
                {
                    PreparedAttackId = "chiefImpact",
                    Center = new FusionRuntimeTracePoint { X = 17, Y = 39.5 },
                    Geometry = new FusionRuntimeTraceGeometry { RadiusTiles = 1 },
                },
                Event(8, "cast.executed", 188, "impact-run", 1, 95, 0, "impact-cast") with
                {
                    PreparedAttackId = "chiefImpact",
                },
            ],
        };

        var comparison = Assert.Single(
            new FusionRuntimeTraceService().AnalyzeSequence(
                sequence,
                "shielded",
                trace).Attacks);

        Assert.Equal(FusionRuntimeFidelityStatus.Aligned, comparison.Status);
        Assert.Equal("chiefImpact", comparison.AttackLifecycleId);
        Assert.False(comparison.IsExpectedExecutionExact);
        Assert.Equal(60, comparison.ExpectedExecutionFrame);
        Assert.Equal(88, comparison.RuntimeMovementCompletedFrame);
        Assert.Equal(88, comparison.RuntimeCommitFrame);
        Assert.Equal(0, comparison.CommitExecutionDeltaFrames);
        Assert.Equal(0, comparison.LandingImpactDeltaFrames);
        Assert.Null(comparison.ExecutionDeltaFrames);
    }

    [Fact]
    public void AnalyzeSequence_AllowsPreparedAttackWithoutMovement()
    {
        var sequence = new FusionBossSequenceDefinition
        {
            Id = "shieldedOpening",
            Steps =
            [
                new FusionBossTimelineStep
                {
                    Index = 0,
                    Kind = FusionBossTimelineStepKind.Action,
                    TechnicalId = "combat.prepareAttack",
                    EarliestStartFrame = 0,
                    AttackLifecycleId = "stationaryImpact",
                    AttackLifecycleStage = "prepare",
                    LinkedAttackStepIndex = 2,
                    AttackGeometry = new FusionBossAttackGeometry
                    {
                        SkillId = 95,
                        RadiusTiles = 1,
                    },
                },
                new FusionBossTimelineStep
                {
                    Index = 1,
                    Kind = FusionBossTimelineStepKind.Wait,
                    TechnicalId = "wait",
                    EarliestStartFrame = 0,
                    DurationFrames = 30,
                },
                new FusionBossTimelineStep
                {
                    Index = 2,
                    Kind = FusionBossTimelineStepKind.Action,
                    TechnicalId = "combat.commitAttack",
                    EarliestStartFrame = 30,
                    AttackLifecycleId = "stationaryImpact",
                    AttackLifecycleStage = "commit",
                    LinkedAttackStepIndex = 0,
                },
            ],
        };
        var trace = new FusionRuntimeTrace
        {
            Schema = "ziap.fusion-runtime-trace/v1",
            SchemaVersion = 1,
            Events =
            [
                Event(1, "sequence.started", 200, "stationary-run"),
                Event(2, "cast.requested", 200, "stationary-run", 0, 95, 0, "stationary-cast") with
                {
                    PreparedAttackId = "stationaryImpact",
                    Role = "boss",
                },
                Event(3, "attack.commit.requested", 230, "stationary-run", 2) with
                {
                    PreparedAttackId = "stationaryImpact",
                    Role = "boss",
                },
                Event(4, "collider.activated", 230, "stationary-run", 0, 95, 0, "stationary-cast") with
                {
                    PreparedAttackId = "stationaryImpact",
                    Geometry = new FusionRuntimeTraceGeometry { RadiusTiles = 1 },
                },
                Event(5, "cast.executed", 230, "stationary-run", 0, 95, 0, "stationary-cast") with
                {
                    PreparedAttackId = "stationaryImpact",
                },
            ],
        };

        var comparison = Assert.Single(
            new FusionRuntimeTraceService().AnalyzeSequence(
                sequence,
                "shielded",
                trace).Attacks);

        Assert.Equal(FusionRuntimeFidelityStatus.Aligned, comparison.Status);
        Assert.Equal(30, comparison.RuntimeCommitFrame);
        Assert.Equal(0, comparison.CommitExecutionDeltaFrames);
        Assert.Null(comparison.RuntimeMovementCompletedFrame);
        Assert.Null(comparison.LandingImpactDeltaFrames);
    }

    [Fact]
    public void AnalyzeSequence_ProjectileComparesColliderSizeNotTravelDistance()
    {
        var sequence = new FusionBossSequenceDefinition
        {
            Id = "shieldedOpening",
            Steps =
            [
                new FusionBossTimelineStep
                {
                    Index = 4,
                    Kind = FusionBossTimelineStepKind.Action,
                    EarliestStartFrame = 20,
                    AttackGeometry = new FusionBossAttackGeometry
                    {
                        SkillId = 90,
                        Kind = FusionBossAttackGeometryKind.ProjectileCorridor,
                        ExecutionDelayFrames = 10,
                        ProjectileColliderRadiusPixels = 19,
                    },
                },
            ],
        };
        var trace = new FusionRuntimeTrace
        {
            Schema = "ziap.fusion-runtime-trace/v1",
            SchemaVersion = 1,
            Events =
            [
                Event(1, "sequence.started", 100, "projectile-run"),
                Event(2, "cast.requested", 120, "projectile-run", 4, 90, 0, "projectile-cast"),
                Event(3, "telegraph.started", 120, "projectile-run", 4, 90, 0, "projectile-cast") with
                {
                    Center = new FusionRuntimeTracePoint { X = 2, Y = 3 },
                },
                Event(4, "cast.executed", 130, "projectile-run", 4, 90, 0, "projectile-cast"),
                Event(5, "collider.activated", 180, "projectile-run", 4, 90, 0, "projectile-cast") with
                {
                    Center = new FusionRuntimeTracePoint { X = 18, Y = 21 },
                    Geometry = new FusionRuntimeTraceGeometry { ColliderRadiusPixels = 19 },
                },
            ],
        };

        var comparison = Assert.Single(
            new FusionRuntimeTraceService().AnalyzeSequence(
                sequence,
                "shielded",
                trace).Attacks);

        Assert.Equal(FusionRuntimeFidelityStatus.Aligned, comparison.Status);
        Assert.Null(comparison.TelegraphColliderOffsetTiles);
        Assert.Null(comparison.ExpectedRadiusTiles);
        Assert.Equal(19, comparison.RuntimeColliderRadiusPixels);
        Assert.Equal(0, comparison.ColliderRadiusDeltaPixels);
    }

    private static FusionRuntimeTraceEvent Event(
        int index,
        string type,
        double frame,
        string runId,
        int? stepIndex = null,
        int? skillId = null,
        int? castIndex = null,
        string castId = "") => new()
        {
            Index = index,
            Type = type,
            Frame = frame,
            Phase = "shielded",
            SequenceId = "shieldedOpening",
            SequenceRunId = runId,
            StepIndex = stepIndex,
            SkillId = skillId,
            CastIndex = castIndex,
            CastId = castId,
        };
}
