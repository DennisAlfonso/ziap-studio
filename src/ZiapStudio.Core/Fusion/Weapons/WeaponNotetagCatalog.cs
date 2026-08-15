namespace ZiapStudio.Core.Fusion.Weapons;

public sealed record WeaponNotetagCatalog
{
    public IReadOnlyList<WeaponPerkOption> Perks { get; init; } = [];

    public IReadOnlyList<WeaponLoreOption> LoreEntries { get; init; } = [];

    public IReadOnlyList<WeaponDisassemblyResourceOption> DisassemblyResources { get; init; } = [];

    public IReadOnlyList<WeaponCustomParameterOption> CustomParameters { get; init; } =
        [new(1, "Maestria Hex")];

    public static WeaponNotetagCatalog Empty { get; } = new()
    {
        Perks = Enumerable.Range(1, 3)
            .SelectMany(column => new[]
            {
                new WeaponPerkOption("random", "Casuale", column, column switch
                {
                    1 => 5,
                    2 => 15,
                    _ => 30,
                }),
                new WeaponPerkOption("nullo", "Nullo", column, 0),
            })
            .ToArray(),
    };
}

public sealed record WeaponPerkOption(string Id, string DisplayName, int Column, int Level);

public sealed record WeaponLoreOption(string Key, string Title, int Id);

public sealed record WeaponDisassemblyResourceOption(string RawValue, string DisplayName);

public sealed record WeaponCustomParameterOption(int Id, string DisplayName);
