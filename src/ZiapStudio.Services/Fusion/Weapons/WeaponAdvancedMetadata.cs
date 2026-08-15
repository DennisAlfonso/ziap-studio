using ZiapStudio.Core.Fusion.Weapons;

namespace ZiapStudio.Services.Fusion.Weapons;

public sealed record WeaponAdvancedMetadata
{
    public string RawSource { get; init; } = string.Empty;

    public IReadOnlyList<string> Perks { get; init; } = ["random", "random", "nullo"];

    public bool HasPerkTag { get; init; }

    public int? Rarity { get; init; }

    public bool HasRarityTag { get; init; }

    public int? RequiredLevel { get; init; }

    public bool HasRequiredLevelTag { get; init; }

    public int? MaximumLevel { get; init; }

    public bool HasMaximumLevelTag { get; init; }

    public int? AutomaticMaximumLevel => Rarity switch
    {
        0 => 5,
        1 => 10,
        2 => 20,
        3 => 30,
        4 => 40,
        5 => 50,
        6 or 7 => 1,
        _ => null,
    };

    public IReadOnlyList<WeaponCustomParameterMetadata> CustomParameters { get; init; } = [];

    public string? LoreRawValue { get; init; }

    public WeaponLoreOption? ResolvedLore { get; init; }

    public WeaponLoreResolutionKind LoreResolutionKind { get; init; }

    public IReadOnlyList<WeaponDisassemblyResultMetadata> DisassemblyResults { get; init; } = [];

    public bool HasDisassemblyBlock { get; init; }

    public bool HideItemIcon { get; init; }

    public int RecognizedInlineTagCount { get; init; }

    public int RecognizedBlockCount { get; init; }

    public int UnmanagedLineCount { get; init; }

    public IReadOnlyList<WeaponNotetagDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record WeaponCustomParameterMetadata(int Id, int Value);

public sealed record WeaponDisassemblyResultMetadata(
    string RawResource,
    string DisplayName,
    int MinimumQuantity,
    int MaximumQuantity,
    int Probability);

public sealed record WeaponNotetagDiagnostic(
    WeaponNotetagDiagnosticSeverity Severity,
    string Message)
{
    public string? Code { get; init; }
}

public enum WeaponNotetagDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public enum WeaponLoreResolutionKind
{
    None,
    ExactKey,
    CaseInsensitiveKey,
    NumericId,
    LegacyAlias,
    Missing,
}
