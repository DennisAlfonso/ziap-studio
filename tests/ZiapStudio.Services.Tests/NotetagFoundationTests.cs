using ZiapStudio.Core.Notetags;
using ZiapStudio.Core.Fusion.Weapons;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Fusion.Weapons;

namespace ZiapStudio.Services.Tests;

public sealed class NotetagFoundationTests
{
    private const string WeaponNote =
        "<perk:incrementoAttacco,nullo,nullo>\r\n" +
        "<cp[1]: +2>\r\n" +
        "testo libero da preservare\r\n" +
        "<qualcosaDiUnPluginCheStudioNonConosce>\r\n" +
        "<Disassemble Pool>\r\n" +
        "   x1-2 {db[0].partiArmamento}\r\n" +
        "</Disassemble Pool>";

    [Fact]
    public void Parser_BuildsPreservingNodesWithOriginalSpans()
    {
        var document = new NotetagParser().Parse(WeaponNote);

        var perk = Assert.Single(document.InlineTags.Where(tag => tag.Name == "perk"));
        Assert.Equal("incrementoAttacco,nullo,nullo", perk.Value);
        Assert.Equal(perk.RawText, WeaponNote.Substring(perk.Span.Start, perk.Span.Length));
        Assert.Equal(
            perk.Value,
            WeaponNote.Substring(perk.ValueSpan!.Start, perk.ValueSpan.Length));

        var block = Assert.Single(document.BlockTags);
        Assert.Equal("Disassemble Pool", block.Name);
        Assert.Equal("   x1-2 {db[0].partiArmamento}\r\n", block.Body);
        Assert.Contains(document.Nodes.OfType<RawTextNotetagNode>(), node =>
            node.RawText.Contains("testo libero da preservare", StringComparison.Ordinal));
    }

    [Fact]
    public void PatchSerializer_ChangesOnlyRequestedInlineValue()
    {
        var serializer = new NotetagPatchSerializer();

        var changed = serializer.SetInlineValue(
            WeaponNote,
            tag => tag.Name.Equals("cp[1]", StringComparison.OrdinalIgnoreCase),
            "cp[1]",
            "+3");

        Assert.Equal(WeaponNote.Replace("<cp[1]: +2>", "<cp[1]: +3>"), changed);
        Assert.Contains("<qualcosaDiUnPluginCheStudioNonConosce>", changed);
        Assert.Contains("testo libero da preservare", changed);
    }

    [Fact]
    public void PatchSerializer_PreservesUnknownTextWhileEditingBlock()
    {
        var serializer = new NotetagPatchSerializer();

        var changed = serializer.SetBlockBody(
            WeaponNote,
            "Disassemble Pool",
            "   x2-3 {db[0].partiArmamento}");

        Assert.Equal(
            WeaponNote.Replace(
                "   x1-2 {db[0].partiArmamento}",
                "   x2-3 {db[0].partiArmamento}"),
            changed);
    }

    [Fact]
    public void PatchSerializer_AppendsAndRemovesFlagUsingExistingNewlines()
    {
        var serializer = new NotetagPatchSerializer();
        var added = serializer.SetFlag(
            WeaponNote,
            tag => tag.Name.Equals("fhd", StringComparison.OrdinalIgnoreCase) &&
                tag.Value?.Equals("no_itemicon", StringComparison.OrdinalIgnoreCase) == true,
            "fhd:no_itemicon",
            enabled: true);

        Assert.EndsWith("\r\n<fhd:no_itemicon>", added);

        var removed = serializer.SetFlag(
            added,
            tag => tag.Name.Equals("fhd", StringComparison.OrdinalIgnoreCase) &&
                tag.Value?.Equals("no_itemicon", StringComparison.OrdinalIgnoreCase) == true,
            "fhd:no_itemicon",
            enabled: false);
        Assert.Equal(WeaponNote, removed);
    }

    [Fact]
    public void WeaponMetadata_ParsesSemanticFieldsAndLegacyLoreWithoutNormalizingRaw()
    {
        const string note =
            "<perk:incrementoAttacco,nullo,nullo>\n" +
            "<cp[1]: +2>\n" +
            "<itemRare:0>\n" +
            "<lvReq:1>\n" +
            "<loreBook:viaggiamondiDistrutta>\n" +
            "<Disassemble Pool>\n" +
            "   x1-2 {db[0].partiArmamento}\n" +
            "</Disassemble Pool>\n" +
            "<plugin-sconosciuto:resta>";
        var catalog = CreateCatalog();

        var metadata = new WeaponAdvancedMetadataProvider().Parse(note, catalog);

        Assert.Equal(["incrementoAttacco", "nullo", "nullo"], metadata.Perks);
        Assert.Equal(0, metadata.Rarity);
        Assert.Equal(5, metadata.AutomaticMaximumLevel);
        Assert.Equal(new WeaponCustomParameterMetadata(1, 2), Assert.Single(metadata.CustomParameters));
        Assert.Equal(WeaponLoreResolutionKind.LegacyAlias, metadata.LoreResolutionKind);
        Assert.Equal("unarmaconundestinointrecciato-1", metadata.ResolvedLore!.Key);
        var result = Assert.Single(metadata.DisassemblyResults);
        Assert.Equal("Parti Armamento", result.DisplayName);
        Assert.Equal(1, result.MinimumQuantity);
        Assert.Equal(2, result.MaximumQuantity);
        Assert.Equal(100, result.Probability);
        Assert.Equal(note, metadata.RawSource);
        Assert.Equal(1, metadata.UnmanagedLineCount);
    }

    [Fact]
    public void WeaponMetadata_UnderstandsRandomAndExplicitMaximumLevel()
    {
        const string note =
            "<cp[3]: +6>\n" +
            "<itemRare:3>\n" +
            "<perk:random,ascendente,nullo>\n" +
            "<lvReq:16>\n" +
            "<maxLevel:1>\n" +
            "<fhd:no_itemicon>";

        var metadata = new WeaponAdvancedMetadataProvider().Parse(note, CreateCatalog());

        Assert.Equal(["random", "ascendente", "nullo"], metadata.Perks);
        Assert.Equal(30, metadata.AutomaticMaximumLevel);
        Assert.Equal(1, metadata.MaximumLevel);
        Assert.True(metadata.HideItemIcon);
        Assert.DoesNotContain(metadata.Diagnostics, issue =>
            issue.Severity == WeaponNotetagDiagnosticSeverity.Error);
    }

    [Fact]
    public void WeaponMetadata_ValidatesColumnsRangesAndUnknownTagsConservatively()
    {
        const string note =
            "<perk:ascendente,nullo>\n" +
            "<itemRare:8>\n" +
            "<lvReq:0>\n" +
            "<Disassemble Pool>\n" +
            "x5-2 {db[0].partiArmamento}: 120%\n" +
            "</Disassemble Pool>";

        var metadata = new WeaponAdvancedMetadataProvider().Parse(note, CreateCatalog());

        Assert.Contains(metadata.Diagnostics, issue => issue.Message.Contains("esattamente 3"));
        Assert.Contains(metadata.Diagnostics, issue => issue.Message.Contains("0 e 7"));
        Assert.Contains(metadata.Diagnostics, issue => issue.Message.Contains("almeno 1"));
        Assert.Contains(metadata.Diagnostics, issue => issue.Message.Contains("minima supera"));
        Assert.Contains(metadata.Diagnostics, issue => issue.Message.Contains("0–100"));
    }

    private static WeaponNotetagCatalog CreateCatalog() => new()
    {
        Perks =
        [
            new("random", "Casuale", 1, 5),
            new("nullo", "Nullo", 1, 0),
            new("incrementoAttacco", "Incremento Attacco", 1, 5),
            new("random", "Casuale", 2, 15),
            new("nullo", "Nullo", 2, 0),
            new("ascendente", "Ascendenza Hex", 2, 15),
            new("random", "Casuale", 3, 30),
            new("nullo", "Nullo", 3, 0),
        ],
        LoreEntries = [new("unarmaconundestinointrecciato-1", "Un'arma con un destino intrecciato", 1)],
        DisassemblyResources = [new("{db[0].partiArmamento}", "Parti Armamento")],
    };

    [Fact]
    public void WeaponEditor_ChangesAcceptanceValuesAndPreservesUnknownContent()
    {
        const string source =
            "<perk:incrementoAttacco,nullo,nullo>\n" +
            "<cp[1]: +2>\n" +
            "<itemRare:0>\n" +
            "<plugin-sconosciuto:preservami>\n" +
            "<Disassemble Pool>\n" +
            "   riga custom del plugin\n" +
            "   x1-2 {db[0].partiArmamento}\n" +
            "</Disassemble Pool>";
        var editor = new WeaponAdvancedNoteEditor();

        var changed = editor.SetRarity(source, 1);
        changed = editor.SetCustomParameter(changed, 1, 1, 3);
        changed = editor.UpdateDisassemblyResult(
            changed,
            0,
            "{db[0].partiArmamento}",
            2,
            3,
            100);

        var expected = source
            .Replace("<itemRare:0>", "<itemRare:1>", StringComparison.Ordinal)
            .Replace("<cp[1]: +2>", "<cp[1]: +3>", StringComparison.Ordinal)
            .Replace("x1-2", "x2-3", StringComparison.Ordinal);
        Assert.Equal(expected, changed);
        Assert.Contains("riga custom del plugin", changed);
        Assert.Contains("<plugin-sconosciuto:preservami>", changed);
    }

    [Fact]
    public async Task CatalogProvider_LoadsProjectPerksLoreAndLocalizedResources()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "js/plugins/zenkaiDevPlugins/ZDP_WeaponPerks.js",
            """
            id: "colonna1", ramoPerks: [
              { id: "nullo", name: "Nullo", level: 0 },
              { id: "incrementoAttacco", name: "{plugin[1].strings[1].atk}", level: 5 }
            ]
            id: "colonna2", ramoPerks: [
              { id: "nullo", name: "Nullo", level: 0 },
              { id: "ascendente", name: "Ascendenza Hex", level: 15 }
            ]
            id: "colonna3", ramoPerks: [
              { id: "nullo", name: "Nullo", level: 0 },
              { id: "retaggioPrimordiale", name: "Retaggio Primordiale", level: 30 }
            ]
            // Funzioni di utilità
            """);
        workspace.WriteFile(
            "locales/it/plugin.json",
            "[{}, {\"strings\":[{}, {\"atk\":\"Incremento Attacco\"}]}]");
        workspace.WriteFile(
            "locales/it/books.json",
            "{\"library\":{\"books\":{\"lore-1\":{\"title\":\"Lore Uno\",\"category\":0,\"id\":1},\"altro-1\":{\"title\":\"Altro\",\"category\":1,\"id\":1}}}}");
        workspace.WriteFile(
            "locales/it/db.json",
            "[{\"partiArmamento\":\"Parti Armamento\"}]");
        workspace.WriteFile(
            "data/Items.json",
            "[null,{\"id\":1,\"name\":\"{db[0].partiArmamento}\"}]");

        var catalog = await new WeaponNotetagCatalogProvider(new FileSystemService()).LoadAsync(
            new ZiapProject
            {
                Id = "test",
                Name = "Test",
                ProjectType = KnownProjectTypes.RpgMakerMz,
                Path = workspace.RootPath,
            });

        Assert.Contains(catalog.Perks, option =>
            option.Id == "incrementoAttacco" && option.DisplayName == "Incremento Attacco");
        Assert.Contains(catalog.Perks, option =>
            option.Id == "ascendente" && option.Column == 2);
        Assert.Equal("lore-1", Assert.Single(catalog.LoreEntries).Key);
        Assert.Contains(catalog.DisassemblyResources, option =>
            option.RawValue == "{db[0].partiArmamento}" &&
            option.DisplayName == "Parti Armamento");
    }
}
