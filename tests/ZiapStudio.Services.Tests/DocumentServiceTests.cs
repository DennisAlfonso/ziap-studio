using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Documents;

namespace ZiapStudio.Services.Tests;

public sealed class DocumentServiceTests
{
    [Fact]
    public async Task OpenAsync_LoadsStructuredWeaponsDatabase()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "data/Weapons.json",
            """
            [null,
              {
                "id": 1,
                "name": "Viaggiamondi",
                "description": "Arma di prova",
                "iconIndex": 203,
                "price": 125,
                "wtypeId": 4,
                "etypeId": 1,
                "animationId": 6,
                "note": "<maxLevel:30>"
              },
              {
                "id": 2,
                "name": "{db[1].armaTest}",
                "iconIndex": 0,
                "price": 0,
                "wtypeId": 0
              }
            ]
            """);
        workspace.WriteFile(
            "data/System.json",
            """{ "weaponTypes": ["", "Spada", "", "", "Lancia"], "equipTypes": ["", "{main[0].weapon}"] }""");
        workspace.WriteFile(
            "data/Animations.json",
            """[null, { "id": 6, "name": "Slash Physical" }]""");
        workspace.WriteFile(
            "locales/it/db.json",
            """[{}, { "armaTest": "Arma di prova localizzata" }]""");
        workspace.WriteFile(
            "locales/it/main.json",
            """[{ "weapon": "Arma" }]""");
        var service = CreateService();

        var document = await service.OpenAsync(
            CreateProject(workspace.RootPath),
            CreateWeaponsDescriptor());

        var database = Assert.IsType<RpgMakerDatabaseDocument>(document);
        Assert.Equal(Path.Combine(workspace.RootPath, "data", "Weapons.json"), database.SourcePath);
        Assert.Equal(2, database.Entries.Count);
        var weapon = database.Entries[0];
        Assert.Equal(1, weapon.Id);
        Assert.Equal("Viaggiamondi", weapon.Name);
        Assert.Equal("Arma di prova", weapon.GetValue("description").DisplayValue);
        Assert.Equal("203", weapon.GetValue("iconIndex").RawValue);
        Assert.Equal(RpgMakerResolvedValueKind.AssetReference, weapon.GetValue("iconIndex").Kind);
        Assert.Equal("icons", weapon.GetValue("iconIndex").ReferenceTarget);
        var weaponTypeOptions = weapon.GetValue("wtypeId").EditorOptions;
        Assert.Contains(weaponTypeOptions, option =>
            option.Value == 0 && option.DisplayValue == "0 — Nessuna");
        Assert.Contains(weaponTypeOptions, option =>
            option.Value == 4 && option.DisplayValue == "4 — Lancia");
        Assert.Contains(weapon.GetValue("animationId").EditorOptions, option =>
            option.Value == 6 && option.DisplayValue == "6 — Slash Physical");
        Assert.Equal("125", weapon.GetValue("price").DisplayValue);
        Assert.Equal("4", weapon.GetValue("wtypeId").RawValue);
        Assert.Equal("Lancia", weapon.GetValue("wtypeId").ResolvedValue);
        Assert.Equal("4 — Lancia", weapon.GetValue("wtypeId").DisplayValue);
        Assert.Equal("1 — Arma", weapon.GetValue("etypeId").DisplayValue);
        var equipTypeOrigin = Assert.IsType<LocalizationReferenceOrigin>(
            weapon.GetValue("etypeId").LocalizationOrigin);
        Assert.Equal("main.json", equipTypeOrigin.SourceFile);
        Assert.Equal("0.weapon", equipTypeOrigin.Path);
        Assert.Equal("6 — Slash Physical", weapon.GetValue("animationId").DisplayValue);
        Assert.Equal("<maxLevel:30>", weapon.GetValue("note").RawValue);
        Assert.Equal(RpgMakerResolvedValueKind.Note, weapon.GetValue("note").Kind);
        var localizedName = database.Entries[1].GetValue("name");
        Assert.Equal(RpgMakerResolvedValueKind.LocalizationReference, localizedName.Kind);
        Assert.Equal("{db[1].armaTest}", localizedName.RawValue);
        Assert.Equal("Arma di prova localizzata", localizedName.ResolvedValue);
        Assert.Equal("Arma di prova localizzata", localizedName.DisplayValue);
        Assert.Equal("db[1].armaTest", localizedName.Target);
        var localizedNameOrigin = Assert.IsType<LocalizationReferenceOrigin>(
            localizedName.LocalizationOrigin);
        Assert.Equal("db.json", localizedNameOrigin.SourceFile);
        Assert.Equal("1.armaTest", localizedNameOrigin.Path);
        Assert.Equal(RpgMakerResolutionStatus.Resolved, localizedName.Status);
        Assert.Equal("Armi", database.Definition.DisplayName);
        Assert.Equal("Arma", database.Definition.ItemDisplayName);
        Assert.Equal(
            ["ID", "Nome", "Tipo arma", "Prezzo"],
            database.Definition.Columns.Select(column => column.DisplayName));
        var fields = database.Definition.Sections.SelectMany(section => section.Fields).ToArray();
        Assert.False(Assert.Single(fields, field => field.Key == "name").IsEditable);
        Assert.False(Assert.Single(fields, field => field.Key == "description").IsEditable);
        Assert.Equal(
            RpgMakerEditorKind.ReferenceComboBox,
            Assert.Single(fields, field => field.Key == "animationId").EditorKind);
        Assert.Equal(
            RpgMakerEditorKind.Number,
            Assert.Single(fields, field => field.Key == "price").EditorKind);
        Assert.Equal(0, Assert.Single(fields, field => field.Key == "price").Minimum);
    }

    [Fact]
    public async Task OpenAsync_ReportsInvalidDatabaseJson()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("data/Weapons.json", "{ invalid json");
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<DocumentLoadException>(() =>
            service.OpenAsync(CreateProject(workspace.RootPath), CreateWeaponsDescriptor()));

        Assert.Contains("Weapons.json", exception.Message);
    }

    [Fact]
    public async Task OpenAsync_UsesActorInspectorDefinition()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "data/Actors.json",
            """
            [null, {
              "id": 1,
              "name": "Yato",
              "classId": 1,
              "initialLevel": 1,
              "maxLevel": 50,
              "characterName": "$YatoBase",
              "profile": "<WordWrap>\\nYato, un evocato.",
              "note": "<xAnima:Yato>"
            }]
            """);
        workspace.WriteFile(
            "data/Classes.json",
            """[null, { "id": 1, "name": "Redux" }]""");

        var document = Assert.IsType<RpgMakerDatabaseDocument>(
            await CreateService().OpenAsync(
                CreateProject(workspace.RootPath),
                CreateDescriptor("actors", "Attori")));

        Assert.Equal("Attore", document.Definition.ItemDisplayName);
        Assert.Equal(
            ["ID", "Nome", "Classe", "Livello iniziale"],
            document.Definition.Columns.Select(column => column.DisplayName));
        Assert.Contains(document.Definition.Sections, section => section.DisplayName == "Progressione");
        Assert.Contains(document.Definition.Sections, section => section.DisplayName == "Grafica");
        var actor = Assert.Single(document.Entries);
        Assert.Equal("50", actor.GetValue("maxLevel").RawValue);
        Assert.Equal("1", actor.GetValue("classId").RawValue);
        Assert.Equal("Redux", actor.GetValue("classId").ResolvedValue);
        Assert.Equal("1 — Redux", actor.GetValue("classId").DisplayValue);
        Assert.Equal("rpgmaker://database/classes/1", actor.GetValue("classId").Target);
        Assert.Equal(RpgMakerResolutionStatus.Resolved, actor.GetValue("classId").Status);
        Assert.Equal(
            $"<WordWrap>{Environment.NewLine}Yato, un evocato.",
            actor.GetValue("profile").DisplayValue);
        Assert.Equal("<WordWrap>\\nYato, un evocato.", actor.GetValue("profile").RawValue);
        Assert.Equal(RpgMakerResolvedValueKind.AssetReference, actor.GetValue("characterName").Kind);
        Assert.Equal("<xAnima:Yato>", actor.GetValue("note").DisplayValue);
    }

    [Fact]
    public async Task OpenAsync_MarksMissingDatabaseReferenceTargets()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "data/Actors.json",
            """[null, { "id": 1, "name": "Yato", "classId": 999 }]""");
        workspace.WriteFile(
            "data/Classes.json",
            """[null, { "id": 1, "name": "Redux" }]""");

        var document = Assert.IsType<RpgMakerDatabaseDocument>(
            await CreateService().OpenAsync(
                CreateProject(workspace.RootPath),
                CreateDescriptor("actors", "Attori")));

        var classReference = Assert.Single(document.Entries).GetValue("classId");
        Assert.Equal("999", classReference.RawValue);
        Assert.Null(classReference.ResolvedValue);
        Assert.Equal("rpgmaker://database/classes/999", classReference.Target);
        Assert.Equal(RpgMakerResolutionStatus.MissingTarget, classReference.Status);
    }

    [Fact]
    public async Task OpenAsync_UsesEnemyInspectorDefinitionAndParameterPaths()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "data/Enemies.json",
            """
            [null, {
              "id": 1,
              "name": "Orso",
              "exp": 1000,
              "gold": 100,
              "params": [150, 0, 18, 10, 10, 10, 10, 10],
              "note": "<ABS>"
            }]
            """);

        var document = Assert.IsType<RpgMakerDatabaseDocument>(
            await CreateService().OpenAsync(
                CreateProject(workspace.RootPath),
                CreateDescriptor("enemies", "Nemici")));

        Assert.Equal("Nemico", document.Definition.ItemDisplayName);
        Assert.Equal(
            ["ID", "Nome", "EXP", "Gold"],
            document.Definition.Columns.Select(column => column.DisplayName));
        Assert.Contains(document.Definition.Sections, section => section.DisplayName == "Ricompense");
        Assert.Contains(document.Definition.Sections, section => section.DisplayName == "Statistiche");
        Assert.Equal("150", Assert.Single(document.Entries).GetValue("params[0]").RawValue);
    }

    [Fact]
    public void Resolve_RejectsUnknownDocumentKinds()
    {
        var resolver = new DocumentResolver(
        [
            new RpgMakerDatabaseDocumentProvider(new FileSystemService()),
        ]);
        var descriptor = new DocumentDescriptor
        {
            Id = new DocumentId("project:overview"),
            DisplayName = "Overview",
            Kind = DocumentKind.ProjectOverview,
            ResourceId = new Uri("ziap://project/overview"),
        };

        Assert.Throws<DocumentLoadException>(() => resolver.Resolve(descriptor));
    }

    private static DocumentService CreateService()
    {
        var fileSystem = new FileSystemService();
        return new DocumentService(new DocumentResolver(
        [
            new RpgMakerDatabaseDocumentProvider(fileSystem),
        ]));
    }

    private static ZiapProject CreateProject(string path) => new()
    {
        Id = "project",
        Name = "Project",
        ProjectType = KnownProjectTypes.RpgMakerMz,
        Path = path,
    };

    private static DocumentDescriptor CreateWeaponsDescriptor() =>
        CreateDescriptor("weapons", "Armi");

    private static DocumentDescriptor CreateDescriptor(string resourceName, string displayName) => new()
    {
        Id = new DocumentId($"project:rpgmaker:database:{resourceName}"),
        DisplayName = displayName,
        Kind = DocumentKind.RpgMakerDatabase,
        ResourceId = new Uri($"rpgmaker://database/{resourceName}"),
    };
}
