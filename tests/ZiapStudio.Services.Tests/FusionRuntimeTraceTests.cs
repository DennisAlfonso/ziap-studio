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
    public void AnalyzeSequence_UsesLatestRunAndOffsetsVolleyCasts()
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
