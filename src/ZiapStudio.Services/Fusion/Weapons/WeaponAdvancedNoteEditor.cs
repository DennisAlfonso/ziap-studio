using System.Globalization;
using System.Text.RegularExpressions;
using ZiapStudio.Core.Fusion.Weapons;
using ZiapStudio.Core.Notetags;

namespace ZiapStudio.Services.Fusion.Weapons;

public sealed partial class WeaponAdvancedNoteEditor
{
    private readonly NotetagParser _parser;
    private readonly NotetagPatchSerializer _serializer;

    public WeaponAdvancedNoteEditor(
        NotetagParser? parser = null,
        NotetagPatchSerializer? serializer = null)
    {
        _parser = parser ?? new NotetagParser();
        _serializer = serializer ?? new NotetagPatchSerializer(_parser);
    }

    public string SetPerks(string source, IReadOnlyList<string> perks) =>
        _serializer.SetInlineValue(
            source,
            tag => IsNamed(tag, "perk"),
            "perk",
            string.Join(',', perks));

    public string SetRarity(string source, int? rarity) =>
        SetOptionalInteger(source, "itemRare", rarity);

    public string SetRequiredLevel(string source, int level) =>
        SetOptionalInteger(source, "lvReq", level);

    public string SetMaximumLevel(string source, int? level) =>
        SetOptionalInteger(source, "maxLevel", level);

    public string SetLore(string source, string? key) => string.IsNullOrWhiteSpace(key)
        ? _serializer.RemoveInline(source, tag => IsNamed(tag, "loreBook"))
        : _serializer.SetInlineValue(source, tag => IsNamed(tag, "loreBook"), "loreBook", key);

    public string SetHideItemIcon(string source, bool enabled) =>
        _serializer.SetFlag(
            source,
            WeaponAdvancedMetadataProvider.IsHideItemIcon,
            "fhd:no_itemicon",
            enabled);

    public string SetWeaponFamily(string source, string? family) =>
        SetOptionalText(source, "weaponFamily", family);

    public string SetWeaponSubtype(string source, string? subtype) =>
        SetOptionalText(source, "weaponSubtype", subtype);

    public string SetHandedness(string source, int? handedness) =>
        SetOptionalInteger(source, "handedness", handedness);

    public string SetCombatProfileEnabled(string source, bool enabled) =>
        _serializer.SetInlineValue(
            source,
            tag => IsNamed(tag, "weaponCombatProfile"),
            "weaponCombatProfile",
            enabled ? "true" : "false");

    public string SetDamageRate(string source, double value) =>
        SetNumber(source, "attackDamageRate", value);

    public string SetFlatDamage(string source, double value) =>
        SetNumber(source, "attackFlatDamage", value);

    public string SetDefenseRate(string source, double value) =>
        SetNumber(source, "attackDefenseRate", value);

    public string SetAttackInterval(string source, double value) =>
        SetNumber(source, "attackInterval", value);

    public string SetAttackRange(string source, double value) =>
        SetNumber(source, "attackRange", value);

    public string SetAttackRadius(string source, double value) =>
        SetNumber(source, "attackRadius", value);

    public string SetProjectileSpeed(string source, double value) =>
        SetNumber(source, "projectileSpeed", value);

    public string SetProjectileColliderRadius(string source, double value) =>
        SetNumber(source, "projectileColliderRadius", value);

    public string SetMagazineSize(string source, int value) =>
        SetOptionalInteger(source, "magazineSize", value);

    public string SetReloadDuration(string source, double value) =>
        SetNumber(source, "reloadDuration", value);

    public string SetFirearmAccuracy(string source, double value) =>
        SetNumber(source, "firearmAccuracy", value);

    public string SetFirearmStability(string source, double value) =>
        SetNumber(source, "firearmStability", value);

    public string SetFirearmHandling(string source, double value) =>
        SetNumber(source, "firearmHandling", value);

    public string SetFirearmAimMinimumDistance(string source, double? value) =>
        SetOptionalNumber(source, "firearmAimMinDistance", value);

    public string SetFirearmAimMaximumDistance(string source, double? value) =>
        SetOptionalNumber(source, "firearmAimMaxDistance", value);

    public string SetFirearmAimMovementMultiplier(string source, double? value) =>
        SetOptionalNumber(source, "firearmAimMovementMultiplier", value);

    public string SetCategories(string source, IEnumerable<string> categories)
    {
        var body = string.Join(
            DetectNewline(source),
            categories.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(body)
            ? _serializer.RemoveBlock(source, "Categories")
            : _serializer.SetBlockBody(source, "Categories", body);
    }

    public string SetWeaponFamilyCategory(string source, WeaponFamilyDefinition family)
    {
        var block = _parser.Parse(source).BlockTags.FirstOrDefault(node =>
            node.Name.Equals("Categories", StringComparison.OrdinalIgnoreCase));
        var managedCategories = WeaponAuthoringSchema.Families
            .Select(definition => definition.Category)
            .Append("AllWeapons")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var categories = block?.Body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(category => !managedCategories.Contains(category))
            .ToList() ?? [];
        categories.Insert(0, family.Category);
        categories.Insert(0, "AllWeapons");
        return SetCategories(source, categories);
    }

    public string ClearFirearmMetadata(string source)
    {
        var updated = source;
        foreach (var name in new[]
        {
            "magazineSize", "reloadDuration", "firearmAccuracy", "firearmStability",
            "firearmHandling", "firearmAimMinDistance", "firearmAimMaxDistance",
            "firearmAimMovementMultiplier",
        })
        {
            updated = _serializer.RemoveInline(updated, tag => IsNamed(tag, name));
        }

        return updated;
    }

    public string ClearDamageOverrides(string source) => RemoveInlineTags(
        source,
        "attackDamageRate",
        "attackFlatDamage",
        "attackDefenseRate");

    public string ClearAttackBehaviorOverrides(string source) => RemoveInlineTags(
        source,
        "attackInterval",
        "attackRange",
        "attackRadius",
        "projectileSpeed",
        "projectileColliderRadius");

    public string ApplyFamilyClassification(string source, string familyId)
    {
        var family = WeaponAuthoringSchema.FindFamily(familyId) ??
            throw new ArgumentException($"Famiglia arma sconosciuta: {familyId}.", nameof(familyId));
        var updated = SetWeaponFamily(source, family.Id);
        updated = SetWeaponSubtype(updated, family.DefaultSubtype);
        updated = SetHandedness(updated, family.DefaultHandedness);
        updated = SetWeaponFamilyCategory(updated, family);
        updated = SetCombatProfileEnabled(updated, true);
        if (family.IsFirearm)
        {
            updated = SetIfMissing(updated, "magazineSize", value => SetMagazineSize(value, 12));
            updated = SetIfMissing(updated, "reloadDuration", value => SetReloadDuration(value, 1.4));
            updated = SetIfMissing(updated, "firearmAccuracy", value => SetFirearmAccuracy(value, 65));
            updated = SetIfMissing(updated, "firearmStability", value => SetFirearmStability(value, 55));
            updated = SetIfMissing(updated, "firearmHandling", value => SetFirearmHandling(value, 80));
            updated = SetIfMissing(updated, "firearmAimMinDistance", value => SetFirearmAimMinimumDistance(value, 4));
            updated = SetIfMissing(updated, "firearmAimMaxDistance", value => SetFirearmAimMaximumDistance(value, 7));
            updated = SetIfMissing(updated, "firearmAimMovementMultiplier", value => SetFirearmAimMovementMultiplier(value, 0.25));
        }
        else
        {
            updated = ClearFirearmMetadata(updated);
        }

        return updated;
    }

    public string ApplyFamilyDefaults(string source, string familyId)
    {
        var family = WeaponAuthoringSchema.FindFamily(familyId) ??
            throw new ArgumentException($"Famiglia arma sconosciuta: {familyId}.", nameof(familyId));
        var updated = ApplyFamilyClassification(source, family.Id);
        updated = SetDamageRate(updated, 1);
        updated = SetFlatDamage(updated, 0);
        updated = SetDefenseRate(updated, 1);
        updated = SetAttackInterval(updated, family.IsFirearm ? 0.35 : 1.2);
        updated = SetAttackRange(updated, family.IsFirearm ? 10 : 1);
        updated = SetAttackRadius(updated, family.IsFirearm ? 0.2 : 0.35);
        updated = SetProjectileSpeed(updated, family.IsFirearm ? 8 : 0);
        updated = SetProjectileColliderRadius(updated, 8);
        if (family.IsFirearm)
        {
            updated = SetMagazineSize(updated, 12);
            updated = SetReloadDuration(updated, 1.4);
            updated = SetFirearmAccuracy(updated, 65);
            updated = SetFirearmStability(updated, 55);
            updated = SetFirearmHandling(updated, 80);
            updated = SetFirearmAimMinimumDistance(updated, 4);
            updated = SetFirearmAimMaximumDistance(updated, 7);
            updated = SetFirearmAimMovementMultiplier(updated, 0.25);
        }
        else
        {
            updated = ClearFirearmMetadata(updated);
        }

        return updated;
    }

    private string RemoveInlineTags(string source, params string[] names)
    {
        var updated = source;
        foreach (var name in names)
        {
            updated = _serializer.RemoveInline(updated, tag => IsNamed(tag, name));
        }

        return updated;
    }

    private string SetIfMissing(string source, string name, Func<string, string> mutation) =>
        FindInline(source, name) is null ? mutation(source) : source;

    private InlineNotetagNode? FindInline(string source, string name) =>
        _parser.Parse(source).InlineTags.FirstOrDefault(tag => IsNamed(tag, name));

    public string SetCustomParameter(
        string source,
        int currentId,
        int newId,
        int value)
    {
        var node = FindCustomParameter(source, currentId);
        if (node is null)
        {
            return _serializer.SetInlineValue(
                source,
                tag => IsCustomParameter(tag, newId),
                $"cp[{newId}]",
                FormatSigned(value));
        }

        var updated = node.NameSpan is { } nameSpan && currentId != newId
            ? NotetagPatchSerializer.Replace(source, nameSpan, $"cp[{newId}]")
            : source;
        return _serializer.SetInlineValue(
            updated,
            tag => IsCustomParameter(tag, newId),
            $"cp[{newId}]",
            FormatSigned(value));
    }

    public string RemoveCustomParameter(string source, int id) =>
        _serializer.RemoveInline(source, tag => IsCustomParameter(tag, id));

    public string UpdateDisassemblyResult(
        string source,
        int resultIndex,
        string resource,
        int minimum,
        int maximum,
        int probability)
    {
        var line = FindDisassemblyLine(source, resultIndex);
        var replacement = CreateDisassemblyLine(resource, minimum, maximum, probability);
        return line is null
            ? AddDisassemblyResult(source, resource, minimum, maximum, probability)
            : NotetagPatchSerializer.Replace(source, line, replacement);
    }

    public string AddDisassemblyResult(
        string source,
        string resource,
        int minimum,
        int maximum,
        int probability)
    {
        var document = _parser.Parse(source);
        var block = document.BlockTags.FirstOrDefault(node =>
            node.Name.Equals("Disassemble Pool", StringComparison.OrdinalIgnoreCase));
        var line = CreateDisassemblyLine(resource, minimum, maximum, probability);
        if (block is null)
        {
            return _serializer.SetBlockBody(source, "Disassemble Pool", line);
        }

        var newline = DetectNewline(source);
        var insertion = block.Body.Length == 0
            ? line + newline
            : block.Body.EndsWith('\n')
                ? line + newline
                : newline + line;
        var position = block.Body.EndsWith('\n')
            ? block.BodySpan.End
            : block.BodySpan.End;
        return NotetagPatchSerializer.Replace(source, new TextSpan(position, 0), insertion);
    }

    public string RemoveDisassemblyResult(string source, int resultIndex)
    {
        var line = FindDisassemblyLine(source, resultIndex, includeNewline: true);
        if (line is null)
        {
            return source;
        }

        var updated = NotetagPatchSerializer.Replace(source, line, string.Empty);
        var block = _parser.Parse(updated).BlockTags.FirstOrDefault(node =>
            node.Name.Equals("Disassemble Pool", StringComparison.OrdinalIgnoreCase));
        return block is not null && string.IsNullOrWhiteSpace(block.Body)
            ? _serializer.RemoveBlock(updated, "Disassemble Pool")
            : updated;
    }

    private string SetOptionalInteger(string source, string name, int? value) => value is null
        ? _serializer.RemoveInline(source, tag => IsNamed(tag, name))
        : _serializer.SetInlineValue(
            source,
            tag => IsNamed(tag, name),
            name,
            value.Value.ToString(CultureInfo.InvariantCulture));

    private string SetOptionalText(string source, string name, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? _serializer.RemoveInline(source, tag => IsNamed(tag, name))
            : _serializer.SetInlineValue(
                source,
                tag => IsNamed(tag, name),
                name,
                value.Trim());

    private string SetOptionalNumber(string source, string name, double? value) => value is null
        ? _serializer.RemoveInline(source, tag => IsNamed(tag, name))
        : SetNumber(source, name, value.Value);

    private string SetNumber(string source, string name, double value) =>
        _serializer.SetInlineValue(
            source,
            tag => IsNamed(tag, name),
            name,
            value.ToString("0.########", CultureInfo.InvariantCulture));

    private InlineNotetagNode? FindCustomParameter(string source, int id) =>
        _parser.Parse(source).InlineTags.FirstOrDefault(tag => IsCustomParameter(tag, id));

    private TextSpan? FindDisassemblyLine(
        string source,
        int resultIndex,
        bool includeNewline = false)
    {
        var block = _parser.Parse(source).BlockTags.FirstOrDefault(node =>
            node.Name.Equals("Disassemble Pool", StringComparison.OrdinalIgnoreCase));
        if (block is null || resultIndex < 0)
        {
            return null;
        }

        var recognizedIndex = 0;
        foreach (Match match in BlockLineRegex().Matches(block.Body))
        {
            if (!DisassemblyLineRegex().IsMatch(match.Groups["line"].Value.Trim()))
            {
                continue;
            }

            if (recognizedIndex++ != resultIndex)
            {
                continue;
            }

            var length = includeNewline ? match.Length : match.Groups["line"].Length;
            return new TextSpan(block.BodySpan.Start + match.Index, length);
        }

        return null;
    }

    private static string CreateDisassemblyLine(
        string resource,
        int minimum,
        int maximum,
        int probability)
    {
        var quantity = minimum == maximum
            ? minimum.ToString(CultureInfo.InvariantCulture)
            : $"{minimum.ToString(CultureInfo.InvariantCulture)}-{maximum.ToString(CultureInfo.InvariantCulture)}";
        var probabilitySuffix = probability == 100
            ? string.Empty
            : $": {probability.ToString(CultureInfo.InvariantCulture)}%";
        return $"   x{quantity} {resource.Trim()}{probabilitySuffix}";
    }

    private static bool IsNamed(InlineNotetagNode tag, string name) =>
        tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static bool IsCustomParameter(InlineNotetagNode tag, int id) =>
        tag.Name.Equals($"cp[{id}]", StringComparison.OrdinalIgnoreCase);

    private static string FormatSigned(int value) => value >= 0
        ? $"+{value.ToString(CultureInfo.InvariantCulture)}"
        : value.ToString(CultureInfo.InvariantCulture);

    private static string DetectNewline(string source) =>
        source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    [GeneratedRegex(@"(?m)^(?<line>[^\r\n]*)(?:\r\n|\n|\r|$)", RegexOptions.CultureInvariant)]
    private static partial Regex BlockLineRegex();

    [GeneratedRegex(
        @"^x\d+(?:-\d+)?\s+.+?(?:\s*:\s*\d+%)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisassemblyLineRegex();
}
