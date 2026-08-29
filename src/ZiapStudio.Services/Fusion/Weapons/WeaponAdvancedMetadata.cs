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

    public WeaponClassificationMetadata Classification { get; init; } = new();

    public WeaponCombatProfileMetadata CombatProfile { get; init; } = new();

    public WeaponFirearmMetadata Firearm { get; init; } = new();

    public int RecognizedInlineTagCount { get; init; }

    public int RecognizedBlockCount { get; init; }

    public int UnmanagedLineCount { get; init; }

    public IReadOnlyList<WeaponNotetagDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record WeaponClassificationMetadata
{
    public string? Family { get; init; }

    public string? Subtype { get; init; }

    public int? Handedness { get; init; }

    public bool IsFirearm => Family?.Equals("firearm", StringComparison.OrdinalIgnoreCase) == true;
}

public sealed record WeaponCombatProfileMetadata
{
    public bool HasProfileTag { get; init; }

    public bool Enabled { get; init; }

    public double? DamageRate { get; init; }

    public double? FlatDamage { get; init; }

    public double? DefenseRate { get; init; }

    public double? AttackInterval { get; init; }

    public double? AttackRange { get; init; }

    public double? AttackRadius { get; init; }

    public double? ProjectileSpeed { get; init; }

    public double? ProjectileColliderRadius { get; init; }
}

public sealed record WeaponFirearmMetadata
{
    public int? MagazineSize { get; init; }

    public double? ReloadDuration { get; init; }

    public double? Accuracy { get; init; }

    public double? Stability { get; init; }

    public double? Handling { get; init; }

    public double? AimMinimumDistance { get; init; }

    public double? AimMaximumDistance { get; init; }

    public double? AimMovementMultiplier { get; init; }
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
