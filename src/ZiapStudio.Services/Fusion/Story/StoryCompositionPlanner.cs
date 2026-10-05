using System.Text.Json;
using System.Text.Json.Nodes;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Localization;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Fusion.Story;

/// <summary>Creates safe, immutable composition intent from a parsed Story block.</summary>
public sealed class StoryCompositionPlanner
{
    private readonly DocumentSnapshotService _snapshotService;

    public StoryCompositionPlanner(DocumentSnapshotService snapshotService)
    {
        _snapshotService = snapshotService;
    }

    public async Task<StoryCompositionPlan> CreateAsync(
        StoryCompositionOperationType operationType,
        StoryCommandListTarget target,
        StoryBlock anchor,
        string projectPath,
        string speaker,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(anchor);
        ValidateText(operationType, speaker, text);
        if (anchor.CommandStartIndex < 0 || anchor.CommandEndIndex < anchor.CommandStartIndex)
        {
            throw new StoryCompositionValidationException("L'anchor non possiede un command range sicuro.");
        }
        if (anchor.SourceCommands.Count == 0)
        {
            throw new StoryCompositionValidationException("L'anchor non conserva i command sorgente necessari.");
        }
        var origin = InferAnchorOrigin(anchor);
        var branch = InferBranch(anchor, origin);
        var sourcePath = GetSafeSourcePath(projectPath, target.SourceFile);
        var snapshot = await _snapshotService.CaptureAsync(sourcePath, cancellationToken);
        var presentation = GetPresentation(anchor);
        var signatures = anchor.SourceCommands.Select(command => new StoryCommandSignature
        {
            Code = command.Code,
            Indent = command.Indent,
            ParametersJson = command.Parameters,
        }).ToArray();
        if (signatures.Length != anchor.CommandEndIndex - anchor.CommandStartIndex + 1)
        {
            throw new StoryCompositionValidationException("Il command range dell'anchor non è conservato integralmente.");
        }

        return new StoryCompositionPlan
        {
            // Canonical UUID form is also enforced by the backend receipt store.
            OperationId = Guid.NewGuid().ToString(),
            OperationType = operationType,
            Target = target,
            InsertionCommandIndex = anchor.CommandEndIndex + 1,
            AnchorStartCommandIndex = anchor.CommandStartIndex,
            AnchorEndCommandIndex = anchor.CommandEndIndex,
            Indent = anchor.Indent,
            AnchorCommands = signatures,
            ExpectedSourceSnapshot = snapshot,
            AnchorOrigin = origin,
            LocalizationBranchPath = branch,
            Values = new StoryCompositionValues
            {
                Speaker = operationType == StoryCompositionOperationType.AddDialogue ? speaker.Trim() : string.Empty,
                Text = text,
            },
            Presentation = presentation,
        };
    }

    public StoryCompositionPlan AttachRemoteBase(
        StoryCompositionPlan plan,
        string remoteContent,
        string? currentVersionId,
        string? stagingChecksum)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var node = JsonNode.Parse(remoteContent) ??
            throw new StoryCompositionValidationException("Lo snapshot Localization remoto non è JSON.");
        var branch = NavigateBranch(node, plan.LocalizationBranchPath);
        if (branch is not JsonArray array)
        {
            throw new StoryCompositionValidationException("La destination Localization non punta a un array esistente.");
        }
        return plan with
        {
            ExpectedArrayLength = array.Count,
            RemoteCurrentVersionId = currentVersionId,
            ExpectedStagingChecksum = stagingChecksum,
        };
    }

    public StoryCompositionPreview CreatePreview(StoryCompositionPlan plan) => new()
    {
        Plan = plan,
        Commands = StoryRpgMakerCommandGenerator.CreatePreviewCommands(plan),
    };

    private static LocalizationReferenceOrigin InferAnchorOrigin(StoryBlock anchor)
    {
        var candidates = anchor.LocalizationOrigins
            .Where(origin => origin.Locale.Equals(LocalizationService.DefaultLocale, StringComparison.OrdinalIgnoreCase) &&
                origin.Segments.Count >= 2 &&
                origin.Segments[^1].PropertyName is "text" or "name")
            .ToArray();
        var textOrigin = candidates.FirstOrDefault(origin => origin.Segments[^1].PropertyName == "text");
        if (textOrigin is null)
        {
            throw new StoryCompositionValidationException(
                "L'anchor deve contenere una reference Localization master .text strutturata.");
        }
        var entryPath = textOrigin.Segments.Take(textOrigin.Segments.Count - 1).ToArray();
        if (entryPath[^1].ArrayIndex is not int)
        {
            throw new StoryCompositionValidationException("La reference .text non appartiene a un entry array MDV.");
        }
        var nameOrigin = candidates.FirstOrDefault(origin => origin.Segments[^1].PropertyName == "name");
        if (nameOrigin is not null && !nameOrigin.Segments.Take(nameOrigin.Segments.Count - 1).SequenceEqual(entryPath))
        {
            throw new StoryCompositionValidationException(
                "Speaker e testo dell'anchor non appartengono allo stesso entry Localization.");
        }
        return textOrigin;
    }

    private static IReadOnlyList<LocalizationPathSegment> InferBranch(
        StoryBlock anchor,
        LocalizationReferenceOrigin textOrigin)
    {
        var entryPath = textOrigin.Segments.Take(textOrigin.Segments.Count - 1).ToArray();
        var branch = entryPath.Take(entryPath.Length - 1).ToArray();
        if (branch.Length == 0 || branch.Any(segment =>
                segment.PropertyName is { Length: 0 } || segment.ArrayIndex is < 0 ||
                segment.PropertyName is not null && segment.ArrayIndex is not null))
        {
            throw new StoryCompositionValidationException("La destination Localization non è sicura.");
        }
        // A dialogue with name/text has already been verified in InferAnchorOrigin.
        return branch;
    }

    private static StoryDialoguePresentation GetPresentation(StoryBlock anchor)
    {
        if (anchor.Kind != StoryBlockKind.Dialogue || anchor.SourceCommands.FirstOrDefault() is not { Code: 101 } command)
        {
            return new StoryDialoguePresentation();
        }
        try
        {
            using var document = JsonDocument.Parse(command.Parameters);
            var values = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToArray() : [];
            return new StoryDialoguePresentation
            {
                FaceName = values.ElementAtOrDefault(0).ValueKind == JsonValueKind.String
                    ? values[0].GetString() ?? string.Empty : string.Empty,
                FaceIndex = values.ElementAtOrDefault(1).TryGetInt32(out var faceIndex) ? faceIndex : 0,
                Background = values.ElementAtOrDefault(2).TryGetInt32(out var background) ? background : 0,
                PositionType = values.ElementAtOrDefault(3).TryGetInt32(out var position) ? position : 2,
                IsInherited = true,
            };
        }
        catch (InvalidOperationException)
        {
            return new StoryDialoguePresentation();
        }
    }

    private static JsonNode? NavigateBranch(JsonNode root, IReadOnlyList<LocalizationPathSegment> segments)
    {
        JsonNode? current = root;
        foreach (var segment in segments)
        {
            current = segment.PropertyName is { } property && current is JsonObject @object
                ? @object[property]
                : segment.ArrayIndex is int index && current is JsonArray array && index >= 0 && index < array.Count
                    ? array[index]
                    : null;
            if (current is null)
            {
                throw new StoryCompositionValidationException("La destination Localization non esiste più.");
            }
        }
        return current;
    }

    private static void ValidateText(StoryCompositionOperationType type, string speaker, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100 * 1024)
        {
            throw new StoryCompositionValidationException("Il testo del dialogo è obbligatorio e troppo lungo.");
        }
        if (type == StoryCompositionOperationType.AddDialogue &&
            (string.IsNullOrWhiteSpace(speaker) || speaker.Length > 100 * 1024))
        {
            throw new StoryCompositionValidationException("Lo speaker è obbligatorio per un dialogo.");
        }
    }

    private static string GetSafeSourcePath(string projectPath, string sourceFile)
    {
        var dataRoot = Path.GetFullPath(Path.Combine(projectPath, "data"));
        var candidate = Path.GetFullPath(Path.Combine(projectPath, sourceFile));
        var prefix = dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !sourceFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new StoryCompositionValidationException("La sorgente RPG Maker è fuori dalla cartella data del progetto.");
        }
        return candidate;
    }
}

public sealed class StoryCompositionValidationException(string message) : Exception(message);
