namespace ZiapStudio.Core.Fusion.Weapons;

public static class WeaponAuthoringSchema
{
    public static IReadOnlyList<WeaponFamilyDefinition> Families { get; } =
    [
        new("sword", "Spada", "Swords", "sword", 1, "sword"),
        new("rapier", "Stocco", "Rapiers", "rapier", 1, "rapier"),
        new("dagger", "Pugnale", "Daggers", "dagger", 1, "dagger"),
        new("twinblade", "Doppia lama", "Twinblades", "twinblade", 2, "twinblade"),
        new("axe", "Ascia", "Axes", "axe", 2, "axe"),
        new("firearm", "Arma da fuoco", "Firearms", "pistol", 1, "firearm"),
    ];

    public static IReadOnlyList<WeaponSubtypeDefinition> FirearmSubtypes { get; } =
    [
        new("pistol", "Pistola"),
        new("revolver", "Revolver"),
        new("smg", "Mitraglietta"),
        new("rifle", "Fucile"),
        new("shotgun", "Fucile a pompa"),
        new("sniper", "Fucile di precisione"),
        new("launcher", "Lanciatore"),
    ];

    public static WeaponFamilyDefinition? FindFamily(string? id) =>
        Families.FirstOrDefault(family => family.Id.Equals(
            id?.Trim(),
            StringComparison.OrdinalIgnoreCase));

    public static bool IsKnownFirearmSubtype(string? id) =>
        FirearmSubtypes.Any(subtype => subtype.Id.Equals(
            id?.Trim(),
            StringComparison.OrdinalIgnoreCase));
}

public sealed record WeaponFamilyDefinition(
    string Id,
    string DisplayName,
    string Category,
    string DefaultSubtype,
    int DefaultHandedness,
    string EditorProfileId)
{
    public bool IsFirearm => EditorProfileId.Equals("firearm", StringComparison.OrdinalIgnoreCase);
}

public sealed record WeaponSubtypeDefinition(string Id, string DisplayName);

public sealed record WeaponCreationDraft
{
    public string Name { get; init; } = "Nuova arma";

    public string Description { get; init; } = string.Empty;

    public string Family { get; init; } = "sword";

    public string Subtype { get; init; } = "sword";

    public int Handedness { get; init; } = 1;

    public int WeaponTypeId { get; init; } = 1;

    public int EquipTypeId { get; init; } = 1;

    public int IconIndex { get; init; }

    public int AnimationId { get; init; }

    public int Price { get; init; }

    public int Power { get; init; } = 10;

    public int AttackSkillId { get; init; } = 1;

    public int AttackElementId { get; init; } = 1;

    public int Rarity { get; init; }

    public int RequiredLevel { get; init; } = 1;

    public IReadOnlyList<string> Perks { get; init; } = ["random", "random", "nullo"];

    public double DamageRate { get; init; } = 1;

    public double FlatDamage { get; init; }

    public double DefenseRate { get; init; } = 1;

    public double AttackInterval { get; init; } = 1.2;

    public double AttackRange { get; init; } = 1;

    public double AttackRadius { get; init; } = 0.35;

    public double ProjectileSpeed { get; init; }

    public double ProjectileColliderRadius { get; init; } = 8;

    public bool OverrideDamageFormula { get; init; }

    public bool OverrideAttackBehavior { get; init; }

    public int MagazineSize { get; init; } = 12;

    public double ReloadDuration { get; init; } = 1.4;

    public double Accuracy { get; init; } = 65;

    public double Stability { get; init; } = 55;

    public double Handling { get; init; } = 80;

    public double AimMinimumDistance { get; init; } = 4;

    public double AimMaximumDistance { get; init; } = 7;

    public double AimMovementMultiplier { get; init; } = 0.25;

    public string DisassemblyResource { get; init; } = "{db[0].partiArmamento}";

    public int DisassemblyMinimum { get; init; } = 1;

    public int DisassemblyMaximum { get; init; } = 2;

    public static WeaponCreationDraft Firearm(string name, string description = "") => new()
    {
        Name = name,
        Description = description,
        Family = "firearm",
        Subtype = "pistol",
        Handedness = 1,
        WeaponTypeId = 3,
        AttackSkillId = 343,
        AttackInterval = 0.35,
        AttackRange = 10,
        AttackRadius = 0.2,
        ProjectileSpeed = 8,
        ProjectileColliderRadius = 8,
    };
}
