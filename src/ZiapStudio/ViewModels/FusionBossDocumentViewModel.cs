using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Bosses;
using ZiapStudio.Services.Fusion.Bosses;

namespace ZiapStudio.ViewModels;

public sealed class FusionBossDocumentViewModel : INotifyPropertyChanged
{
    private FusionBossEncounterViewModel? _selectedEncounter;
    private FusionBossPhaseViewModel? _selectedPhase;
    private FusionBossSequenceViewModel? _selectedSequence;
    private FusionBossExecutionNode? _selectedExecutionNode;
    private FusionBossTimelineStepViewModel? _selectedTimelineStep;
    private FusionBossArenaViewModel? _selectedArena;
    private FusionBossMapSceneViewModel? _selectedMap;
    private IReadOnlyList<FusionRuntimeTraceViewModel> _allRuntimeTraces = [];
    private IReadOnlyList<FusionRuntimeTraceViewModel> _runtimeTraces = [];
    private FusionRuntimeTraceViewModel? _selectedRuntimeTrace;
    private FusionRuntimeSequenceAnalysis? _runtimeTraceAnalysis;
    private IReadOnlyList<FusionRuntimeAttackComparisonViewModel> _runtimeAttackComparisons = [];
    private FusionRuntimeAttackComparisonViewModel? _selectedRuntimeAttack;
    private IReadOnlyList<string> _runtimeTraceErrors = [];
    private bool _showAlignedRuntimeAttacks;
    private bool _isArenaPreviewDetached;
    private double _previewFrame;
    private string _editorPhaseDisplayName = string.Empty;
    private string _editorPhaseSummary = string.Empty;
    private string _editorPhasePlayerGoal = string.Empty;
    private string _editorPhaseDesignerIntent = string.Empty;
    private string _editorSequenceDisplayName = string.Empty;
    private string _editorSequenceSummary = string.Empty;
    private string _editorSequencePlayerGoal = string.Empty;
    private string _editorSequenceDesignerIntent = string.Empty;
    private string _editorStepJson = string.Empty;
    private string _editorStatusText = "Modifica i campi e applicali in memoria; Ctrl+S salva il file.";
    private readonly FusionRuntimeTraceService _runtimeTraceService = new();
    private readonly FusionBossEditSession? _editSession;
    private static readonly JsonSerializerOptions EditorJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public FusionBossDocumentViewModel(
        FusionBossWorkspaceDocument document,
        FusionBossEditSession? editSession)
    {
        Document = document;
        _editSession = editSession;
        if (_editSession is not null)
        {
            _editSession.PropertyChanged += EditSession_PropertyChanged;
        }
        else
        {
            _editorStatusText = "Sola lettura: FusionEncounters.json non è disponibile come JSON valido.";
        }
        Databases = document.Databases
            .Select(database => new FusionBossDatabaseViewModel(database))
            .ToArray();
        Plugins = document.Plugins
            .Select(plugin => new FusionBossPluginViewModel(plugin))
            .ToArray();
        Diagnostics = document.Diagnostics
            .Select(diagnostic => new FusionBossDiagnosticViewModel(diagnostic))
            .ToArray();
        Encounters = document.Encounters
            .Select(encounter => new FusionBossEncounterViewModel(encounter))
            .ToArray();
        Arenas = document.Arenas
            .Select(arena => new FusionBossArenaViewModel(arena))
            .ToArray();
        SelectedEncounter = Encounters.FirstOrDefault();
        SelectedArena ??= Arenas.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal event EventHandler? ExternalSurfacesCloseRequested;

    internal bool ReattachArenaPreviewOnWindowClose { get; private set; } = true;

    public FusionBossWorkspaceDocument Document { get; }

    public IReadOnlyList<FusionBossDatabaseViewModel> Databases { get; }

    public IReadOnlyList<FusionBossPluginViewModel> Plugins { get; }

    public IReadOnlyList<FusionBossDiagnosticViewModel> Diagnostics { get; }

    public IReadOnlyList<FusionBossEncounterViewModel> Encounters { get; }

    public IReadOnlyList<FusionBossArenaViewModel> Arenas { get; }

    public string ProjectPath => Document.ProjectPath;

    public FusionBossEncounterViewModel? SelectedEncounter
    {
        get => _selectedEncounter;
        set
        {
            if (ReferenceEquals(_selectedEncounter, value))
            {
                return;
            }
            _selectedEncounter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedEncounterSubtitle));
            OnPropertyChanged(nameof(Phases));
            SelectedPhase = value?.Phases.FirstOrDefault(phase => phase.IsInitial) ??
                value?.Phases.FirstOrDefault();
            SelectedArena = value is null
                ? SelectedArena
                : Arenas.FirstOrDefault(arena => arena.EncounterId.Equals(
                    value.Id,
                    StringComparison.OrdinalIgnoreCase)) ?? SelectedArena;
            RefreshRuntimeTraceChoices();
            RefreshEditorFields();
        }
    }

    public IReadOnlyList<FusionBossPhaseViewModel> Phases =>
        SelectedEncounter?.Phases ?? [];

    public FusionBossPhaseViewModel? SelectedPhase
    {
        get => _selectedPhase;
        set
        {
            if (ReferenceEquals(_selectedPhase, value))
            {
                return;
            }
            _selectedPhase = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Sequences));
            OnPropertyChanged(nameof(PhaseSummaryText));
            OnPropertyChanged(nameof(ExecutionGraph));
            OnPropertyChanged(nameof(ExecutionGraphSummaryText));
            OnPropertyChanged(nameof(ExecutionGraphWarningText));
            SelectedSequence = value?.Sequences.FirstOrDefault(sequence =>
                sequence.Sequence.SourceKind == FusionBossTimelineSourceKind.Sequence) ??
                value?.Sequences.FirstOrDefault();
            SelectedExecutionNode = value?.ExecutionGraph.Nodes.FirstOrDefault(node =>
                node.Kind == FusionBossExecutionNodeKind.PhaseEntry) ??
                value?.ExecutionGraph.Nodes.FirstOrDefault();
            RefreshEditorFields();
        }
    }

    public IReadOnlyList<FusionBossSequenceViewModel> Sequences =>
        SelectedPhase?.Sequences ?? [];

    public FusionBossSequenceViewModel? SelectedSequence
    {
        get => _selectedSequence;
        set
        {
            if (ReferenceEquals(_selectedSequence, value))
            {
                return;
            }
            _selectedSequence = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimelineSteps));
            OnPropertyChanged(nameof(TimelineTitle));
            OnPropertyChanged(nameof(TimelineSubtitle));
            OnPropertyChanged(nameof(SequenceIntentText));
            OnPropertyChanged(nameof(TimelineDurationText));
            OnPropertyChanged(nameof(TimelineMaximumFrame));
            OnPropertyChanged(nameof(TimelineFirstInexactFrame));
            OnPropertyChanged(nameof(PreviewFrameText));
            OnPropertyChanged(nameof(TimelineStepListHeader));
            PreviewFrame = 0;
            SelectedTimelineStep = value?.Steps.FirstOrDefault(step => step.HasAttackGeometry) ??
                value?.Steps.FirstOrDefault();
            var executionNode = FindExecutionNode(value);
            if (executionNode is not null && !ReferenceEquals(_selectedExecutionNode, executionNode))
            {
                _selectedExecutionNode = executionNode;
                OnPropertyChanged(nameof(SelectedExecutionNode));
                OnPropertyChanged(nameof(CanOpenSelectedExecutionTimeline));
            }
            RefreshRuntimeTraceAnalysis();
            RefreshEditorFields();
        }
    }

    public FusionBossExecutionGraph? ExecutionGraph => SelectedPhase?.ExecutionGraph;

    public FusionBossExecutionNode? SelectedExecutionNode
    {
        get => _selectedExecutionNode;
        set
        {
            if (ReferenceEquals(_selectedExecutionNode, value))
            {
                return;
            }
            _selectedExecutionNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanOpenSelectedExecutionTimeline));
            if (value?.TimelineSourceId is not { } sourceId)
            {
                return;
            }
            var sequence = Sequences.FirstOrDefault(candidate =>
                candidate.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase) &&
                candidate.Sequence.SourceKind == value.TimelineSourceKind);
            if (sequence is not null && !ReferenceEquals(SelectedSequence, sequence))
            {
                SelectedSequence = sequence;
            }
        }
    }

    public bool CanOpenSelectedExecutionTimeline =>
        SelectedExecutionNode?.CanOpenTimeline == true;

    public string ExecutionGraphSummaryText => ExecutionGraph?.Summary ??
        "Nessuna esecuzione disponibile per la fase.";

    public string ExecutionGraphWarningText => ExecutionGraph?.WarningSummary ?? string.Empty;

    public void SelectExecutionEdge(FusionBossExecutionEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        var sourceNode = ExecutionGraph?.Nodes.FirstOrDefault(node => node.Id.Equals(
            edge.SourceNodeId,
            StringComparison.OrdinalIgnoreCase));
        if (sourceNode is not null)
        {
            SelectedExecutionNode = sourceNode;
        }
        if (edge.SourceStepIndex is { } stepIndex && SelectedSequence is { } sequence)
        {
            SelectedTimelineStep = sequence.Steps.FirstOrDefault(step =>
                step.Step.Index == stepIndex) ?? SelectedTimelineStep;
        }
    }

    public IReadOnlyList<FusionBossTimelineStepViewModel> TimelineSteps =>
        SelectedSequence?.Steps ?? [];

    public FusionBossTimelineStepViewModel? SelectedTimelineStep
    {
        get => _selectedTimelineStep;
        set
        {
            if (ReferenceEquals(_selectedTimelineStep, value))
            {
                return;
            }
            _selectedTimelineStep = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAttackGeometry));
            OnPropertyChanged(nameof(SelectedAttackTarget));
            OnPropertyChanged(nameof(RuntimeDeclaredContractText));
            OnPropertyChanged(nameof(RuntimeContractInterpretationText));
            if (value is not null)
            {
                PreviewFrame = value.Step.EarliestStartFrame;
            }
            RefreshEditorFields();
        }
    }

    public string EditorPhaseDisplayName
    {
        get => _editorPhaseDisplayName;
        set => SetProperty(ref _editorPhaseDisplayName, value);
    }

    public string EditorPhaseSummary
    {
        get => _editorPhaseSummary;
        set => SetProperty(ref _editorPhaseSummary, value);
    }

    public string EditorPhasePlayerGoal
    {
        get => _editorPhasePlayerGoal;
        set => SetProperty(ref _editorPhasePlayerGoal, value);
    }

    public string EditorPhaseDesignerIntent
    {
        get => _editorPhaseDesignerIntent;
        set => SetProperty(ref _editorPhaseDesignerIntent, value);
    }

    public string EditorSequenceDisplayName
    {
        get => _editorSequenceDisplayName;
        set => SetProperty(ref _editorSequenceDisplayName, value);
    }

    public string EditorSequenceSummary
    {
        get => _editorSequenceSummary;
        set => SetProperty(ref _editorSequenceSummary, value);
    }

    public string EditorSequencePlayerGoal
    {
        get => _editorSequencePlayerGoal;
        set => SetProperty(ref _editorSequencePlayerGoal, value);
    }

    public string EditorSequenceDesignerIntent
    {
        get => _editorSequenceDesignerIntent;
        set => SetProperty(ref _editorSequenceDesignerIntent, value);
    }

    public string EditorStepJson
    {
        get => _editorStepJson;
        set => SetProperty(ref _editorStepJson, value);
    }

    public string EditorStatusText
    {
        get => _editorStatusText;
        private set => SetProperty(ref _editorStatusText, value);
    }

    public bool CanEditSelectedSequence =>
        CanAuthor &&
        SelectedSequence?.Sequence.SourceKind == FusionBossTimelineSourceKind.Sequence;

    public bool CanEditSelectedStep => CanAuthor && SelectedTimelineStep is not null;

    public bool CanAuthor => _editSession is not null;

    public bool EditorCanUndo => _editSession?.CanUndo == true;

    public bool EditorCanRedo => _editSession?.CanRedo == true;

    public bool EditorIsDirty => _editSession?.IsDirty == true;

    public string EditorDirtyText => !CanAuthor
        ? "Authoring non disponibile"
        : EditorIsDirty
            ? "Modifiche in memoria · Ctrl+S per salvare"
            : "Nessuna modifica da salvare";

    public string EditorSelectionText => SelectedPhase is null
        ? "Nessuna fase selezionata"
        : SelectedSequence is null
            ? $"{SelectedEncounter?.Id} / {SelectedPhase.Id}"
            : $"{SelectedEncounter?.Id} / {SelectedPhase.Id} / {SelectedSequence.Id}";

    public void ApplyPhaseEdits()
    {
        if (_editSession is null || PhasePointer() is not { } pointer)
        {
            EditorStatusText = "Seleziona una fase prima di applicare le modifiche.";
            return;
        }
        _editSession.SetValues(
        [
            ($"{pointer}/displayName", JsonValue.Create(EditorPhaseDisplayName)),
            ($"{pointer}/summary", JsonValue.Create(EditorPhaseSummary)),
            ($"{pointer}/playerGoal", JsonValue.Create(EditorPhasePlayerGoal)),
            ($"{pointer}/designerIntent", JsonValue.Create(EditorPhaseDesignerIntent)),
        ]);
        EditorStatusText = "Metadati della fase applicati in memoria; timeline e preview si riallineano dopo il salvataggio.";
    }

    public void ApplySequenceEdits()
    {
        if (_editSession is null || !CanEditSelectedSequence || SequencePointer() is not { } pointer)
        {
            EditorStatusText = "Gli hook di fase si modificano attraverso i loro step.";
            return;
        }
        _editSession.SetValues(
        [
            ($"{pointer}/displayName", JsonValue.Create(EditorSequenceDisplayName)),
            ($"{pointer}/summary", JsonValue.Create(EditorSequenceSummary)),
            ($"{pointer}/playerGoal", JsonValue.Create(EditorSequencePlayerGoal)),
            ($"{pointer}/designerIntent", JsonValue.Create(EditorSequenceDesignerIntent)),
        ]);
        EditorStatusText = "Metadati della sequenza applicati in memoria; timeline e preview si riallineano dopo il salvataggio.";
    }

    public void ApplyStepJson()
    {
        if (_editSession is null || !CanEditSelectedStep || StepPointer() is not { } pointer)
        {
            EditorStatusText = "Seleziona uno step prima di modificarlo.";
            return;
        }
        try
        {
            var node = JsonNode.Parse(EditorStepJson);
            if (node is not JsonArray && node is not JsonObject)
            {
                EditorStatusText = "Uno step deve essere un array azione o un oggetto controllo.";
                return;
            }
            _editSession.SetValue(pointer, node);
            EditorStatusText = "Step applicato in memoria; sarà validato e riproiettato su timeline e preview al salvataggio.";
        }
        catch (JsonException exception)
        {
            EditorStatusText = $"JSON dello step non valido: {exception.Message}";
        }
    }

    public void UndoEditorChange()
    {
        if (_editSession?.Undo() == true)
        {
            EditorStatusText = "Ultima modifica annullata.";
        }
    }

    public void RedoEditorChange()
    {
        if (_editSession?.Redo() == true)
        {
            EditorStatusText = "Modifica ripristinata.";
        }
    }

    public FusionBossAttackGeometry? SelectedAttackGeometry =>
        SelectedTimelineStep?.AttackGeometry;

    public FusionBossAttackTarget? SelectedAttackTarget =>
        SelectedTimelineStep?.AttackTarget;

    public FusionBossArenaViewModel? SelectedArena
    {
        get => _selectedArena;
        set
        {
            if (ReferenceEquals(_selectedArena, value))
            {
                return;
            }
            _selectedArena = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Maps));
            OnPropertyChanged(nameof(ArenaSummaryText));
            SelectedMap = value?.Maps.FirstOrDefault();
        }
    }

    public IReadOnlyList<FusionBossMapSceneViewModel> Maps =>
        SelectedArena?.Maps ?? [];

    public FusionBossMapSceneViewModel? SelectedMap
    {
        get => _selectedMap;
        set
        {
            if (ReferenceEquals(_selectedMap, value))
            {
                return;
            }
            _selectedMap = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MapSummaryText));
            OnPropertyChanged(nameof(TimelineMaximumFrame));
            OnPropertyChanged(nameof(PreviewFrameText));
            PreviewFrame = Math.Min(PreviewFrame, TimelineMaximumFrame);
            RefreshRuntimeTraceChoices(preferSelectedMap: true);
        }
    }

    public IReadOnlyList<FusionRuntimeTraceViewModel> RuntimeTraces => _runtimeTraces;

    public FusionRuntimeTraceViewModel? SelectedRuntimeTrace
    {
        get => _selectedRuntimeTrace;
        set
        {
            if (ReferenceEquals(_selectedRuntimeTrace, value))
            {
                return;
            }
            _selectedRuntimeTrace = value;
            OnPropertyChanged();
            AlignArenaToRuntimeTrace();
            RefreshRuntimeTraceAnalysis();
        }
    }

    public FusionRuntimeSequenceAnalysis? RuntimeTraceAnalysis =>
        _runtimeTraceAnalysis;

    public IReadOnlyList<FusionRuntimeAttackComparisonViewModel> RuntimeAttackComparisons =>
        _runtimeAttackComparisons;

    public FusionRuntimeAttackComparisonViewModel? SelectedRuntimeAttack
    {
        get => _selectedRuntimeAttack;
        set
        {
            if (ReferenceEquals(_selectedRuntimeAttack, value))
            {
                return;
            }
            _selectedRuntimeAttack = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedRuntimeAttack));
            OnPropertyChanged(nameof(RuntimeReviewSelectionHint));
            if (value is null)
            {
                return;
            }
            var timelineStep = TimelineSteps.FirstOrDefault(step =>
                step.Step.Index == value.Comparison.StepIndex);
            if (timelineStep is not null)
            {
                SelectedTimelineStep = timelineStep;
            }
            PreviewFrame = value.FocusFrame;
        }
    }

    public bool ShowAlignedRuntimeAttacks
    {
        get => _showAlignedRuntimeAttacks;
        set
        {
            if (_showAlignedRuntimeAttacks == value)
            {
                return;
            }
            _showAlignedRuntimeAttacks = value;
            OnPropertyChanged();
            RefreshRuntimeAttackComparisons();
        }
    }

    public int RuntimeAlignedCount => RuntimeStatusCount(FusionRuntimeFidelityStatus.Aligned);

    public int RuntimeDriftCount => RuntimeStatusCount(FusionRuntimeFidelityStatus.Drift);

    public int RuntimeDivergentCount => RuntimeStatusCount(FusionRuntimeFidelityStatus.Divergent);

    public int RuntimeMissingCount => RuntimeStatusCount(FusionRuntimeFidelityStatus.Missing);

    public string RuntimeReviewListHeader => RuntimeTraceAnalysis?.Attacks.All(
        comparison => comparison.Status == FusionRuntimeFidelityStatus.Aligned) == true
            ? $"Confronti allineati ({RuntimeAttackComparisons.Count})"
            : ShowAlignedRuntimeAttacks
                ? $"Tutti i confronti ({RuntimeAttackComparisons.Count})"
                : $"Da verificare ({RuntimeAttackComparisons.Count})";

    public string RuntimeReviewSummaryText
    {
        get
        {
            if (SelectedRuntimeTrace is null)
            {
                return "Scegli un trace per confrontare geometria prevista e runtime.";
            }
            if (RuntimeTraceAnalysis is null || RuntimeTraceAnalysis.Events.Count == 0)
            {
                return "La sequenza selezionata non compare nella run corrente.";
            }
            var total = RuntimeTraceAnalysis.Attacks.Count;
            var problems = total - RuntimeAlignedCount;
            return $"{RuntimeTraceAnalysis.Events.Count} eventi letti · {total} attacchi confrontati · " +
                $"{problems} da verificare";
        }
    }

    public string RuntimeReviewEmptyText
    {
        get
        {
            if (SelectedRuntimeTrace is null)
            {
                return "Nessun trace selezionato.";
            }
            if (RuntimeTraceAnalysis is null || RuntimeTraceAnalysis.Events.Count == 0)
            {
                return "Nessun evento della sequenza in questa run.";
            }
            if (RuntimeTraceAnalysis.Attacks.Count == 0)
            {
                return "La sequenza non contiene attacchi confrontabili.";
            }
            if (RuntimeAttackComparisons.Count == 0)
            {
                return "Nessuno scostamento: abilita “Mostra allineati” per vedere tutti i confronti.";
            }
            return "Seleziona un attacco per sincronizzare timeline e Arena Preview.";
        }
    }

    public string RuntimeReviewSelectionHint => SelectedRuntimeAttack is null
        ? "Seleziona un confronto per leggerne timing, geometria e identificativi runtime."
        : string.Empty;

    public bool HasSelectedRuntimeAttack => SelectedRuntimeAttack is not null;

    public string RuntimeDeclaredContractText
    {
        get
        {
            var step = SelectedTimelineStep?.Step;
            var geometry = step?.AttackGeometry;
            if (step is null || geometry is null)
            {
                return "Nessun contratto d'attacco disponibile per lo step selezionato.";
            }
            var targetCount = Math.Max(1, step.AttackTargets.Count);
            var targetText = step.AttackTargets.Count > 0
                ? $"{targetCount} bersagli"
                : "1 bersaglio";
            var shapeText = geometry.Kind switch
            {
                FusionBossAttackGeometryKind.ProjectileCorridor =>
                    $"corridoio proiettile, collider {geometry.ProjectileColliderRadiusPixels:0.##} px",
                FusionBossAttackGeometryKind.DirectionalInstantChain =>
                    $"catena direzionale di {geometry.ChainCount} collider, " +
                    $"raggio {geometry.RadiusTiles:0.##}, passo {geometry.ChainSpacingTiles:0.##} tile",
                _ => $"area circolare istantanea, raggio {geometry.RadiusTiles:0.##} tile",
            };
            var cadenceText = step.TechnicalId.Equals(
                "combat.castVolley",
                StringComparison.OrdinalIgnoreCase)
                    ? "cast paralleli"
                    : "cast sequenziali";
            if (step.AttackLifecycleStage.Equals("prepare", StringComparison.OrdinalIgnoreCase))
            {
                return $"{step.TechnicalId} · {targetText} · {shapeText} · telegraph mantenuto " +
                    $"fino al commit “{step.AttackLifecycleId}”, poi impatto immediato.";
            }
            return $"{step.TechnicalId} · {targetText}, {cadenceText} · {shapeText} · " +
                $"esecuzione dopo {geometry.ExecutionDelayFrames} frame.";
        }
    }

    public string RuntimeContractInterpretationText
    {
        get
        {
            var step = SelectedTimelineStep?.Step;
            if (step?.AttackLifecycleStage.Equals(
                "prepare",
                StringComparison.OrdinalIgnoreCase) == true)
            {
                return "Questo attacco usa un lifecycle esplicito: il telegraph viene preparato, " +
                    "il movimento avviene mentre resta visibile e il commit applica la skill nello " +
                    "stesso frame dell'atterraggio. Runtime Review verifica entrambi i collegamenti.";
            }
            if (step?.AttackGeometry?.Kind == FusionBossAttackGeometryKind.DirectionalInstantChain)
            {
                var directions = step.AttackTargets
                    .Select(target => target.ExecutionDirection)
                    .Distinct()
                    .Select(DirectionLabel);
                return $"Alpha ABS esegue {step.AttackGeometry.ChainCount} collider per cast " +
                    $"lungo {step.AttackGeometry.ChainReachTiles:0.##} tile. " +
                    $"Direzione: {string.Join(", ", directions)}. Runtime Review verifica quantità e percorso.";
            }
            if (step?.AttackGeometry?.Kind == FusionBossAttackGeometryKind.InstantCircle &&
                step.TechnicalId.Equals("combat.castVolley", StringComparison.OrdinalIgnoreCase) &&
                step.AttackTargets.Count > 1)
            {
                return "Questa dichiarazione non contiene origine, direzione o percorso: " +
                    "la Runtime Review può confermare le aree circolari, ma non può dedurre " +
                    "la traiettoria lineare desiderata. In questo caso il problema è nel contratto di authoring.";
            }
            return "La Runtime Review confronta il contratto dichiarato con il gioco reale; " +
                "un risultato allineato non certifica un'intenzione che non compare nei dati.";
        }
    }

    private static string DirectionLabel(int direction) => direction switch
    {
        1 => "↙",
        2 => "↓",
        3 => "↘",
        4 => "←",
        6 => "→",
        7 => "↖",
        8 => "↑",
        9 => "↗",
        _ => "↓ implicita",
    };

    public string RuntimeTraceStatusText
    {
        get
        {
            if (SelectedRuntimeTrace is null)
            {
                return RuntimeTraces.Count == 0
                    ? "Nessun trace runtime per questa arena"
                    : "Seleziona un trace runtime";
            }
            if (RuntimeTraceAnalysis is null || RuntimeTraceAnalysis.Events.Count == 0)
            {
                return "La sequenza selezionata non compare nel trace";
            }
            var attacks = RuntimeTraceAnalysis.Attacks;
            var aligned = attacks.Count(entry =>
                entry.Status == FusionRuntimeFidelityStatus.Aligned);
            var problems = attacks.Count(entry =>
                entry.Status != FusionRuntimeFidelityStatus.Aligned);
            return problems == 0
                ? $"Runtime · {RuntimeTraceAnalysis.Events.Count} eventi · {aligned} attacchi allineati"
                : $"Runtime · {RuntimeTraceAnalysis.Events.Count} eventi · {problems} scostamenti";
        }
    }

    public string RuntimeTraceErrorsText => _runtimeTraceErrors.Count == 0
        ? string.Empty
        : $"{_runtimeTraceErrors.Count} trace ignorati: {_runtimeTraceErrors[0]}";

    public void ApplyRuntimeTraceLoadResult(FusionRuntimeTraceLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _allRuntimeTraces = result.Traces
            .Select(trace => new FusionRuntimeTraceViewModel(trace))
            .ToArray();
        _runtimeTraceErrors = result.Errors;
        OnPropertyChanged(nameof(RuntimeTraceErrorsText));
        RefreshRuntimeTraceChoices();
    }

    public double PreviewFrame
    {
        get => _previewFrame;
        set
        {
            var normalized = Math.Clamp(value, 0, TimelineMaximumFrame);
            if (Math.Abs(_previewFrame - normalized) < 0.01)
            {
                return;
            }
            _previewFrame = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewFrameText));
        }
    }

    public double TimelineMaximumFrame
    {
        get
        {
            if (SelectedSequence is null)
            {
                return 1;
            }
            var maximum = (double)Math.Max(
                1,
                SelectedSequence.Sequence.MinimumDurationFrames);
            foreach (var step in SelectedSequence.Steps)
            {
                maximum = Math.Max(maximum, CalculateStepEndFrame(step));
            }
            return Math.Ceiling(maximum);
        }
    }

    public int? TimelineFirstInexactFrame => SelectedSequence?.Steps
        .Where(step => !step.Step.IsStartExact)
        .Select(step => (int?)step.Step.EarliestStartFrame)
        .OrderBy(frame => frame)
        .FirstOrDefault();

    public string PreviewFrameText =>
        TimelineFirstInexactFrame is int firstInexactFrame && PreviewFrame >= firstInexactFrame
        ? $"≥ F {PreviewFrame:0.#} / {TimelineMaximumFrame:0}"
        : $"F {PreviewFrame:0.#} / {TimelineMaximumFrame:0}";

    public bool IsArenaPreviewDetached => _isArenaPreviewDetached;

    public bool IsArenaPreviewEmbedded => !_isArenaPreviewDetached;

    public string ArenaPreviewWindowButtonText => _isArenaPreviewDetached
        ? "Mostra finestra"
        : "Apri in una finestra";

    public string StatusText => Document.IsValid
        ? "Contratti validi"
        : $"{Document.ErrorCount} errori";

    public string WarningText => $"{Document.WarningCount} avvisi";

    public string BossCountText => $"{Document.GetRecordCount("bosses")} boss";

    public string EncounterCountText => $"{Document.GetRecordCount("encounters")} encounter";

    public string ArenaCountText => $"{Document.GetRecordCount("arenas")} arene";

    public string WorkspaceCountSummaryText =>
        $"{BossCountText} · {EncounterCountText} · {ArenaCountText}";

    public bool HasDiagnostics => Diagnostics.Count > 0;

    public string DiagnosticsTitle => Diagnostics.Count == 0
        ? "Nessun problema rilevato"
        : $"Diagnostica ({Diagnostics.Count})";

    public string SelectedEncounterSubtitle => SelectedEncounter is null
        ? "Nessun encounter disponibile"
        : $"{SelectedEncounter.BossText} · {SelectedEncounter.PhaseCountText}";

    public string PhaseSummaryText => SelectedPhase is null
        ? "Nessuna fase selezionata"
        : $"{SelectedPhase.TransitionCountText} · {SelectedPhase.MechanicCountText}";

    public string TimelineTitle => SelectedSequence is null
        ? "Timeline"
        : SelectedSequence.DisplayName;

    public string TimelineSubtitle => SelectedSequence is null
        ? "Nessuna sequenza selezionata"
        : $"{SelectedSequence.Id} · {SelectedSequence.ScopeText} · {SelectedSequence.DocumentationStatusText}";

    public string SequenceIntentText => SelectedSequence?.ContextText ?? string.Empty;

    public string TimelineDurationText => SelectedSequence?.DurationText ??
        "Nessuna sequenza nella fase";

    public string TimelineStepListHeader => SelectedSequence is null
        ? "Tutti gli step"
        : $"Tutti gli step ({SelectedSequence.Steps.Count})";

    public string ArenaSummaryText => SelectedArena is null
        ? "Nessuna arena disponibile"
        : $"Encounter {SelectedArena.EncounterId} · {SelectedArena.MapCountText}";

    public string MapSummaryText => SelectedMap?.SummaryText ??
        "Nessuna mappa disponibile";

    private void RefreshRuntimeTraceChoices(bool preferSelectedMap = false)
    {
        var encounterId = SelectedEncounter?.Id;
        var mapId = SelectedMap?.Scene.MapId;
        var previousTraceId = SelectedRuntimeTrace?.Trace.TraceId;
        _runtimeTraces = _allRuntimeTraces
            .Where(item => string.IsNullOrWhiteSpace(encounterId) ||
                item.Trace.EncounterId.Equals(
                    encounterId,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        OnPropertyChanged(nameof(RuntimeTraces));
        var previous = _runtimeTraces.FirstOrDefault(item =>
            item.Trace.TraceId.Equals(previousTraceId, StringComparison.OrdinalIgnoreCase));
        SelectedRuntimeTrace = preferSelectedMap && mapId is not null
            ? previous?.Trace.MapId == mapId
                ? previous
                : _runtimeTraces.FirstOrDefault(item => item.Trace.MapId == mapId)
            : previous ?? _runtimeTraces.FirstOrDefault();
        OnPropertyChanged(nameof(RuntimeTraceStatusText));
    }

    private void AlignArenaToRuntimeTrace()
    {
        var trace = SelectedRuntimeTrace?.Trace;
        if (trace is null)
        {
            return;
        }
        var arena = Arenas.FirstOrDefault(candidate =>
            candidate.EncounterId.Equals(trace.EncounterId, StringComparison.OrdinalIgnoreCase) &&
            candidate.Maps.Any(map => map.Scene.MapId == trace.MapId));
        if (arena is null)
        {
            return;
        }
        SelectedArena = arena;
        var map = arena.Maps.FirstOrDefault(candidate => candidate.Scene.MapId == trace.MapId);
        if (map is not null)
        {
            SelectedMap = map;
        }
    }

    private void RefreshRuntimeTraceAnalysis()
    {
        _runtimeTraceAnalysis = SelectedRuntimeTrace is not null &&
            SelectedSequence is not null &&
            SelectedPhase is not null
                ? _runtimeTraceService.AnalyzeSequence(
                    SelectedSequence.Sequence,
                    SelectedPhase.Id,
                    SelectedRuntimeTrace.Trace)
                : null;
        OnPropertyChanged(nameof(RuntimeTraceAnalysis));
        OnPropertyChanged(nameof(RuntimeTraceStatusText));
        RefreshRuntimeAttackComparisons();
    }

    private void RefreshRuntimeAttackComparisons()
    {
        var previousCastId = SelectedRuntimeAttack?.Comparison.CastId;
        var previousStepIndex = SelectedRuntimeAttack?.Comparison.StepIndex;
        var previousCastIndex = SelectedRuntimeAttack?.Comparison.CastIndex;
        var attacks = _runtimeTraceAnalysis?.Attacks ?? [];
        var hasProblems = attacks.Any(comparison =>
            comparison.Status != FusionRuntimeFidelityStatus.Aligned);
        _runtimeAttackComparisons = attacks
            .Where(comparison => ShowAlignedRuntimeAttacks || !hasProblems ||
                comparison.Status != FusionRuntimeFidelityStatus.Aligned)
            .OrderBy(comparison => RuntimeStatusSortOrder(comparison.Status))
            .ThenBy(comparison => comparison.ExpectedExecutionFrame)
            .ThenBy(comparison => comparison.CastIndex)
            .Select(comparison => new FusionRuntimeAttackComparisonViewModel(comparison))
            .ToArray();
        OnPropertyChanged(nameof(RuntimeAttackComparisons));
        OnPropertyChanged(nameof(RuntimeAlignedCount));
        OnPropertyChanged(nameof(RuntimeDriftCount));
        OnPropertyChanged(nameof(RuntimeDivergentCount));
        OnPropertyChanged(nameof(RuntimeMissingCount));
        OnPropertyChanged(nameof(RuntimeReviewListHeader));
        OnPropertyChanged(nameof(RuntimeReviewSummaryText));
        OnPropertyChanged(nameof(RuntimeReviewEmptyText));
        SelectedRuntimeAttack = _runtimeAttackComparisons.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(previousCastId) &&
            item.Comparison.CastId.Equals(previousCastId, StringComparison.OrdinalIgnoreCase)) ??
            _runtimeAttackComparisons.FirstOrDefault(item =>
                item.Comparison.StepIndex == previousStepIndex &&
                item.Comparison.CastIndex == previousCastIndex) ??
            _runtimeAttackComparisons.FirstOrDefault();
    }

    private int RuntimeStatusCount(FusionRuntimeFidelityStatus status) =>
        _runtimeTraceAnalysis?.Attacks.Count(comparison => comparison.Status == status) ?? 0;

    private static int RuntimeStatusSortOrder(FusionRuntimeFidelityStatus status) => status switch
    {
        FusionRuntimeFidelityStatus.Missing => 0,
        FusionRuntimeFidelityStatus.Divergent => 1,
        FusionRuntimeFidelityStatus.Drift => 2,
        _ => 3,
    };

    private double CalculateStepEndFrame(FusionBossTimelineStepViewModel step)
    {
        var end = step.Step.EarliestStartFrame + (step.Step.DurationFrames ?? 0);
        if (step.AttackGeometry is not { } attack)
        {
            return end;
        }
        var repeatDelayFrames = attack.RepeatDelayMilliseconds * 60d / 1000d;
        var repeatsEnd = repeatDelayFrames * Math.Max(0, attack.RepeatOnUseCount - 1);
        if (attack.Kind != FusionBossAttackGeometryKind.ProjectileCorridor)
        {
            repeatsEnd += repeatDelayFrames * Math.Max(0, attack.HitRepeatCount - 1);
        }
        var travelFrames = attack.Kind == FusionBossAttackGeometryKind.ProjectileCorridor &&
            attack.Speed > 0
                ? attack.RangeTiles * (SelectedMap?.Scene.TileWidth ?? 48) / attack.Speed
                : 1;
        return step.Step.EarliestStartFrame + attack.ExecutionDelayFrames +
            repeatsEnd + travelFrames;
    }

    private void EditSession_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(FusionBossEditSession.WorkingState) or
            nameof(FusionBossEditSession.IsDirty) or
            nameof(FusionBossEditSession.CanUndo) or
            nameof(FusionBossEditSession.CanRedo))
        {
            RefreshEditorFields();
            OnPropertyChanged(nameof(EditorCanUndo));
            OnPropertyChanged(nameof(EditorCanRedo));
            OnPropertyChanged(nameof(EditorIsDirty));
            OnPropertyChanged(nameof(EditorDirtyText));
        }
    }

    private void RefreshEditorFields()
    {
        var phasePointer = PhasePointer();
        EditorPhaseDisplayName = ReadEditorText(phasePointer, "displayName");
        EditorPhaseSummary = ReadEditorText(phasePointer, "summary");
        EditorPhasePlayerGoal = ReadEditorText(phasePointer, "playerGoal");
        EditorPhaseDesignerIntent = ReadEditorText(phasePointer, "designerIntent");

        var sequencePointer = SequencePointer();
        EditorSequenceDisplayName = ReadEditorText(sequencePointer, "displayName");
        EditorSequenceSummary = ReadEditorText(sequencePointer, "summary");
        EditorSequencePlayerGoal = ReadEditorText(sequencePointer, "playerGoal");
        EditorSequenceDesignerIntent = ReadEditorText(sequencePointer, "designerIntent");

        EditorStepJson = _editSession is not null && StepPointer() is { } stepPointer &&
            _editSession.GetValue(stepPointer) is { } step
                ? step.ToJsonString(EditorJsonOptions)
                : string.Empty;
        OnPropertyChanged(nameof(CanEditSelectedSequence));
        OnPropertyChanged(nameof(CanEditSelectedStep));
        OnPropertyChanged(nameof(EditorSelectionText));
    }

    private FusionBossExecutionNode? FindExecutionNode(FusionBossSequenceViewModel? sequence)
    {
        if (sequence is null)
        {
            return null;
        }
        return ExecutionGraph?.Nodes.FirstOrDefault(node =>
            node.TimelineSourceId?.Equals(sequence.Id, StringComparison.OrdinalIgnoreCase) == true &&
            node.TimelineSourceKind == sequence.Sequence.SourceKind);
    }

    private string ReadEditorText(string? parentPointer, string property)
    {
        if (_editSession is null || parentPointer is null ||
            _editSession.GetValue($"{parentPointer}/{property}") is not JsonValue value ||
            !value.TryGetValue<string>(out var text))
        {
            return string.Empty;
        }
        return text;
    }

    private string? PhasePointer()
    {
        if (SelectedEncounter is null || SelectedPhase is null)
        {
            return null;
        }
        return $"/encounters/{Pointer(SelectedEncounter.Id)}/phases/{Pointer(SelectedPhase.Id)}";
    }

    private string? SequencePointer()
    {
        if (_editSession is null || !CanEditSelectedSequence || PhasePointer() is not { } phasePointer ||
            _editSession.GetValue($"{phasePointer}/sequences") is not JsonArray sequences)
        {
            return null;
        }
        for (var index = 0; index < sequences.Count; index++)
        {
            if (sequences[index] is JsonObject sequence &&
                sequence["id"] is JsonValue idValue &&
                idValue.TryGetValue<string>(out var id) &&
                id.Equals(SelectedSequence!.Id, StringComparison.OrdinalIgnoreCase))
            {
                return $"{phasePointer}/sequences/{index}";
            }
        }
        return null;
    }

    private string? StepPointer()
    {
        if (SelectedTimelineStep is null || PhasePointer() is not { } phasePointer ||
            SelectedSequence is null)
        {
            return null;
        }
        var sourcePointer = SelectedSequence.Sequence.SourceKind switch
        {
            FusionBossTimelineSourceKind.PhaseEnter => $"{phasePointer}/onEnter",
            FusionBossTimelineSourceKind.PhaseExit => $"{phasePointer}/onExit",
            _ => SequencePointer() is { } sequencePointer
                ? $"{sequencePointer}/steps"
                : null,
        };
        return sourcePointer is null
            ? null
            : $"{sourcePointer}/{SelectedTimelineStep.Step.Index}";
    }

    private static string Pointer(string value) =>
        FusionBossEditSession.EscapePointerSegment(value);

    public bool NavigateTo(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var segments = target.AbsolutePath.Trim('/').Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 3 ||
            !segments[0].Equals("encounters", StringComparison.OrdinalIgnoreCase) ||
            !segments[1].Equals("encounters", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var encounterId = Uri.UnescapeDataString(segments[2]);
        var encounter = Encounters.FirstOrDefault(candidate =>
            candidate.Id.Equals(encounterId, StringComparison.OrdinalIgnoreCase));
        if (encounter is null)
        {
            return false;
        }
        SelectedEncounter = encounter;
        return true;
    }

    internal void SetArenaPreviewDetached(bool value)
    {
        if (_isArenaPreviewDetached == value)
        {
            return;
        }
        _isArenaPreviewDetached = value;
        OnPropertyChanged(nameof(IsArenaPreviewDetached));
        OnPropertyChanged(nameof(IsArenaPreviewEmbedded));
        OnPropertyChanged(nameof(ArenaPreviewWindowButtonText));
    }

    internal void PrepareArenaPreviewDetach() =>
        ReattachArenaPreviewOnWindowClose = true;

    public void CloseExternalSurfaces()
    {
        if (_editSession is not null)
        {
            _editSession.PropertyChanged -= EditSession_PropertyChanged;
        }
        ReattachArenaPreviewOnWindowClose = false;
        ExternalSurfacesCloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class FusionRuntimeTraceViewModel
{
    public FusionRuntimeTraceViewModel(FusionRuntimeTrace trace)
    {
        Trace = trace;
    }

    public FusionRuntimeTrace Trace { get; }

    public string DisplayName
    {
        get
        {
            var timestamp = Trace.StartedAtUtc?.ToLocalTime().ToString("dd/MM HH:mm:ss") ??
                "orario sconosciuto";
            return $"{timestamp} · {CaptureProfileText} · map {Trace.MapId:000}";
        }
    }

    public string CaptureProfileText => Trace.CaptureProfile.ToLowerInvariant() switch
    {
        "balanced" => "Balanced",
        "compact" => "Compact",
        "custom" => "Custom",
        _ => "Full",
    };

    public string DetailText =>
        $"{Trace.EventCount} eventi · {CaptureProfileText} · campione ogni " +
        $"{Math.Max(1, Trace.SampleEveryFrames)}f · {Trace.StopReason}";
}

public sealed class FusionRuntimeAttackComparisonViewModel
{
    public FusionRuntimeAttackComparisonViewModel(FusionRuntimeAttackComparison comparison)
    {
        Comparison = comparison;
    }

    public FusionRuntimeAttackComparison Comparison { get; }

    public double FocusFrame => Comparison.RuntimeColliderFrame ??
        Comparison.RuntimeExecutionFrame ??
        Comparison.RuntimeTelegraphFrame ??
        Comparison.ExpectedExecutionFrame;

    public string StatusGlyph => Comparison.Status switch
    {
        FusionRuntimeFidelityStatus.Aligned => "✓",
        FusionRuntimeFidelityStatus.Drift => "≈",
        FusionRuntimeFidelityStatus.Divergent => "!",
        _ => "?",
    };

    public string StatusText => Comparison.Status switch
    {
        FusionRuntimeFidelityStatus.Aligned => "ALLINEATO",
        FusionRuntimeFidelityStatus.Drift => "DRIFT",
        FusionRuntimeFidelityStatus.Divergent => "DIVERGENTE",
        _ => "MANCANTE",
    };

    public string SkillText => $"Skill #{Comparison.SkillId} · cast {Comparison.CastIndex + 1}";

    public string StepText => $"Step {Comparison.StepIndex + 1:00}";

    public string FrameComparisonText =>
        $"Previsto {(Comparison.IsExpectedExecutionExact ? "F" : "≥ F")} " +
        $"{Comparison.ExpectedExecutionFrame:0.##} → " +
        (Comparison.RuntimeExecutionFrame is { } frame
            ? $"runtime F {frame:0.##}"
            : "runtime non rilevato");

    public string Summary => Comparison.Summary;

    public string TimingDetailText => string.Join(
        " · ",
        new[]
        {
            FormatFrame("start previsto", Comparison.ExpectedStartFrame),
            FormatOptionalFrame("richiesta", Comparison.RuntimeStartFrame),
            FormatOptionalFrame("telegraph", Comparison.RuntimeTelegraphFrame),
            FormatOptionalFrame("movimento", Comparison.RuntimeMovementStartFrame),
            FormatOptionalFrame("atterraggio", Comparison.RuntimeMovementCompletedFrame),
            FormatOptionalFrame("commit", Comparison.RuntimeCommitFrame),
            FormatOptionalFrame("esecuzione", Comparison.RuntimeExecutionFrame),
            FormatOptionalFrame("collider", Comparison.RuntimeColliderFrame),
        });

    public string GeometryDetailText
    {
        get
        {
            var parts = new List<string>();
            AddDelta(parts, "centro", Comparison.TelegraphColliderOffsetTiles, "tile");
            AddPair(
                parts,
                "raggio",
                Comparison.ExpectedRadiusTiles,
                Comparison.RuntimeRadiusTiles,
                "tile");
            AddPair(
                parts,
                "collider",
                Comparison.ExpectedColliderRadiusPixels,
                Comparison.RuntimeColliderRadiusPixels,
                "px");
            AddPair(
                parts,
                "quantità collider",
                Comparison.ExpectedColliderCount,
                Comparison.RuntimeColliderCount,
                string.Empty);
            AddDelta(parts, "percorso", Comparison.ColliderPathOffsetTiles, "tile");
            return parts.Count == 0
                ? "Nessuna misura geometrica disponibile per questo cast."
                : string.Join(" · ", parts);
        }
    }

    public string IdentifierText => string.IsNullOrWhiteSpace(Comparison.CastId)
        ? $"run {FallbackIdentifier(Comparison.SequenceRunId)}"
        : $"cast {Comparison.CastId} · run {FallbackIdentifier(Comparison.SequenceRunId)}";

    private static string FormatFrame(string label, double frame) =>
        $"{label} F {frame:0.##}";

    private static string FormatOptionalFrame(string label, double? frame) =>
        frame is { } value ? FormatFrame(label, value) : $"{label} —";

    private static void AddDelta(
        ICollection<string> parts,
        string label,
        double? value,
        string unit)
    {
        if (value is not null)
        {
            parts.Add($"{label} Δ {value:+0.###;-0.###;0} {unit}");
        }
    }

    private static void AddPair(
        ICollection<string> parts,
        string label,
        double? expected,
        double? runtime,
        string unit)
    {
        if (expected is null && runtime is null)
        {
            return;
        }
        var expectedText = expected is { } expectedValue ? $"{expectedValue:0.###}" : "—";
        var runtimeText = runtime is { } runtimeValue ? $"{runtimeValue:0.###}" : "—";
        parts.Add($"{label} {expectedText} → {runtimeText} {unit}");
    }

    private static string FallbackIdentifier(string value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}

public sealed class FusionBossArenaViewModel
{
    public FusionBossArenaViewModel(FusionBossArenaDefinition arena)
    {
        Arena = arena;
        Maps = arena.Maps.Select(map => new FusionBossMapSceneViewModel(map)).ToArray();
    }

    public FusionBossArenaDefinition Arena { get; }

    public IReadOnlyList<FusionBossMapSceneViewModel> Maps { get; }

    public string Id => Arena.Id;

    public string DisplayName => Arena.DisplayName;

    public string EncounterId => Arena.EncounterId;

    public string MapCountText => $"{Maps.Count} mappe";
}

public sealed class FusionBossMapSceneViewModel
{
    public FusionBossMapSceneViewModel(FusionBossMapScene scene)
    {
        Scene = scene;
    }

    public FusionBossMapScene Scene { get; }

    public string DisplayName => $"#{Scene.MapId:000} · {Scene.DisplayName}";

    public string SummaryText =>
        $"{Scene.Width}×{Scene.Height} tile · {Scene.Markers.Count(marker => marker.Kind == FusionBossMapMarkerKind.Anchor)} anchor · {Scene.Markers.Count(marker => marker.Kind == FusionBossMapMarkerKind.Role)} ruoli · {Scene.TilesetName}";
}

public sealed class FusionBossEncounterViewModel
{
    public FusionBossEncounterViewModel(FusionBossEncounterDefinition encounter)
    {
        Encounter = encounter;
        Phases = encounter.Phases
            .Select(phase => new FusionBossPhaseViewModel(phase))
            .ToArray();
    }

    public FusionBossEncounterDefinition Encounter { get; }

    public IReadOnlyList<FusionBossPhaseViewModel> Phases { get; }

    public string Id => Encounter.Id;

    public string DisplayName => Encounter.DisplayName;

    public string BossText => $"Boss: {Encounter.BossId}";

    public string PhaseCountText => $"{Phases.Count} fasi";
}

public sealed class FusionBossPhaseViewModel
{
    public FusionBossPhaseViewModel(FusionBossPhaseDefinition phase)
    {
        Phase = phase;
        ExecutionGraph = new FusionBossExecutionGraphService().Build(phase);
        Sequences = BuildTimelineSources(phase)
            .Select(sequence => new FusionBossSequenceViewModel(sequence))
            .ToArray();
    }

    public FusionBossPhaseDefinition Phase { get; }

    public FusionBossExecutionGraph ExecutionGraph { get; }

    public IReadOnlyList<FusionBossSequenceViewModel> Sequences { get; }

    public string Id => Phase.Id;

    public string DisplayName => string.IsNullOrWhiteSpace(Phase.DisplayName)
        ? Phase.Id
        : Phase.DisplayName;

    public string TechnicalIdText => Phase.Id;

    public string SummaryText => string.IsNullOrWhiteSpace(Phase.Summary)
        ? "Descrizione specifica non aggiunta: la struttura resta documentata automaticamente dagli step."
        : Phase.Summary;

    public string PlayerGoalText => string.IsNullOrWhiteSpace(Phase.PlayerGoal)
        ? "Obiettivo giocatore non specificato."
        : $"Obiettivo: {Phase.PlayerGoal}";

    public string DesignerIntentText => string.IsNullOrWhiteSpace(Phase.DesignerIntent)
        ? string.Empty
        : $"Intento: {Phase.DesignerIntent}";

    public string DocumentationStatusText => string.IsNullOrWhiteSpace(Phase.Summary) &&
        string.IsNullOrWhiteSpace(Phase.PlayerGoal) &&
        string.IsNullOrWhiteSpace(Phase.DesignerIntent)
            ? "SOLO AUTO-DOCUMENTAZIONE"
            : "CONTESTO DOCUMENTATO";

    public string ContextText => string.Join(
        Environment.NewLine,
        new[] { SummaryText, PlayerGoalText, DesignerIntentText }
            .Where(text => !string.IsNullOrWhiteSpace(text)));

    public bool IsInitial => Phase.IsInitial;

    public string InitialMarker => IsInitial ? "INIZIALE" : string.Empty;

    public string SequenceCountText => $"{Phase.Sequences.Count} sequenze";

    public string HookStepCountText
    {
        get
        {
            var count = Phase.OnEnterSteps.Count + Phase.OnExitSteps.Count;
            return count == 1 ? "1 azione fase" : $"{count} azioni fase";
        }
    }

    public string HookSummaryText =>
        $"Ingresso: {Phase.OnEnterSteps.Count} · uscita: {Phase.OnExitSteps.Count}";

    public string TransitionCountText => $"{Phase.Transitions.Count} transizioni";

    public string MechanicCountText => $"{Phase.Mechanics.Count} meccaniche";

    public string MechanicsText => Phase.Mechanics.Count == 0
        ? "Nessuna meccanica"
        : string.Join(", ", Phase.Mechanics);

    public string TransitionDetailText => Phase.Transitions.Count == 0
        ? "Nessuna transizione in uscita"
        : string.Join(
            Environment.NewLine,
            Phase.Transitions.Select(transition =>
                $"→ {transition.TargetPhaseId} · {transition.ConditionSummary}"));

    private static IEnumerable<FusionBossSequenceDefinition> BuildTimelineSources(
        FusionBossPhaseDefinition phase)
    {
        if (phase.OnEnterSteps.Count > 0)
        {
            yield return new FusionBossSequenceDefinition
            {
                Id = "onEnter",
                DisplayName = "Ingresso fase",
                Summary = "Azioni eseguite una volta quando la fase diventa attiva.",
                Scope = "hook di fase",
                SourceKind = FusionBossTimelineSourceKind.PhaseEnter,
                Steps = phase.OnEnterSteps,
            };
        }

        foreach (var sequence in phase.Sequences)
        {
            yield return sequence;
        }

        if (phase.OnExitSteps.Count > 0)
        {
            yield return new FusionBossSequenceDefinition
            {
                Id = "onExit",
                DisplayName = "Uscita fase",
                Summary = "Azioni eseguite una volta prima di lasciare la fase.",
                Scope = "hook di fase",
                SourceKind = FusionBossTimelineSourceKind.PhaseExit,
                Steps = phase.OnExitSteps,
            };
        }
    }
}

public sealed class FusionBossSequenceViewModel
{
    public FusionBossSequenceViewModel(FusionBossSequenceDefinition sequence)
    {
        Sequence = sequence;
        Steps = sequence.Steps
            .Select(step => new FusionBossTimelineStepViewModel(step))
            .ToArray();
    }

    public FusionBossSequenceDefinition Sequence { get; }

    public IReadOnlyList<FusionBossTimelineStepViewModel> Steps { get; }

    public string Id => Sequence.Id;

    public string DisplayName => string.IsNullOrWhiteSpace(Sequence.DisplayName)
        ? Sequence.Id
        : Sequence.DisplayName;

    public string TechnicalIdText => Sequence.Id;

    public string SummaryText => string.IsNullOrWhiteSpace(Sequence.Summary)
        ? "La sequenza e' spiegata automaticamente dai suoi step."
        : Sequence.Summary;

    public string PlayerGoalText => string.IsNullOrWhiteSpace(Sequence.PlayerGoal)
        ? string.Empty
        : $"Obiettivo giocatore: {Sequence.PlayerGoal}";

    public string DesignerIntentText => string.IsNullOrWhiteSpace(Sequence.DesignerIntent)
        ? string.Empty
        : $"Intento: {Sequence.DesignerIntent}";

    public string DocumentationStatusText => string.IsNullOrWhiteSpace(Sequence.Summary) &&
        string.IsNullOrWhiteSpace(Sequence.PlayerGoal) &&
        string.IsNullOrWhiteSpace(Sequence.DesignerIntent)
            ? "AUTO"
            : Sequence.SourceKind == FusionBossTimelineSourceKind.Sequence
                ? "DOCUMENTATA"
                : "HOOK DI FASE";

    public string ContextText => string.Join(
        Environment.NewLine,
        new[] { SummaryText, PlayerGoalText, DesignerIntentText }
            .Where(text => !string.IsNullOrWhiteSpace(text)));

    public string ScopeText => Sequence.SourceKind switch
    {
        FusionBossTimelineSourceKind.PhaseEnter => "eseguito all'ingresso",
        FusionBossTimelineSourceKind.PhaseExit => "eseguito all'uscita",
        _ => Sequence.AutoStart
            ? $"{Sequence.Scope} · auto"
            : $"{Sequence.Scope} · manuale",
    };

    public string DurationText => Sequence.HasDynamicTiming
        ? $"almeno {Sequence.MinimumDurationFrames} frame · durata dinamica"
        : $"{Sequence.MinimumDurationFrames} frame";

    public string StepCountText => $"{Steps.Count} step";
}

public sealed class FusionBossTimelineStepViewModel
{
    public FusionBossTimelineStepViewModel(FusionBossTimelineStep step)
    {
        Step = step;
    }

    public FusionBossTimelineStep Step { get; }

    public bool HasAttackGeometry => Step.AttackGeometry is not null;

    public FusionBossAttackGeometry? AttackGeometry => Step.AttackGeometry;

    public FusionBossAttackTarget? AttackTarget => Step.AttackTarget;

    public IReadOnlyList<FusionBossAttackTarget> AttackTargets => Step.AttackTargets;

    public string PositionText => $"{Step.Index + 1:00}";

    public string TimeText => Step.IsStartExact
        ? $"F {Step.EarliestStartFrame:0000}"
        : $"≥ F {Step.EarliestStartFrame:0000}";

    public string KindText => Step.Kind switch
    {
        FusionBossTimelineStepKind.Action => "AZIONE",
        FusionBossTimelineStepKind.Wait => "ATTESA",
        FusionBossTimelineStepKind.WaitUntil => "SYNC",
        FusionBossTimelineStepKind.Sequence => "SEQUENZA",
        FusionBossTimelineStepKind.RepeatSequence => "LOOP",
        FusionBossTimelineStepKind.Guard => "GUARDIA",
        _ => "ALTRO",
    };

    public string Label => Step.AttackGeometry is { } attack
        ? $"{Step.Label} · Skill #{attack.SkillId} {attack.SkillName}"
        : Step.ReferencedSequenceId is null
            ? Step.Label
            : $"{Step.Label}: {Step.ReferencedSequenceId}";

    public string Detail => Step.Detail;

    public string TechnicalId => Step.TechnicalId;

    public string TechnicalText => string.IsNullOrWhiteSpace(Step.TechnicalDetail)
        ? Step.TechnicalId
        : $"{Step.TechnicalId} · {Step.TechnicalDetail}";

    public string CategoryText => string.IsNullOrWhiteSpace(Step.Category)
        ? KindText
        : Step.Category.ToUpperInvariant();

    public string IconGlyph => string.IsNullOrWhiteSpace(Step.IconGlyph) ? "◆" : Step.IconGlyph;

    public bool HasDataFlow => Step.Reads.Count > 0 || Step.Writes.Count > 0;

    public string DataFlowText
    {
        get
        {
            var parts = new List<string>();
            if (Step.Reads.Count > 0)
            {
                parts.Add($"usa {string.Join(", ", Step.Reads)}");
            }
            if (Step.Writes.Count > 0)
            {
                parts.Add($"produce {string.Join(", ", Step.Writes)}");
            }
            return string.Join(" · ", parts);
        }
    }

    public string DurationText => Step.AttackLifecycleStage.Equals(
        "prepare",
        StringComparison.OrdinalIgnoreCase)
        ? $"telegraph → step {Step.LinkedAttackStepIndex.GetValueOrDefault() + 1:00}"
        : Step.AttackLifecycleStage.Equals("commit", StringComparison.OrdinalIgnoreCase)
            ? "impatto immediato"
        : Step.AttackGeometry is { } attack
            ? attack.RepeatOnUseCount > 1
            ? $"+{attack.ExecutionDelayFrames}f · ×{attack.RepeatOnUseCount}"
            : $"+{attack.ExecutionDelayFrames}f"
        : Step.DurationFrames is { } duration
            ? $"{duration}f"
            : Step.TimeoutFrames is { } timeout ? $"timeout {timeout}f" : "istantaneo";

    public double BarWidth => Step.DurationFrames is { } duration
        ? Math.Clamp(duration * 0.55, 14, 220)
        : Step.Kind is FusionBossTimelineStepKind.WaitUntil or
            FusionBossTimelineStepKind.Sequence or
            FusionBossTimelineStepKind.RepeatSequence
                ? 84
                : 14;
}

public sealed class FusionBossDatabaseViewModel
{
    public FusionBossDatabaseViewModel(FusionBossDatabaseSummary database)
    {
        Database = database;
    }

    public FusionBossDatabaseSummary Database { get; }

    public string DisplayName => Database.DisplayName;

    public string StatusGlyph => Database.Exists ? "●" : "○";

    public string VersionText => Database.Exists
        ? $"schema {Database.SchemaVersion?.ToString() ?? "?"} · db {Database.DatabaseVersion ?? "?"}"
        : "File mancante";

    public string RecordCountText => $"{Database.RecordCount} record";

    public string CollectionsText => Database.Collections.Count == 0
        ? "Nessuna collezione disponibile"
        : string.Join(
            " · ",
            Database.Collections.Select(collection =>
                $"{collection.DisplayName}: {collection.RecordIds.Count}"));

    public string SourceFileName => Path.GetFileName(Database.SourcePath);
}

public sealed class FusionBossPluginViewModel
{
    public FusionBossPluginViewModel(FusionBossPluginStatus plugin)
    {
        Plugin = plugin;
    }

    public FusionBossPluginStatus Plugin { get; }

    public string DisplayName => Plugin.DisplayName;

    public string StatusGlyph => Plugin.IsActive ? "●" : "○";

    public string StatusText => Plugin.IsActive
        ? "Attivo"
        : Plugin.IsRequired ? "Richiesto" : "Non attivo";
}

public sealed class FusionBossDiagnosticViewModel
{
    public FusionBossDiagnosticViewModel(FusionBossDiagnostic diagnostic)
    {
        Diagnostic = diagnostic;
    }

    public FusionBossDiagnostic Diagnostic { get; }

    public string Glyph => Diagnostic.Severity switch
    {
        FusionBossDiagnosticSeverity.Error => "●",
        FusionBossDiagnosticSeverity.Warning => "▲",
        _ => "○",
    };

    public string Message => Diagnostic.Message;

    public string ContextText
    {
        get
        {
            var parts = new[]
            {
                Diagnostic.Database?.ToString(),
                Diagnostic.CollectionId,
                Diagnostic.RecordId,
                Diagnostic.Code,
            }.Where(value => !string.IsNullOrWhiteSpace(value));
            return string.Join(" · ", parts);
        }
    }
}
