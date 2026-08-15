using System.Globalization;
using System.Text.RegularExpressions;
using ZiapStudio.Core.Fusion.Weapons;
using ZiapStudio.Core.Notetags;

namespace ZiapStudio.Services.Fusion.Weapons;

public sealed partial class WeaponAdvancedMetadataProvider
{
    private readonly NotetagParser _parser;

    public WeaponAdvancedMetadataProvider(NotetagParser? parser = null)
    {
        _parser = parser ?? new NotetagParser();
    }

    public WeaponAdvancedMetadata Parse(
        string? note,
        WeaponNotetagCatalog? catalog = null)
    {
        note ??= string.Empty;
        catalog ??= WeaponNotetagCatalog.Empty;
        var document = _parser.Parse(note);
        var diagnostics = new List<WeaponNotetagDiagnostic>();
        var recognized = new HashSet<NotetagNode>();

        var perkNode = FindInline(document, "perk");
        IReadOnlyList<string> perks = ["random", "random", "nullo"];
        if (perkNode is not null)
        {
            recognized.Add(perkNode);
            perks = (perkNode.Value ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries)
                .ToArray();
            if (perks.Count != 3)
            {
                diagnostics.Add(Error(
                    "weapon.perk.columns",
                    "perk deve avere esattamente 3 colonne."));
            }
            else
            {
                for (var column = 1; column <= 3; column++)
                {
                    var value = perks[column - 1];
                    var hasProjectCatalog = catalog.Perks.Any(option =>
                        option.Column == column && option.Id is not ("random" or "nullo"));
                    if (hasProjectCatalog && !catalog.Perks.Any(option => option.Column == column &&
                        option.Id.Equals(value, StringComparison.OrdinalIgnoreCase)))
                    {
                        var existsInAnotherColumn = catalog.Perks.Any(option =>
                            option.Id.Equals(value, StringComparison.OrdinalIgnoreCase));
                        diagnostics.Add(Error(
                            existsInAnotherColumn ? "weapon.perk.column" : "weapon.perk.unknown",
                            $"{value} non appartiene alla colonna perk {ToRoman(column)}."));
                    }
                }
            }
        }

        var rarity = ReadInteger(
            document, "itemRare", "weapon.rarity.invalid", recognized, diagnostics, minimum: 0, maximum: 7);
        var requiredLevel = ReadInteger(
            document, "lvReq", "weapon.required-level.invalid", recognized, diagnostics, minimum: 1);
        var maximumLevel = ReadInteger(
            document, "maxLevel", "weapon.maximum-level.invalid", recognized, diagnostics, minimum: 1);
        if (rarity is 6 or 7 && maximumLevel is null)
        {
            diagnostics.Add(Warning(
                "weapon.maximum-level.required-override",
                "ZDP_WeaponLevel non definisce un livello automatico per questa rarità; il fallback runtime è 1."));
        }

        var customParameters = new List<WeaponCustomParameterMetadata>();
        foreach (var node in document.InlineTags)
        {
            var match = CustomParameterRegex().Match(node.Name);
            if (!match.Success)
            {
                if (node.Name.StartsWith("cp[", StringComparison.OrdinalIgnoreCase))
                {
                    recognized.Add(node);
                    diagnostics.Add(Error(
                        "weapon.custom-parameter.malformed",
                        $"Notetag parametro custom non valido: {node.Name}."));
                }

                continue;
            }

            recognized.Add(node);
            if (!int.TryParse(match.Groups["id"].Value, out var id) || id < 1)
            {
                diagnostics.Add(Error(
                    "weapon.custom-parameter.malformed",
                    $"ID parametro custom non valido: {node.Name}."));
                continue;
            }

            if (!int.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                diagnostics.Add(Error(
                    "weapon.custom-parameter.malformed",
                    $"{node.Name} deve contenere un valore numerico."));
                continue;
            }

            customParameters.Add(new WeaponCustomParameterMetadata(id, value));
        }

        var loreNode = FindInline(document, "loreBook");
        WeaponLoreOption? resolvedLore = null;
        var loreResolution = WeaponLoreResolutionKind.None;
        if (loreNode is not null)
        {
            recognized.Add(loreNode);
            var rawLore = loreNode.Value ?? string.Empty;
            resolvedLore = catalog.LoreEntries.FirstOrDefault(entry =>
                entry.Key.Equals(rawLore, StringComparison.Ordinal));
            if (resolvedLore is not null)
            {
                loreResolution = WeaponLoreResolutionKind.ExactKey;
            }
            else
            {
                resolvedLore = catalog.LoreEntries.FirstOrDefault(entry =>
                    entry.Key.Equals(rawLore, StringComparison.OrdinalIgnoreCase));
                if (resolvedLore is not null)
                {
                    loreResolution = WeaponLoreResolutionKind.CaseInsensitiveKey;
                }
                else if (int.TryParse(rawLore, out var loreId))
                {
                    resolvedLore = catalog.LoreEntries.FirstOrDefault(entry => entry.Id == loreId);
                    loreResolution = resolvedLore is null
                        ? WeaponLoreResolutionKind.Missing
                        : WeaponLoreResolutionKind.NumericId;
                }
                else if (WeaponNotetagCatalogProvider.TryResolveLegacyLoreAlias(rawLore, out loreId))
                {
                    resolvedLore = catalog.LoreEntries.FirstOrDefault(entry => entry.Id == loreId);
                    loreResolution = resolvedLore is null
                        ? WeaponLoreResolutionKind.Missing
                        : WeaponLoreResolutionKind.LegacyAlias;
                }
                else
                {
                    loreResolution = WeaponLoreResolutionKind.Missing;
                }
            }

            if (loreResolution == WeaponLoreResolutionKind.LegacyAlias)
            {
                diagnostics.Add(Warning(
                    "weapon.lore.legacy",
                    $"{rawLore} è un alias lore legacy; il valore raw viene preservato."));
            }
            else if (loreResolution == WeaponLoreResolutionKind.Missing)
            {
                diagnostics.Add(Warning(
                    "weapon.lore.unresolved",
                    $"Lore arma non risolta: {rawLore}."));
            }
        }

        var disassemblyNode = document.BlockTags.FirstOrDefault(node =>
            node.Name.Equals("Disassemble Pool", StringComparison.OrdinalIgnoreCase));
        var disassembly = new List<WeaponDisassemblyResultMetadata>();
        if (disassemblyNode is not null)
        {
            recognized.Add(disassemblyNode);
            foreach (var rawLine in disassemblyNode.Body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var match = DisassemblyLineRegex().Match(line);
                if (!match.Success)
                {
                    diagnostics.Add(Error(
                        "weapon.disassembly.syntax",
                        $"Riga disassemblaggio non valida: {line}."));
                    continue;
                }

                var minimum = int.Parse(match.Groups["min"].Value, CultureInfo.InvariantCulture);
                var maximum = match.Groups["max"].Success
                    ? int.Parse(match.Groups["max"].Value, CultureInfo.InvariantCulture)
                    : minimum;
                var probability = match.Groups["probability"].Success
                    ? int.Parse(match.Groups["probability"].Value, CultureInfo.InvariantCulture)
                    : 100;
                var resource = match.Groups["resource"].Value.Trim();
                var resolvedResource = catalog.DisassemblyResources.FirstOrDefault(option =>
                    option.RawValue.Equals(resource, StringComparison.OrdinalIgnoreCase));
                if (minimum > maximum)
                {
                    diagnostics.Add(Error(
                        "weapon.disassembly.range",
                        $"Disassemblaggio {resource}: la quantità minima supera la massima."));
                }

                if (probability is < 0 or > 100)
                {
                    diagnostics.Add(Error(
                        "weapon.disassembly.probability",
                        $"Disassemblaggio {resource}: la probabilità deve essere 0–100%."));
                }

                if (resolvedResource is null && catalog.DisassemblyResources.Count > 0)
                {
                    diagnostics.Add(Warning(
                        "weapon.disassembly.resource",
                        $"Risorsa disassemblaggio non risolta: {resource}; raw preservato."));
                }

                disassembly.Add(new WeaponDisassemblyResultMetadata(
                    resource,
                    resolvedResource?.DisplayName ?? resource,
                    minimum,
                    maximum,
                    probability));
            }
        }

        var hideIconNode = document.InlineTags.FirstOrDefault(IsHideItemIcon);
        if (hideIconNode is not null)
        {
            recognized.Add(hideIconNode);
        }

        var unknownTagCount = document.Nodes
            .Where(node => node is InlineNotetagNode or BlockNotetagNode)
            .Count(node => !recognized.Contains(node));
        var unmanagedTextLines = document.Nodes
            .OfType<RawTextNotetagNode>()
            .SelectMany(node => node.RawText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            .Count(line => !string.IsNullOrWhiteSpace(line));
        var unmanagedCount = unknownTagCount + unmanagedTextLines;
        if (unmanagedCount > 0)
        {
            diagnostics.Add(new WeaponNotetagDiagnostic(
                WeaponNotetagDiagnosticSeverity.Information,
                $"{unmanagedCount} righe o notetag non gestiti da Studio · preservati."));
        }

        return new WeaponAdvancedMetadata
        {
            RawSource = note,
            Perks = perks,
            HasPerkTag = perkNode is not null,
            Rarity = rarity,
            HasRarityTag = FindInline(document, "itemRare") is not null,
            RequiredLevel = requiredLevel,
            HasRequiredLevelTag = FindInline(document, "lvReq") is not null,
            MaximumLevel = maximumLevel,
            HasMaximumLevelTag = FindInline(document, "maxLevel") is not null,
            CustomParameters = customParameters,
            LoreRawValue = loreNode?.Value,
            ResolvedLore = resolvedLore,
            LoreResolutionKind = loreResolution,
            DisassemblyResults = disassembly,
            HasDisassemblyBlock = disassemblyNode is not null,
            HideItemIcon = hideIconNode is not null,
            RecognizedInlineTagCount = recognized.Count(node => node is InlineNotetagNode),
            RecognizedBlockCount = recognized.Count(node => node is BlockNotetagNode),
            UnmanagedLineCount = unmanagedCount,
            Diagnostics = diagnostics,
        };
    }

    public static bool IsHideItemIcon(InlineNotetagNode node) =>
        node.Name.Equals("fhd", StringComparison.OrdinalIgnoreCase) &&
        node.Value?.Equals("no_itemicon", StringComparison.OrdinalIgnoreCase) == true;

    private static InlineNotetagNode? FindInline(NotetagDocument document, string name) =>
        document.InlineTags.FirstOrDefault(node =>
            node.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static int? ReadInteger(
        NotetagDocument document,
        string name,
        string diagnosticCode,
        ISet<NotetagNode> recognized,
        ICollection<WeaponNotetagDiagnostic> diagnostics,
        int minimum,
        int? maximum = null)
    {
        var node = FindInline(document, name);
        if (node is null)
        {
            return null;
        }

        recognized.Add(node);
        if (!int.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            diagnostics.Add(Error(
                diagnosticCode,
                $"{name} deve contenere un valore numerico."));
            return null;
        }

        if (value < minimum || maximum is not null && value > maximum.Value)
        {
            diagnostics.Add(Error(diagnosticCode, maximum is null
                ? $"{name} deve essere almeno {minimum}."
                : $"{name} deve essere compreso tra {minimum} e {maximum}."));
        }

        return value;
    }

    private static WeaponNotetagDiagnostic Error(string code, string message) =>
        new(WeaponNotetagDiagnosticSeverity.Error, message) { Code = code };

    private static WeaponNotetagDiagnostic Warning(string code, string message) =>
        new(WeaponNotetagDiagnosticSeverity.Warning, message) { Code = code };

    private static string ToRoman(int column) => column switch
    {
        1 => "I",
        2 => "II",
        _ => "III",
    };

    [GeneratedRegex(@"^cp\[(?<id>\d+)\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CustomParameterRegex();

    [GeneratedRegex(
        @"^x(?<min>\d+)(?:-(?<max>\d+))?\s+(?<resource>.+?)(?:\s*:\s*(?<probability>\d+)%)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisassemblyLineRegex();
}
