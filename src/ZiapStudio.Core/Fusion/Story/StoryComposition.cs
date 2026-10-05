using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Localization;

namespace ZiapStudio.Core.Fusion.Story;

public enum StoryCompositionOperationType
{
    AddDialogue,
    AddNarration,
}

public enum StoryCommandListKind
{
    MapPage,
    CommonEvent,
}

/// <summary>Stable, JSON-level identity of the sole command list a plan may alter.</summary>
public sealed record StoryCommandListTarget
{
    public required StoryCommandListKind Kind { get; init; }
    public required string SourceFile { get; init; }
    public int? MapId { get; init; }
    public required int EventId { get; init; }
    /// <summary>One-based RPG Maker page number. Null for Common Events.</summary>
    public int? PageNumber { get; init; }
}

public sealed record StoryCommandSignature
{
    public required int Code { get; init; }
    public required int Indent { get; init; }
    public required string ParametersJson { get; init; }
}

public sealed record StoryDialoguePresentation
{
    public string FaceName { get; init; } = string.Empty;
    public int FaceIndex { get; init; }
    public int Background { get; init; }
    public int PositionType { get; init; } = 2;
    public bool IsInherited { get; init; }
}

public sealed record StoryCompositionValues
{
    public string Speaker { get; init; } = string.Empty;
    public required string Text { get; init; }
}

/// <summary>
/// Immutable, previewable intent. It cannot itself mutate a map, common event or
/// Localization file; commit services validate all live state again.
/// </summary>
public sealed record StoryCompositionPlan
{
    public required string OperationId { get; init; }
    public required StoryCompositionOperationType OperationType { get; init; }
    public required StoryCommandListTarget Target { get; init; }
    public required int InsertionCommandIndex { get; init; }
    public required int AnchorStartCommandIndex { get; init; }
    public required int AnchorEndCommandIndex { get; init; }
    public required int Indent { get; init; }
    public required IReadOnlyList<StoryCommandSignature> AnchorCommands { get; init; }
    public required DocumentSourceSnapshot ExpectedSourceSnapshot { get; init; }
    public required LocalizationReferenceOrigin AnchorOrigin { get; init; }
    /// <summary>Array route, ending at the destination array and never its entry.</summary>
    public required IReadOnlyList<LocalizationPathSegment> LocalizationBranchPath { get; init; }
    public required StoryCompositionValues Values { get; init; }
    public required StoryDialoguePresentation Presentation { get; init; }
    public int? ExpectedArrayLength { get; init; }
    public string? RemoteCurrentVersionId { get; init; }
    public string? ExpectedStagingChecksum { get; init; }

    public string LocalizationFile => AnchorOrigin.SourceFile;
    public string Locale => AnchorOrigin.Locale;
    public string Namespace => AnchorOrigin.Namespace;
    public string TemplateName => OperationType == StoryCompositionOperationType.AddDialogue
        ? "dialogue" : "narration";
    public string ExpectedAppendText => ExpectedArrayLength is int index
        ? $"expected index {index}" : "authoritative index assigned by server";
}

public sealed record StoryCompositionPreview
{
    public required StoryCompositionPlan Plan { get; init; }
    public required IReadOnlyList<StoryGeneratedCommand> Commands { get; init; }
    public string LocalizationSummary =>
        $"{Plan.LocalizationFile} · append {Plan.TemplateName} at {Plan.ExpectedAppendText}";
    public string EventSummary =>
        $"{Plan.Target.SourceFile} · after command {Plan.AnchorEndCommandIndex} · indent {Plan.Indent}";
}

public sealed record StoryGeneratedCommand
{
    public required int Code { get; init; }
    public required int Indent { get; init; }
    public required IReadOnlyList<object?> Parameters { get; init; }
}

public enum StoryCompositionState
{
    Planned,
    RemoteAppended,
    LocalMirrorSynchronized,
    SourceConflict,
    Completed,
    Dismissed,
}

/// <summary>Persisted only outside the game repository to recover a partial commit.</summary>
public sealed record StoryCompositionRecoveryRecord
{
    public required string ProjectId { get; init; }
    public required StoryCompositionPlan Plan { get; init; }
    public required StoryCompositionState State { get; init; }
    public int? AssignedIndex { get; init; }
    public IReadOnlyList<LocalizationPathSegment> AssignedEntryPath { get; init; } = [];
    public string? CanonicalLocalizationContent { get; init; }
    /// <summary>Fingerprint captured before the remote mutation; prevents a recovery from overwriting a changed mirror.</summary>
    public DocumentSourceSnapshot? ExpectedLocalizationMirrorSnapshot { get; init; }
    public string? CurrentVersionId { get; init; }
    public string? StagingChecksum { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
