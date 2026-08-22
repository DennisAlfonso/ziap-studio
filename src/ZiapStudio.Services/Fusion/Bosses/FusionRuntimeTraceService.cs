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
                var collider = castEvents.FirstOrDefault(entry =>
                    entry.Type.Equals("collider.activated", StringComparison.OrdinalIgnoreCase));
                var castIndex = request.CastIndex ?? 0;
                var castSpacingFrames = step.TechnicalId.Equals(
                    "combat.castVolley",
                    StringComparison.OrdinalIgnoreCase)
                        ? 0
                        : castIndex * geometry.RepeatDelayMilliseconds *
                            Math.Max(1d, framesPerSecond) / 1000d;
                var expectedExecution = step.EarliestStartFrame +
                    geometry.ExecutionDelayFrames +
                    castSpacingFrames;
                var actualExecution = execution?.Frame - runStart.Frame;
                var telegraphCenter = telegraph?.Center ?? telegraph?.Point;
                var colliderCenter = collider?.Center;
                var isProjectile = geometry.Kind ==
                    FusionBossAttackGeometryKind.ProjectileCorridor;
                var centerOffset = isProjectile
                    ? null
                    : Distance(telegraphCenter, colliderCenter);
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
                double? executionDelta = actualExecution is null
                    ? null
                    : actualExecution.Value - expectedExecution;
                var status = FidelityStatus(
                    executionDelta,
                    centerOffset,
                    radiusDelta,
                    colliderRadiusDelta,
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
                    Status = status,
                    Summary = BuildSummary(
                        status,
                        executionDelta,
                        centerOffset,
                        radiusDelta,
                        colliderRadiusDelta),
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
            ExpectedExecutionFrame = step.EarliestStartFrame +
                step.AttackGeometry.ExecutionDelayFrames,
            ExpectedRadiusTiles = step.AttackGeometry.Kind ==
                FusionBossAttackGeometryKind.InstantCircle
                    ? step.AttackGeometry.RadiusTiles
                    : null,
            ExpectedColliderRadiusPixels = step.AttackGeometry.Kind ==
                FusionBossAttackGeometryKind.ProjectileCorridor
                    ? step.AttackGeometry.ProjectileColliderRadiusPixels
                    : null,
            Status = FusionRuntimeFidelityStatus.Missing,
            Summary = "Nessun cast runtime correlato.",
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
        FusionRuntimeTraceEvent? execution)
    {
        if (execution is null)
        {
            return FusionRuntimeFidelityStatus.Missing;
        }
        var absoluteFrame = Math.Abs(frameDelta ?? 0);
        var absoluteCenter = Math.Abs(centerOffset ?? 0);
        var absoluteRadius = Math.Abs(radiusDelta ?? 0);
        var absoluteColliderRadius = Math.Abs(colliderRadiusDelta ?? 0);
        if (absoluteFrame > 3 || absoluteCenter > 0.25 || absoluteRadius > 0.1 ||
            absoluteColliderRadius > 1)
        {
            return FusionRuntimeFidelityStatus.Divergent;
        }
        if (absoluteFrame > 1 || absoluteCenter > 0.05 || absoluteRadius > 0.05 ||
            absoluteColliderRadius > 0.25)
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
        double? colliderRadiusDelta)
    {
        var parts = new List<string>();
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
        return parts.Count == 0
            ? status == FusionRuntimeFidelityStatus.Missing
                ? "Dati runtime incompleti."
                : "Previsto e runtime allineati."
            : string.Join(" · ", parts);
    }
}
