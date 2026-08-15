using System.Globalization;
using System.Text.RegularExpressions;
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
