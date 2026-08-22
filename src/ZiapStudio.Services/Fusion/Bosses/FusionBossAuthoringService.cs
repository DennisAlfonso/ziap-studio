using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZiapStudio.Core.Editing;
using ZiapStudio.Services.Editing;

namespace ZiapStudio.Services.Fusion.Bosses;

public sealed class FusionBossAuthoringService
{
    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private readonly ExternalModificationDetector _externalModificationDetector;
    private readonly AtomicJsonFileWriter _writer;
    private readonly DocumentSnapshotService _snapshotService;

    public FusionBossAuthoringService(
        ExternalModificationDetector externalModificationDetector,
        AtomicJsonFileWriter writer,
        DocumentSnapshotService snapshotService)
    {
        _externalModificationDetector = externalModificationDetector;
        _writer = writer;
        _snapshotService = snapshotService;
    }

    public DocumentValidationResult Validate(FusionBossEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var issues = new List<DocumentValidationIssue>();
        if (session.WorkingState is not JsonObject root)
        {
            issues.Add(Error("invalid-root", "FusionEncounters.json deve contenere un oggetto JSON."));
            return new DocumentValidationResult { Issues = issues };
        }
        if (root["encounters"] is not JsonObject encounters)
        {
            issues.Add(Error("missing-encounters", "La collezione encounters è obbligatoria.", "/encounters"));
            return new DocumentValidationResult { Issues = issues };
        }

        foreach (var (encounterId, encounterNode) in encounters)
        {
            var encounterPath = $"/encounters/{Pointer(encounterId)}";
            if (encounterNode is not JsonObject encounter)
            {
                issues.Add(Error("invalid-encounter", $"L'encounter '{encounterId}' non è un oggetto.", encounterPath));
                continue;
            }
            if (!HasText(encounter, "boss"))
            {
                issues.Add(Error("missing-boss", $"L'encounter '{encounterId}' non dichiara il boss.", $"{encounterPath}/boss"));
            }
            if (encounter["phases"] is not JsonObject phases)
            {
                issues.Add(Error("missing-phases", $"L'encounter '{encounterId}' non contiene fasi.", $"{encounterPath}/phases"));
                continue;
            }
            var initialPhase = Text(encounter["initialPhase"]);
            if (string.IsNullOrWhiteSpace(initialPhase) || !phases.ContainsKey(initialPhase))
            {
                issues.Add(Error("invalid-initial-phase", $"La fase iniziale '{initialPhase}' non esiste in '{encounterId}'.", $"{encounterPath}/initialPhase"));
            }
            foreach (var (phaseId, phaseNode) in phases)
            {
                ValidatePhase(encounterId, phaseId, phaseNode, issues);
            }
        }
        return new DocumentValidationResult { Issues = issues };
    }

    public async Task<DocumentSaveResult> SaveAsync(
        FusionBossEditSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsDirty)
        {
            return new DocumentSaveResult { Status = DocumentSaveStatus.NoChanges };
        }
        var validation = Validate(session);
        if (!validation.CanSave)
        {
            return new DocumentSaveResult
            {
                Status = DocumentSaveStatus.ValidationFailed,
                Validation = validation,
                Message = "FusionEncounters.json contiene errori che impediscono il salvataggio.",
            };
        }
        try
        {
            if (await _externalModificationDetector.HasChangedAsync(
                session.SourceSnapshot,
                cancellationToken))
            {
                return new DocumentSaveResult
                {
                    Status = DocumentSaveStatus.ExternalModification,
                    Validation = validation,
                    Message = "FusionEncounters.json è stato modificato esternamente.",
                };
            }
            var json = session.WorkingState.ToJsonString(SaveOptions) + Environment.NewLine;
            var bytes = Encoding.UTF8.GetBytes(json);
            await _writer.WriteAsync(
                session.SourceSnapshot.SourcePath,
                bytes,
                session.SourceSnapshot.ContentHash,
                cancellationToken);
            var snapshot = await _snapshotService.CaptureAsync(
                session.SourceSnapshot.SourcePath,
                cancellationToken);
            session.AcceptSavedSnapshot(snapshot);
            return new DocumentSaveResult
            {
                Status = DocumentSaveStatus.Saved,
                Validation = validation,
            };
        }
        catch (ExternalDocumentModificationException exception)
        {
            return new DocumentSaveResult
            {
                Status = DocumentSaveStatus.ExternalModification,
                Validation = validation,
                Message = exception.Message,
                Exception = exception,
            };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new DocumentSaveResult
            {
                Status = DocumentSaveStatus.Failed,
                Validation = validation,
                Message = "Il salvataggio atomico non è riuscito; le modifiche restano in memoria.",
                Exception = exception,
            };
        }
    }

    private static void ValidatePhase(
        string encounterId,
        string phaseId,
        JsonNode? phaseNode,
        ICollection<DocumentValidationIssue> issues)
    {
        var phasePath = $"/encounters/{Pointer(encounterId)}/phases/{Pointer(phaseId)}";
        if (phaseNode is not JsonObject phase)
        {
            issues.Add(Error("invalid-phase", $"La fase '{phaseId}' non è un oggetto.", phasePath));
            return;
        }
        ValidateStepArray(phase["onEnter"], $"{phasePath}/onEnter", optional: true, issues);
        ValidateStepArray(phase["onExit"], $"{phasePath}/onExit", optional: true, issues);
        if (phase["sequences"] is null)
        {
            return;
        }
        if (phase["sequences"] is not JsonArray sequences)
        {
            issues.Add(Error("invalid-sequences", $"Le sequenze della fase '{phaseId}' devono essere un array.", $"{phasePath}/sequences"));
            return;
        }
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < sequences.Count; index++)
        {
            var sequencePath = $"{phasePath}/sequences/{index}";
            if (sequences[index] is not JsonObject sequence)
            {
                issues.Add(Error("invalid-sequence", $"La sequenza #{index + 1} non è un oggetto.", sequencePath));
                continue;
            }
            var sequenceId = Text(sequence["id"]);
            if (string.IsNullOrWhiteSpace(sequenceId))
            {
                issues.Add(Error("missing-sequence-id", "Ogni sequenza deve avere un ID.", $"{sequencePath}/id"));
            }
            else if (!ids.Add(sequenceId))
            {
                issues.Add(Error("duplicate-sequence-id", $"L'ID sequenza '{sequenceId}' è duplicato nella fase '{phaseId}'.", $"{sequencePath}/id"));
            }
            ValidateStepArray(sequence["steps"], $"{sequencePath}/steps", optional: false, issues);
        }
    }

    private static void ValidateStepArray(
        JsonNode? node,
        string path,
        bool optional,
        ICollection<DocumentValidationIssue> issues)
    {
        if (node is null && optional)
        {
            return;
        }
        if (node is not JsonArray steps)
        {
            issues.Add(Error("invalid-step-array", "Il flusso deve essere un array di step.", path));
            return;
        }
        var preparedAttacks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < steps.Count; index++)
        {
            var stepPath = $"{path}/{index}";
            var step = steps[index];
            if (step is JsonArray action)
            {
                var actionId = action.Count > 0 ? Text(action[0]) : null;
                if (string.IsNullOrWhiteSpace(actionId))
                {
                    issues.Add(Error("invalid-action", "Uno step azione deve iniziare con un ID testuale.", stepPath));
                }
                else if (actionId.Equals("wait", StringComparison.OrdinalIgnoreCase) &&
                    (action.Count < 2 || !TryNonNegativeInteger(action[1])))
                {
                    issues.Add(Error("invalid-wait", "wait richiede un numero intero di frame non negativo.", stepPath));
                }
                else if (actionId.Equals("combat.prepareAttack", StringComparison.OrdinalIgnoreCase))
                {
                    var attackId = action.Count > 1 && action[1] is JsonObject prepareConfig
                        ? Text(prepareConfig["attackId"]) ?? Text(prepareConfig["preparedAttackId"])
                        : null;
                    if (string.IsNullOrWhiteSpace(attackId))
                    {
                        issues.Add(Error("missing-attack-id", "combat.prepareAttack richiede attackId.", stepPath));
                    }
                    else if (!preparedAttacks.TryAdd(attackId, index))
                    {
                        issues.Add(Error("duplicate-prepared-attack", $"L'attacco preparato '{attackId}' è già attivo in questa sequenza.", stepPath));
                    }
                }
                else if (actionId.Equals("combat.commitAttack", StringComparison.OrdinalIgnoreCase) ||
                    actionId.Equals("combat.cancelAttack", StringComparison.OrdinalIgnoreCase))
                {
                    var attackId = action.Count > 1 && action[1] is JsonObject commitConfig
                        ? Text(commitConfig["attackId"]) ?? Text(commitConfig["preparedAttackId"])
                        : null;
                    if (string.IsNullOrWhiteSpace(attackId))
                    {
                        issues.Add(Error("missing-attack-id", $"{actionId} richiede attackId.", stepPath));
                    }
                    else if (!preparedAttacks.Remove(attackId))
                    {
                        issues.Add(Error("unmatched-attack-lifecycle", $"L'attacco '{attackId}' viene risolto senza un prepare precedente.", stepPath));
                    }
                }
                continue;
            }
            if (step is JsonObject control &&
                (control.ContainsKey("waitUntil") ||
                    control.ContainsKey("sequence") ||
                    control.ContainsKey("repeatSequence")))
            {
                continue;
            }
            issues.Add(Error("invalid-step", "Lo step deve essere un'azione o un controllo riconosciuto.", stepPath));
        }
        foreach (var (attackId, prepareIndex) in preparedAttacks)
        {
            issues.Add(Error(
                "unresolved-prepared-attack",
                $"L'attacco preparato '{attackId}' non viene né risolto né annullato.",
                $"{path}/{prepareIndex}"));
        }
    }

    private static bool TryNonNegativeInteger(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var result) && result >= 0;

    private static bool HasText(JsonObject value, string property) =>
        !string.IsNullOrWhiteSpace(Text(value[property]));

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string Pointer(string value) => FusionBossEditSession.EscapePointerSegment(value);

    private static DocumentValidationIssue Error(string code, string message, string target) => new()
    {
        Severity = DocumentValidationSeverity.Error,
        Code = code,
        Message = message,
        Target = target,
    };

    private static DocumentValidationIssue Error(string code, string message) =>
        Error(code, message, string.Empty);
}
