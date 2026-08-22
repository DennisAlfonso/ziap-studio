using System.Text.Json;
using ZiapStudio.Core.Fusion.Bosses;

namespace ZiapStudio.Services.Fusion.Bosses;

public sealed class FusionRuntimeTraceService
{
    private const long MaximumTraceBytes = 64L * 1024 * 1024;
    private const int MaximumTraceFiles = 50;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<FusionRuntimeTraceLoadResult> LoadAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(projectPath, ".ziap", "runtime-traces");
        if (!Directory.Exists(directory))
        {
            return new FusionRuntimeTraceLoadResult();
        }
        var traces = new List<FusionRuntimeTrace>();
        var errors = new List<string>();
        var files = new DirectoryInfo(directory)
            .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaximumTraceFiles)
            .ToArray();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.Length > MaximumTraceBytes)
            {
                errors.Add($"{file.Name}: supera il limite di 64 MB.");
                continue;
            }
            try
            {
                await using var stream = new FileStream(
                    file.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 65536,
                    useAsync: true);
                var trace = await JsonSerializer.DeserializeAsync<FusionRuntimeTrace>(
                    stream,
                    JsonOptions,
                    cancellationToken);
                if (trace is null ||
                    trace.SchemaVersion != 1 ||
                    !trace.Schema.Equals(
                        "ziap.fusion-runtime-trace/v1",
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{file.Name}: schema runtime trace non supportato.");
                    continue;
                }
                traces.Add(trace with
                {
                    SourcePath = file.FullName,
                    Events = trace.Events.OrderBy(entry => entry.Index).ToArray(),
                });
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or JsonException)
            {
                errors.Add($"{file.Name}: {exception.Message}");
            }
        }
        return new FusionRuntimeTraceLoadResult
        {
            Traces = traces,
            Errors = errors,
        };
    }

    public FusionRuntimeSequenceAnalysis AnalyzeSequence(
        FusionBossSequenceDefinition sequence,
        string phaseId,
        FusionRuntimeTrace trace)
    {
        var runStart = trace.Events.LastOrDefault(entry =>
            entry.Type.Equals("sequence.started", StringComparison.OrdinalIgnoreCase) &&
            entry.SequenceId.Equals(sequence.Id, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(phaseId) ||
                entry.Phase.Equals(phaseId, StringComparison.OrdinalIgnoreCase)));
        if (runStart is null)
        {
            return new FusionRuntimeSequenceAnalysis
            {
                SequenceId = sequence.Id,
                Attacks = MissingComparisons(sequence),
            };
        }
        var runEvents = SelectRunEvents(trace.Events, runStart).ToArray();
        var projections = runEvents
            .Select(entry => new FusionRuntimeTraceEventProjection
            {
                Event = entry,
                SequenceFrame = Math.Max(0, entry.Frame - runStart.Frame),
            })
            .ToArray();
        return new FusionRuntimeSequenceAnalysis
        {
            SequenceId = sequence.Id,
            SequenceScope = runStart.SequenceScope,
            SequenceRunId = runStart.SequenceRunId,
            RuntimeStartFrame = runStart.Frame,
            Events = projections,
            Attacks = CompareAttacks(sequence, runStart, runEvents, trace.Fps),
        };
    }

    private static IEnumerable<FusionRuntimeTraceEvent> SelectRunEvents(
        IReadOnlyList<FusionRuntimeTraceEvent> events,
        FusionRuntimeTraceEvent runStart)
    {
        if (!string.IsNullOrWhiteSpace(runStart.SequenceRunId))
        {
            return events.Where(entry => entry.SequenceRunId.Equals(
                runStart.SequenceRunId,
                StringComparison.OrdinalIgnoreCase));
        }
        var nextStart = events.FirstOrDefault(entry =>
            entry.Index > runStart.Index &&
            entry.Type.Equals("sequence.started", StringComparison.OrdinalIgnoreCase) &&
            entry.SequenceId.Equals(runStart.SequenceId, StringComparison.OrdinalIgnoreCase));
        return events.Where(entry =>
            entry.Index >= runStart.Index &&
            (nextStart is null || entry.Index < nextStart.Index) &&
            entry.SequenceId.Equals(runStart.SequenceId, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<FusionRuntimeAttackComparison> CompareAttacks(
        FusionBossSequenceDefinition sequence,
        FusionRuntimeTraceEvent runStart,
        IReadOnlyList<FusionRuntimeTraceEvent> events,
        double framesPerSecond)
    {
        var comparisons = new List<FusionRuntimeAttackComparison>();
        foreach (var step in sequence.Steps.Where(item => item.AttackGeometry is not null))
        {
            var geometry = step.AttackGeometry!;
            var requests = events
                .Where(entry =>
                    entry.Type.Equals("cast.requested", StringComparison.OrdinalIgnoreCase) &&
                    entry.StepIndex == step.Index &&
                    entry.SkillId == geometry.SkillId)
                .OrderBy(entry => entry.CastIndex)
                .ThenBy(entry => entry.Index)
                .ToArray();
            if (requests.Length == 0)
            {
                comparisons.Add(CreateMissingComparison(sequence, step));
                continue;
            }
            foreach (var request in requests)
            {
                var castEvents = events.Where(entry =>
                    !string.IsNullOrWhiteSpace(request.CastId) &&
                    entry.CastId.Equals(request.CastId, StringComparison.OrdinalIgnoreCase)).ToArray();
                var telegraph = castEvents.FirstOrDefault(entry =>
                    entry.Type.Equals("telegraph.started", StringComparison.OrdinalIgnoreCase));
                var execution = castEvents.FirstOrDefault(entry =>
                    entry.Type.Equals("cast.executed", StringComparison.OrdinalIgnoreCase));
                var colliders = castEvents.Where(entry =>
                    entry.Type.Equals("collider.activated", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => entry.ColliderIndex)
                    .ThenBy(entry => entry.Index)
                    .ToArray();
                var collider = colliders.FirstOrDefault();
                var castIndex = request.CastIndex ?? 0;
                var isPreparedLifecycle = step.AttackLifecycleStage.Equals(
                    "prepare",
                    StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(step.AttackLifecycleId);
                var linkedImpactStep = isPreparedLifecycle && step.LinkedAttackStepIndex is { } impactIndex
                    ? sequence.Steps.FirstOrDefault(candidate => candidate.Index == impactIndex)
                    : null;
                var expectsMovement = linkedImpactStep is not null && sequence.Steps.Any(candidate =>
                    candidate.Index > step.Index &&
                    candidate.Index < linkedImpactStep.Index &&
                    candidate.TechnicalId.Equals("combat.moveTo", StringComparison.OrdinalIgnoreCase));
                var lifecycleId = request.PreparedAttackId;
                if (string.IsNullOrWhiteSpace(lifecycleId))
                {
                    lifecycleId = step.AttackLifecycleId;
                }
                var lifecycleCommit = isPreparedLifecycle
                    ? events.FirstOrDefault(entry =>
                        entry.Type.Equals("attack.commit.requested", StringComparison.OrdinalIgnoreCase) &&
                        entry.PreparedAttackId.Equals(lifecycleId, StringComparison.OrdinalIgnoreCase))
                    : null;
                var movementEvents = isPreparedLifecycle && expectsMovement
                    ? events.Where(entry =>
                        entry.Index >= request.Index &&
                        (lifecycleCommit is null || entry.Index <= lifecycleCommit.Index) &&
                        (string.IsNullOrWhiteSpace(request.Role) ||
                            entry.Role.Equals(request.Role, StringComparison.OrdinalIgnoreCase)))
                        .ToArray()
                    : [];
                var movementStarted = movementEvents.FirstOrDefault(entry =>
                    entry.Type.Equals("movement.started", StringComparison.OrdinalIgnoreCase));
                var movementCompleted = movementEvents.LastOrDefault(entry =>
                    entry.Type.Equals("movement.completed", StringComparison.OrdinalIgnoreCase));
                var lifecycleIncomplete = isPreparedLifecycle &&
                    (lifecycleCommit is null || (expectsMovement && movementCompleted is null));
                var castSpacingFrames = step.TechnicalId.Equals(
                    "combat.castVolley",
                    StringComparison.OrdinalIgnoreCase)
                        ? 0
                        : castIndex * geometry.RepeatDelayMilliseconds *
                            Math.Max(1d, framesPerSecond) / 1000d;
                var expectedExecution = isPreparedLifecycle
                    ? linkedImpactStep?.EarliestStartFrame ?? step.EarliestStartFrame
                    : step.EarliestStartFrame + geometry.ExecutionDelayFrames + castSpacingFrames;
                var actualExecution = execution?.Frame - runStart.Frame;
                var runtimeCommitFrame = lifecycleCommit?.Frame - runStart.Frame;
                var runtimeMovementStartedFrame = movementStarted?.Frame - runStart.Frame;
                var runtimeMovementCompletedFrame = movementCompleted?.Frame - runStart.Frame;
                double? commitExecutionDelta = !isPreparedLifecycle ||
                    actualExecution is null || runtimeCommitFrame is null
                        ? null
                        : actualExecution.Value - runtimeCommitFrame.Value;
                double? landingImpactDelta = !isPreparedLifecycle || !expectsMovement ||
                    actualExecution is null || runtimeMovementCompletedFrame is null
                        ? null
                        : actualExecution.Value - runtimeMovementCompletedFrame.Value;
                var telegraphCenter = telegraph?.Center ?? telegraph?.Point;
                var colliderCenter = collider?.Center;
                var isProjectile = geometry.Kind ==
                    FusionBossAttackGeometryKind.ProjectileCorridor;
                var isDirectionalChain = geometry.Kind ==
                    FusionBossAttackGeometryKind.DirectionalInstantChain;
                var centerOffset = isProjectile
                    ? null
                    : Distance(telegraphCenter, colliderCenter);
                var colliderPathOffset = isDirectionalChain
                    ? DirectionalChainOffset(request, colliders, geometry)
                    : null;
                var expectedColliderCount = isDirectionalChain
                    ? geometry.ChainCount * Math.Max(1, geometry.HitRepeatCount)
                    : (int?)null;
                var runtimeColliderCount = isDirectionalChain
                    ? colliders.Length
                    : (int?)null;
                var colliderCountDelta = expectedColliderCount is null ||
                    runtimeColliderCount is null
                        ? (int?)null
                        : runtimeColliderCount.Value - expectedColliderCount.Value;
                var runtimeRadius = isProjectile
                    ? null
                    : collider?.Geometry?.RadiusTiles;
                double? radiusDelta = runtimeRadius is null
                    ? null
                    : runtimeRadius.Value - geometry.RadiusTiles;
                double? runtimeColliderRadius = isProjectile
                    ? collider?.Geometry?.ColliderRadiusPixels
                    : null;
                double? colliderRadiusDelta = runtimeColliderRadius is null
                    ? null
                    : runtimeColliderRadius.Value - geometry.ProjectileColliderRadiusPixels;
                double? executionDelta = isPreparedLifecycle || actualExecution is null
                    ? null
                    : actualExecution.Value - expectedExecution;
                var status = FidelityStatus(
                    executionDelta,
                    centerOffset,
                    radiusDelta,
                    colliderRadiusDelta,
                    colliderCountDelta,
                    colliderPathOffset,
                    commitExecutionDelta,
                    landingImpactDelta,
                    lifecycleIncomplete,
                    execution);
                comparisons.Add(new FusionRuntimeAttackComparison
                {
                    SequenceId = sequence.Id,
                    SequenceScope = runStart.SequenceScope,
                    SequenceRunId = runStart.SequenceRunId,
                    StepIndex = step.Index,
                    SkillId = geometry.SkillId,
                    CastIndex = castIndex,
                    CastId = request.CastId,
                    ExpectedStartFrame = step.EarliestStartFrame,
                    ExpectedExecutionFrame = expectedExecution,
                    IsExpectedExecutionExact = linkedImpactStep?.IsStartExact ?? step.IsStartExact,
                    RuntimeStartFrame = request.Frame - runStart.Frame,
                    RuntimeTelegraphFrame = telegraph?.Frame - runStart.Frame,
                    RuntimeExecutionFrame = actualExecution,
                    RuntimeColliderFrame = collider?.Frame - runStart.Frame,
                    ExecutionDeltaFrames = executionDelta,
                    TelegraphColliderOffsetTiles = centerOffset,
                    ExpectedRadiusTiles = isProjectile
                        ? null
                        : geometry.RadiusTiles,
                    RuntimeRadiusTiles = runtimeRadius,
                    RadiusDeltaTiles = radiusDelta,
                    ExpectedColliderRadiusPixels = isProjectile
                        ? geometry.ProjectileColliderRadiusPixels
                        : null,
                    RuntimeColliderRadiusPixels = runtimeColliderRadius,
                    ColliderRadiusDeltaPixels = colliderRadiusDelta,
                    ExpectedColliderCount = expectedColliderCount,
                    RuntimeColliderCount = runtimeColliderCount,
                    ColliderCountDelta = colliderCountDelta,
                    ColliderPathOffsetTiles = colliderPathOffset,
                    AttackLifecycleId = lifecycleId,
                    RuntimeCommitFrame = runtimeCommitFrame,
                    RuntimeMovementStartFrame = runtimeMovementStartedFrame,
                    RuntimeMovementCompletedFrame = runtimeMovementCompletedFrame,
                    CommitExecutionDeltaFrames = commitExecutionDelta,
                    LandingImpactDeltaFrames = landingImpactDelta,
                    Status = status,
                    Summary = BuildSummary(
                        status,
                        executionDelta,
                        centerOffset,
                        radiusDelta,
                        colliderRadiusDelta,
                        colliderCountDelta,
                        colliderPathOffset,
                        expectedColliderCount,
                        runtimeColliderCount,
                        commitExecutionDelta,
                        landingImpactDelta,
                        lifecycleIncomplete),
                });
            }
        }
        return comparisons;
    }

    private static IReadOnlyList<FusionRuntimeAttackComparison> MissingComparisons(
        FusionBossSequenceDefinition sequence) => sequence.Steps
        .Where(step => step.AttackGeometry is not null)
        .Select(step => CreateMissingComparison(sequence, step))
        .ToArray();

    private static FusionRuntimeAttackComparison CreateMissingComparison(
        FusionBossSequenceDefinition sequence,
        FusionBossTimelineStep step) => new()
        {
            SequenceId = sequence.Id,
            StepIndex = step.Index,
            SkillId = step.AttackGeometry!.SkillId,
            ExpectedStartFrame = step.EarliestStartFrame,
            ExpectedExecutionFrame = step.AttackLifecycleStage.Equals(
                "prepare",
                StringComparison.OrdinalIgnoreCase) &&
                step.LinkedAttackStepIndex is { } impactIndex
                    ? sequence.Steps.FirstOrDefault(candidate => candidate.Index == impactIndex)?
                        .EarliestStartFrame ?? step.EarliestStartFrame
                    : step.EarliestStartFrame + step.AttackGeometry.ExecutionDelayFrames,
            IsExpectedExecutionExact = step.AttackLifecycleStage.Equals(
                "prepare",
                StringComparison.OrdinalIgnoreCase) &&
                step.LinkedAttackStepIndex is { } exactImpactIndex
                    ? sequence.Steps.FirstOrDefault(candidate => candidate.Index == exactImpactIndex)?
                        .IsStartExact == true
                    : step.IsStartExact,
            AttackLifecycleId = step.AttackLifecycleId,
            ExpectedRadiusTiles = step.AttackGeometry.Kind !=
                FusionBossAttackGeometryKind.ProjectileCorridor
                    ? step.AttackGeometry.RadiusTiles
                    : null,
            ExpectedColliderRadiusPixels = step.AttackGeometry.Kind ==
                FusionBossAttackGeometryKind.ProjectileCorridor
                    ? step.AttackGeometry.ProjectileColliderRadiusPixels
                    : null,
            Status = FusionRuntimeFidelityStatus.Missing,
            Summary = "Nessun cast runtime correlato.",
        };

    private static double? DirectionalChainOffset(
        FusionRuntimeTraceEvent request,
        IReadOnlyList<FusionRuntimeTraceEvent> colliders,
        FusionBossAttackGeometry geometry)
    {
        if (request.Point is null || colliders.Count == 0 || geometry.ChainCount <= 0)
        {
            return null;
        }
        var direction = request.AttackDirection ??
            colliders.Select(entry => entry.Geometry?.Direction ?? 0)
                .FirstOrDefault(value => value > 0);
        if (direction <= 0)
        {
            direction = 2;
        }
        var (dx, dy) = DirectionVector(direction);
        var expected = Enumerable.Range(1, geometry.ChainCount)
            .Select(index => new FusionRuntimeTracePoint
            {
                X = request.Point.X + dx * geometry.ChainSpacingTiles * index,
                Y = request.Point.Y + dy * geometry.ChainSpacingTiles * index +
                    geometry.RuntimeCenterOffsetYTiles,
            })
            .ToArray();
        var offsets = colliders
            .Select((entry, ordinal) =>
            {
                if (entry.Center is null)
                {
                    return (double?)null;
                }
                var recordedIndex = entry.ColliderIndex is >= 0
                    ? entry.ColliderIndex.Value
                    : ordinal;
                var expectedIndex = recordedIndex % expected.Length;
                return Distance(expected[expectedIndex], entry.Center);
            })
            .Where(offset => offset is not null)
            .Select(offset => offset!.Value)
            .ToArray();
        return offsets.Length == 0 ? null : offsets.Max();
    }

    private static (double X, double Y) DirectionVector(int direction) => direction switch
    {
        1 => (-1, 1),
        2 => (0, 1),
        3 => (1, 1),
        4 => (-1, 0),
        6 => (1, 0),
        7 => (-1, -1),
        8 => (0, -1),
        9 => (1, -1),
        _ => (0, 1),
    };

    private static double? Distance(
        FusionRuntimeTracePoint? first,
        FusionRuntimeTracePoint? second)
    {
        if (first is null || second is null)
        {
            return null;
        }
        return Math.Sqrt(
            Math.Pow(first.X - second.X, 2) +
            Math.Pow(first.Y - second.Y, 2));
    }

    private static FusionRuntimeFidelityStatus FidelityStatus(
        double? frameDelta,
        double? centerOffset,
        double? radiusDelta,
        double? colliderRadiusDelta,
        int? colliderCountDelta,
        double? colliderPathOffset,
        double? commitExecutionDelta,
        double? landingImpactDelta,
        bool lifecycleIncomplete,
        FusionRuntimeTraceEvent? execution)
    {
        if (execution is null)
        {
            return FusionRuntimeFidelityStatus.Missing;
        }
        if (lifecycleIncomplete)
        {
            return FusionRuntimeFidelityStatus.Divergent;
        }
        var absoluteFrame = Math.Abs(frameDelta ?? 0);
        var absoluteCenter = Math.Abs(centerOffset ?? 0);
        var absoluteRadius = Math.Abs(radiusDelta ?? 0);
        var absoluteColliderRadius = Math.Abs(colliderRadiusDelta ?? 0);
        var absoluteColliderCount = Math.Abs(colliderCountDelta ?? 0);
        var absolutePathOffset = Math.Abs(colliderPathOffset ?? 0);
        var absoluteCommitDelta = Math.Abs(commitExecutionDelta ?? 0);
        var absoluteLandingDelta = Math.Abs(landingImpactDelta ?? 0);
        if (absoluteFrame > 3 || absoluteCenter > 0.25 || absoluteRadius > 0.1 ||
            absoluteColliderRadius > 1 || absoluteColliderCount > 0 ||
            absolutePathOffset > 0.25 || absoluteCommitDelta > 1 ||
            absoluteLandingDelta > 1)
        {
            return FusionRuntimeFidelityStatus.Divergent;
        }
        if (absoluteFrame > 1 || absoluteCenter > 0.05 || absoluteRadius > 0.05 ||
            absoluteColliderRadius > 0.25 || absolutePathOffset > 0.05 ||
            absoluteCommitDelta > 0 || absoluteLandingDelta > 0)
        {
            return FusionRuntimeFidelityStatus.Drift;
        }
        return FusionRuntimeFidelityStatus.Aligned;
    }

    private static string BuildSummary(
        FusionRuntimeFidelityStatus status,
        double? frameDelta,
        double? centerOffset,
        double? radiusDelta,
        double? colliderRadiusDelta,
        int? colliderCountDelta,
        double? colliderPathOffset,
        int? expectedColliderCount,
        int? runtimeColliderCount,
        double? commitExecutionDelta,
        double? landingImpactDelta,
        bool lifecycleIncomplete)
    {
        var parts = new List<string>();
        if (lifecycleIncomplete)
        {
            parts.Add("lifecycle incompleto");
        }
        if (frameDelta is not null)
        {
            parts.Add($"esecuzione Δ {frameDelta:+0.##;-0.##;0}f");
        }
        if (centerOffset is not null)
        {
            parts.Add($"centro Δ {centerOffset:0.###} tile");
        }
        if (radiusDelta is not null)
        {
            parts.Add($"raggio Δ {radiusDelta:+0.###;-0.###;0} tile");
        }
        if (colliderRadiusDelta is not null)
        {
            parts.Add($"collider Δ {colliderRadiusDelta:+0.##;-0.##;0} px");
        }
        if (expectedColliderCount is not null && runtimeColliderCount is not null)
        {
            parts.Add($"collider {runtimeColliderCount}/{expectedColliderCount}");
        }
        if (colliderCountDelta is not null && colliderCountDelta != 0)
        {
            parts.Add($"quantità Δ {colliderCountDelta:+#;-#;0}");
        }
        if (colliderPathOffset is not null)
        {
            parts.Add($"percorso Δ {colliderPathOffset:0.###} tile");
        }
        if (commitExecutionDelta is not null)
        {
            parts.Add($"commit→hit Δ {commitExecutionDelta:+0.##;-0.##;0}f");
        }
        if (landingImpactDelta is not null)
        {
            parts.Add($"atterraggio→hit Δ {landingImpactDelta:+0.##;-0.##;0}f");
        }
        return parts.Count == 0
            ? status == FusionRuntimeFidelityStatus.Missing
                ? "Dati runtime incompleti."
                : "Previsto e runtime allineati."
            : string.Join(" · ", parts);
    }
}
