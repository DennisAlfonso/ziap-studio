using ZiapStudio.Core.Fusion.Bosses;

namespace ZiapStudio.Services.Fusion.Bosses;

public sealed class FusionBossExecutionGraphService
{
    private const string PhaseEntryNodeId = "@phase-entry";
    private const string PhaseExitNodeId = "@phase-exit";
    private const string OnEnterNodeId = "hook:onEnter";
    private const string OnExitNodeId = "hook:onExit";

    public FusionBossExecutionGraph Build(FusionBossPhaseDefinition phase)
    {
        ArgumentNullException.ThrowIfNull(phase);
        var drafts = new Dictionary<string, NodeDraft>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<FusionBossExecutionEdge>();

        AddTrigger(drafts, PhaseEntryNodeId, "Ingresso nella fase", phase.Id, true);
        if (phase.OnEnterSteps.Count > 0)
        {
            AddHook(drafts, OnEnterNodeId, "Ingresso fase", "onEnter", phase.OnEnterSteps,
                FusionBossTimelineSourceKind.PhaseEnter);
            edges.Add(TriggerEdge(PhaseEntryNodeId, OnEnterNodeId, "UNA VOLTA",
                "Eseguito quando la fase diventa attiva."));
        }

        if (phase.OnExitSteps.Count > 0)
        {
            AddTrigger(drafts, PhaseExitNodeId, "Uscita dalla fase", phase.Id, false);
            AddHook(drafts, OnExitNodeId, "Uscita fase", "onExit", phase.OnExitSteps,
                FusionBossTimelineSourceKind.PhaseExit);
            edges.Add(TriggerEdge(PhaseExitNodeId, OnExitNodeId, "ALL'USCITA",
                "Eseguito prima di abbandonare la fase."));
        }

        foreach (var sequence in phase.Sequences)
        {
            var nodeId = SequenceNodeId(sequence.Id);
            drafts[nodeId] = NodeDraft.FromSequence(nodeId, sequence);
            if (sequence.AutoStart)
            {
                edges.Add(TriggerEdge(
                    PhaseEntryNodeId,
                    nodeId,
                    "AUTO ∥",
                    "Parte all'ingresso della fase, in parallelo agli altri flussi automatici."));
            }
        }

        AddCallEdges(phase.OnEnterSteps, OnEnterNodeId, phase, drafts, edges);
        AddCallEdges(phase.OnExitSteps, OnExitNodeId, phase, drafts, edges);
        foreach (var sequence in phase.Sequences)
        {
            AddCallEdges(
                sequence.Steps,
                SequenceNodeId(sequence.Id),
                phase,
                drafts,
                edges);
        }

        var callEdges = edges
            .Where(edge => edge.Kind is FusionBossExecutionEdgeKind.Call or
                FusionBossExecutionEdgeKind.Repeat)
            .ToArray();
        foreach (var draft in drafts.Values.Where(draft => draft.Sequence is not null))
        {
            var incoming = callEdges
                .Where(edge => edge.TargetNodeId.Equals(draft.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            draft.ApplyIncoming(incoming, drafts, phase);
        }

        var missingCount = drafts.Values.Count(draft =>
            draft.Kind == FusionBossExecutionNodeKind.MissingReference);
        var orphanCount = drafts.Values.Count(draft => draft.IsOrphan);
        var autoCount = phase.Sequences.Count(sequence => sequence.AutoStart);
        var manualCount = phase.Sequences.Count - autoCount;
        var warnings = new List<string>();
        if (orphanCount > 0)
        {
            warnings.Add(orphanCount == 1
                ? "1 flusso manuale senza richiamo dichiarativo"
                : $"{orphanCount} flussi manuali senza richiamo dichiarativo");
        }
        if (missingCount > 0)
        {
            warnings.Add(missingCount == 1
                ? "1 riferimento a una sequenza inesistente"
                : $"{missingCount} riferimenti a sequenze inesistenti");
        }

        return new FusionBossExecutionGraph
        {
            PhaseId = phase.Id,
            Nodes = drafts.Values.Select(draft => draft.ToNode()).ToArray(),
            Edges = edges,
            Summary = $"{autoCount} automatici in parallelo · {manualCount} manuali · " +
                $"{callEdges.Length} richiami dichiarativi",
            WarningSummary = warnings.Count == 0 ? string.Empty : string.Join(" · ", warnings),
        };
    }

    private static void AddCallEdges(
        IReadOnlyList<FusionBossTimelineStep> steps,
        string sourceNodeId,
        FusionBossPhaseDefinition phase,
        IDictionary<string, NodeDraft> drafts,
        ICollection<FusionBossExecutionEdge> edges)
    {
        if (!drafts.ContainsKey(sourceNodeId))
        {
            return;
        }
        foreach (var step in steps.Where(step =>
            step.Kind is FusionBossTimelineStepKind.Sequence or
                FusionBossTimelineStepKind.RepeatSequence &&
            !string.IsNullOrWhiteSpace(step.ReferencedSequenceId)))
        {
            var sequenceId = step.ReferencedSequenceId!;
            var targetNodeId = SequenceNodeId(sequenceId);
            if (!drafts.ContainsKey(targetNodeId))
            {
                var missingId = MissingNodeId(sequenceId);
                if (!drafts.ContainsKey(missingId))
                {
                    drafts[missingId] = NodeDraft.Missing(missingId, sequenceId);
                }
                targetNodeId = missingId;
            }
            var isRepeat = step.Kind == FusionBossTimelineStepKind.RepeatSequence;
            edges.Add(new FusionBossExecutionEdge
            {
                SourceNodeId = sourceNodeId,
                TargetNodeId = targetNodeId,
                Kind = isRepeat
                    ? FusionBossExecutionEdgeKind.Repeat
                    : FusionBossExecutionEdgeKind.Call,
                Label = isRepeat ? "RIPETE" : "CHIAMA",
                Detail = $"Step {step.Index + 1} · " +
                    (string.IsNullOrWhiteSpace(step.TechnicalDetail)
                        ? step.Detail
                        : step.TechnicalDetail),
                SourceStepIndex = step.Index,
            });
        }
    }

    private static FusionBossExecutionEdge TriggerEdge(
        string source,
        string target,
        string label,
        string detail) => new()
    {
        SourceNodeId = source,
        TargetNodeId = target,
        Kind = FusionBossExecutionEdgeKind.PhaseTrigger,
        Label = label,
        Detail = detail,
    };

    private static void AddTrigger(
        IDictionary<string, NodeDraft> drafts,
        string id,
        string displayName,
        string phaseId,
        bool isEntry) => drafts[id] = new NodeDraft
    {
        Id = id,
        DisplayName = displayName,
        TechnicalId = phaseId,
        Kind = isEntry
            ? FusionBossExecutionNodeKind.PhaseEntry
            : FusionBossExecutionNodeKind.PhaseExit,
        BadgeText = "TRIGGER",
        StartReason = isEntry
            ? $"Si attiva quando il runtime entra nella fase “{phaseId}”."
            : $"Si attiva quando il runtime lascia la fase “{phaseId}”.",
        CallerSummary = "Generato dal ciclo di vita della fase.",
        OutcomeSummary = "Distribuisce l'esecuzione ai flussi collegati.",
    };

    private static void AddHook(
        IDictionary<string, NodeDraft> drafts,
        string id,
        string displayName,
        string technicalId,
        IReadOnlyList<FusionBossTimelineStep> steps,
        FusionBossTimelineSourceKind sourceKind) => drafts[id] = new NodeDraft
    {
        Id = id,
        DisplayName = displayName,
        TechnicalId = technicalId,
        Kind = FusionBossExecutionNodeKind.PhaseHook,
        TimelineSourceId = technicalId,
        TimelineSourceKind = sourceKind,
        BadgeText = "HOOK",
        StartReason = sourceKind == FusionBossTimelineSourceKind.PhaseEnter
            ? "Eseguito una volta quando la fase diventa attiva."
            : "Eseguito una volta prima di abbandonare la fase.",
        CallerSummary = "Richiamato direttamente dal ciclo di vita della fase.",
        OutcomeSummary = "Il suo completamento appartiene al ciclo di vita della fase.",
        StepCount = steps.Count,
        DurationSummary = DurationSummary(steps),
    };

    private static string SequenceNodeId(string sequenceId) => $"sequence:{sequenceId}";

    private static string MissingNodeId(string sequenceId) => $"missing:{sequenceId}";

    private static string DurationSummary(IReadOnlyList<FusionBossTimelineStep> steps)
    {
        var minimum = steps.Sum(step => step.DurationFrames ?? 0);
        var dynamic = steps.Any(step => step.Kind is
            FusionBossTimelineStepKind.WaitUntil or
            FusionBossTimelineStepKind.Sequence or
            FusionBossTimelineStepKind.RepeatSequence);
        return dynamic
            ? $"almeno {minimum} frame · durata dinamica"
            : $"{minimum} frame";
    }

    private sealed class NodeDraft
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required string TechnicalId { get; init; }
        public FusionBossExecutionNodeKind Kind { get; init; }
        public FusionBossSequenceDefinition? Sequence { get; init; }
        public string? TimelineSourceId { get; init; }
        public FusionBossTimelineSourceKind TimelineSourceKind { get; init; }
        public string BadgeText { get; init; } = string.Empty;
        public string StartReason { get; set; } = string.Empty;
        public string CallerSummary { get; set; } = string.Empty;
        public string OutcomeSummary { get; set; } = string.Empty;
        public string DurationSummary { get; init; } = string.Empty;
        public int StepCount { get; init; }
        public bool IsWarning { get; set; }
        public bool IsOrphan { get; set; }

        public static NodeDraft FromSequence(
            string nodeId,
            FusionBossSequenceDefinition sequence) => new()
        {
            Id = nodeId,
            DisplayName = string.IsNullOrWhiteSpace(sequence.DisplayName)
                ? sequence.Id
                : sequence.DisplayName,
            TechnicalId = sequence.Id,
            Kind = sequence.AutoStart
                ? FusionBossExecutionNodeKind.AutomaticSequence
                : FusionBossExecutionNodeKind.ManualSequence,
            Sequence = sequence,
            TimelineSourceId = sequence.Id,
            TimelineSourceKind = FusionBossTimelineSourceKind.Sequence,
            BadgeText = sequence.AutoStart ? "AUTO ∥" : "MANUALE",
            DurationSummary = sequence.HasDynamicTiming
                ? $"almeno {sequence.MinimumDurationFrames} frame · durata dinamica"
                : $"{sequence.MinimumDurationFrames} frame",
            StepCount = sequence.Steps.Count,
        };

        public static NodeDraft Missing(string nodeId, string sequenceId) => new()
        {
            Id = nodeId,
            DisplayName = "Sequenza mancante",
            TechnicalId = sequenceId,
            Kind = FusionBossExecutionNodeKind.MissingReference,
            BadgeText = "MANCANTE",
            StartReason = "Un altro flusso tenta di richiamare questa sequenza, ma la definizione non esiste.",
            CallerSummary = "Controllare i collegamenti entranti nel grafo.",
            OutcomeSummary = "Il riferimento può causare il fallimento della sequenza chiamante.",
            IsWarning = true,
        };

        public void ApplyIncoming(
            IReadOnlyList<FusionBossExecutionEdge> incoming,
            IReadOnlyDictionary<string, NodeDraft> drafts,
            FusionBossPhaseDefinition phase)
        {
            var sequence = Sequence!;
            IsOrphan = !sequence.AutoStart && incoming.Count == 0;
            IsWarning = IsOrphan;
            var callerDetails = incoming.Select(edge =>
            {
                var caller = drafts.TryGetValue(edge.SourceNodeId, out var source)
                    ? source.DisplayName
                    : edge.SourceNodeId;
                var step = (edge.SourceStepIndex ?? 0) + 1;
                var operation = edge.Kind == FusionBossExecutionEdgeKind.Repeat
                    ? "ripetizione"
                    : "chiamata";
                return new { Caller = caller, Step = step, Operation = operation };
            }).ToArray();
            CallerSummary = callerDetails.Length == 0
                ? sequence.AutoStart
                    ? "Nessun chiamante: il trigger è l'ingresso nella fase."
                    : "Nessun richiamo dichiarativo trovato; può partire soltanto da API o runtime esterno."
                : string.Join(
                    " ",
                    callerDetails.Select(caller =>
                        $"Richiamata da “{caller.Caller}” allo step {caller.Step} tramite {caller.Operation}."));
            StartReason = sequence.AutoStart
                ? callerDetails.Length == 0
                    ? $"Parte automaticamente entrando nella fase “{phase.Id}”, in parallelo agli altri flussi AUTO."
                    : $"Parte automaticamente entrando nella fase “{phase.Id}” e può anche essere richiamato da altri flussi."
                : callerDetails.Length == 0
                    ? "Non parte automaticamente e non ha chiamanti dichiarativi."
                    : string.Join(
                        " Oppure ",
                        callerDetails.Select(caller =>
                            $"parte quando “{caller.Caller}” raggiunge lo step {caller.Step} ({caller.Operation})")) + ".";

            var observedTransitions = phase.Transitions
                .Where(transition => transition.ConditionSummary.Contains(
                    $"sequenceFailed({sequence.Id}",
                    StringComparison.OrdinalIgnoreCase))
                .Select(transition => $"fallimento → {transition.TargetPhaseId}")
                .ToArray();
            OutcomeSummary = observedTransitions.Length == 0
                ? "Nessuna transizione della fase osserva direttamente il suo esito."
                : $"La fase osserva questo flusso: {string.Join("; ", observedTransitions)}.";
        }

        public FusionBossExecutionNode ToNode() => new()
        {
            Id = Id,
            DisplayName = DisplayName,
            TechnicalId = TechnicalId,
            Kind = Kind,
            TimelineSourceId = TimelineSourceId,
            TimelineSourceKind = TimelineSourceKind,
            BadgeText = BadgeText,
            StartReason = StartReason,
            CallerSummary = CallerSummary,
            OutcomeSummary = OutcomeSummary,
            DurationSummary = DurationSummary,
            StepCount = StepCount,
            IsWarning = IsWarning,
            IsOrphan = IsOrphan,
        };
    }
}
