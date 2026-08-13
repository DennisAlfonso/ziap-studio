using System.Text.Json.Nodes;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Documents;
using ZiapStudio.Services.Editing;

namespace ZiapStudio.Services.Tests;

public sealed class EditingFoundationTests
{
    [Fact]
    public async Task EditSession_TracksDirtyStateUndoRedoAndChangeSet()
    {
        using var workspace = new TestWorkspace();
        var document = await OpenWeaponsAsync(workspace, StandardWeaponsJson);
        var session = new DocumentEditSessionFactory().Create(document);

        var changed = session.SetValue(1, "price", JsonValue.Create(300));

        Assert.True(changed);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        var change = Assert.Single(session.ChangeSet.Changes);
        Assert.Equal("rpgmaker://database/weapons/1", change.Target);
        Assert.Equal("price", change.PropertyPath);
        Assert.Equal(250, change.OldValue!.GetValue<int>());
        Assert.Equal(300, change.NewValue!.GetValue<int>());

        Assert.True(session.Undo());
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
        Assert.Empty(session.ChangeSet.Changes);

        Assert.True(session.Redo());
        Assert.True(session.IsDirty);
        Assert.Equal(300, GetRecord(session, 1)["price"]!.GetValue<int>());
    }

    [Fact]
    public async Task Save_PreservesUnknownFieldsNullSentinelAndPlaceholders()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = workspace.WriteFile("data/Weapons.json", StandardWeaponsJson);
        var document = await OpenWeaponsAsync(workspace);
        var session = new DocumentEditSessionFactory().Create(document);
        session.SetValue(1, "name", JsonValue.Create("Spada del Precursore"));
        session.SetValue(1, "params[2]", JsonValue.Create(42));
        var saveService = CreateSaveService();

        var result = await saveService.SaveAsync(session);

        Assert.Equal(DocumentSaveStatus.Saved, result.Status);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(File.Exists($"{sourcePath}.ziap-tmp"));
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(sourcePath))!.AsArray();
        Assert.Null(saved[0]);
        Assert.Null(saved[2]);
        Assert.Equal(4, saved.Count);
        var weapon = saved[1]!.AsObject();
        Assert.Equal("Spada del Precursore", weapon["name"]!.GetValue<string>());
        Assert.Equal(42, weapon["params"]![2]!.GetValue<int>());
        Assert.Equal("preservami", weapon["qualcheCampoCustom"]!.GetValue<string>());
        Assert.Equal(123, weapon["campoDiUnPlugin"]!.GetValue<int>());
        Assert.Equal("Placeholder", saved[3]!["name"]!.GetValue<string>());
        Assert.NotEqual(document.SourceSnapshot.ContentHash, session.SourceSnapshot.ContentHash);
        var expectedText = StandardWeaponsJson
            .Replace("\"name\": \"La Spada\"", "\"name\": \"Spada del Precursore\"", StringComparison.Ordinal)
            .Replace("\"params\": [0, 0, 10,", "\"params\": [0, 0, 42,", StringComparison.Ordinal);
        Assert.Equal(expectedText, await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task Save_DetectsExternalModificationAndDoesNotOverwriteIt()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = workspace.WriteFile("data/Weapons.json", StandardWeaponsJson);
        var document = await OpenWeaponsAsync(workspace);
        var session = new DocumentEditSessionFactory().Create(document);
        session.SetValue(1, "price", JsonValue.Create(300));
        const string externalJson = "[null,{\"id\":1,\"name\":\"Modifica RPG Maker\",\"price\":999}]";
        await File.WriteAllTextAsync(sourcePath, externalJson);

        var result = await CreateSaveService().SaveAsync(session);

        Assert.Equal(DocumentSaveStatus.ExternalModification, result.Status);
        Assert.True(session.IsDirty);
        Assert.Equal(externalJson, await File.ReadAllTextAsync(sourcePath));
        Assert.False(File.Exists($"{sourcePath}.ziap-tmp"));
    }

    [Fact]
    public async Task Save_BlocksIncompatibleValueTypes()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = workspace.WriteFile("data/Weapons.json", StandardWeaponsJson);
        var original = await File.ReadAllTextAsync(sourcePath);
        var document = await OpenWeaponsAsync(workspace);
        var warning = new DocumentValidationIssue
        {
            Severity = DocumentValidationSeverity.Warning,
            Code = "missing-asset",
            Message = "Asset volutamente mancante.",
        };
        var session = new DocumentEditSession(document, [warning]);
        session.SetValue(1, "price", JsonValue.Create("trecento"));

        var result = await CreateSaveService().SaveAsync(session);

        Assert.Equal(DocumentSaveStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Validation.Issues, issue =>
            issue.Code == "incompatible-value-type" &&
            issue.Severity == DocumentValidationSeverity.Error);
        Assert.Contains(result.Validation.Issues, issue =>
            issue.Code == "missing-asset" &&
            issue.Severity == DocumentValidationSeverity.Warning);
        Assert.Equal(original, await File.ReadAllTextAsync(sourcePath));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task Save_AllowsWarningsWhenStructuralValidationPasses()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("data/Weapons.json", StandardWeaponsJson);
        var document = await OpenWeaponsAsync(workspace);
        var warning = new DocumentValidationIssue
        {
            Severity = DocumentValidationSeverity.Warning,
            Code = "missing-asset",
            Message = "Asset volutamente mancante.",
        };
        var session = new DocumentEditSession(document, [warning]);
        session.SetValue(1, "price", JsonValue.Create(300));

        var result = await CreateSaveService().SaveAsync(session);

        Assert.Equal(DocumentSaveStatus.Saved, result.Status);
        Assert.False(session.IsDirty);
        Assert.Contains(result.Validation.Issues, issue =>
            issue.Code == "missing-asset" &&
            issue.Severity == DocumentValidationSeverity.Warning);
    }

    [Fact]
    public async Task Save_BlocksWeaponValuesBelowDeclaredMinimum()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = workspace.WriteFile("data/Weapons.json", StandardWeaponsJson);
        var original = await File.ReadAllTextAsync(sourcePath);
        var document = await OpenWeaponsAsync(workspace);
        var session = new DocumentEditSessionFactory().Create(document);
        session.SetValue(1, "price", JsonValue.Create(-1));

        var result = await CreateSaveService().SaveAsync(session);

        Assert.Equal(DocumentSaveStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Validation.Issues, issue =>
            issue.Code == "value-below-minimum" && issue.PropertyPath == "price");
        Assert.Equal(original, await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task AtomicSaveFailure_LeavesOriginalIntactAndSessionDirty()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = workspace.WriteFile("data/Weapons.json", StandardWeaponsJson);
        var original = await File.ReadAllBytesAsync(sourcePath);
        var document = await OpenWeaponsAsync(workspace);
        var session = new DocumentEditSessionFactory().Create(document);
        session.SetValue(1, "price", JsonValue.Create(999));

        await using var lockStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var result = await CreateSaveService().SaveAsync(session);

        Assert.Equal(DocumentSaveStatus.Failed, result.Status);
        Assert.True(session.IsDirty);
        Assert.Equal(original, await File.ReadAllBytesAsync(sourcePath));
        Assert.False(File.Exists($"{sourcePath}.ziap-tmp"));
    }

    [Fact]
    public async Task SessionSnapshot_CapturesSourceIdentityAndReferenceWarnings()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "data/Actors.json",
            "[null,{\"id\":1,\"name\":\"Yato\",\"classId\":999}]" );
        workspace.WriteFile(
            "data/Classes.json",
            "[null,{\"id\":1,\"name\":\"Redux\"}]" );
        var descriptor = CreateDescriptor("actors", "Attori");
        var document = Assert.IsType<RpgMakerDatabaseDocument>(
            await CreateDocumentService().OpenAsync(
                CreateProject(workspace.RootPath),
                descriptor));

        var session = new DocumentEditSessionFactory().Create(document);

        Assert.Equal(Path.Combine(workspace.RootPath, "data", "Actors.json"),
            session.SourceSnapshot.SourcePath);
        Assert.NotEmpty(session.SourceSnapshot.ContentHash);
        Assert.True(session.SourceSnapshot.Length > 0);
        Assert.True(session.SourceSnapshot.LoadedAtUtc > DateTimeOffset.MinValue);
        Assert.Contains(session.BaselineIssues, issue =>
            issue.Code == "missing-database-reference" &&
            issue.Severity == DocumentValidationSeverity.Warning);
    }

    [Fact]
    public async Task FirstRealEditingScenario_SavesOnlyPriceAndAttackTokens()
    {
        using var workspace = new TestWorkspace();
        var localizedJson = StandardWeaponsJson.Replace(
            "\"name\": \"La Spada\"",
            "\"name\": \"{db[1].spada}\"",
            StringComparison.Ordinal);
        var sourcePath = workspace.WriteFile("data/Weapons.json", localizedJson);
        workspace.WriteFile("locales/it/db.json", "[{}, {\"spada\":\"La Spada\"}]");
        var originalBytes = await File.ReadAllBytesAsync(sourcePath);
        var document = await OpenWeaponsAsync(workspace);
        var session = new DocumentEditSessionFactory().Create(document);

        session.SetValue(1, "price", JsonValue.Create(251));
        session.SetValue(1, "params[2]", JsonValue.Create(11));
        Assert.Equal(2, session.ChangeSet.Count);
        Assert.True(session.Undo());
        Assert.Equal(1, session.ChangeSet.Count);
        Assert.Equal(10, session.GetValue(1, "params[2]")!.GetValue<int>());
        Assert.True(session.Redo());
        Assert.Equal(2, session.ChangeSet.Count);

        var result = await CreateSaveService().SaveAsync(session);

        Assert.Equal(DocumentSaveStatus.Saved, result.Status);
        var savedBytes = await File.ReadAllBytesAsync(sourcePath);
        Assert.Equal(originalBytes.Length, savedBytes.Length);
        Assert.Equal(2, originalBytes.Zip(savedBytes).Count(pair => pair.First != pair.Second));
        var reloaded = await OpenWeaponsAsync(workspace);
        var weapon = reloaded.Entries.Single(entry => entry.Id == 1);
        Assert.Equal("{db[1].spada}", weapon.GetValue("name").RawValue);
        Assert.Equal("La Spada", weapon.GetValue("name").DisplayValue);
        Assert.Equal("251", weapon.GetValue("price").RawValue);
        Assert.Equal("11", weapon.GetValue("params[2]").RawValue);
        var savedRoot = reloaded.SourceRoot.AsArray();
        Assert.Null(savedRoot[0]);
        Assert.Null(savedRoot[2]);
        Assert.Equal("preservami", savedRoot[1]!["qualcheCampoCustom"]!.GetValue<string>());
        Assert.Equal("<custom>", savedRoot[1]!["note"]!.GetValue<string>());
    }

    private const string StandardWeaponsJson =
        """
        [
          null,
          {
            "id": 1,
            "name": "La Spada",
            "description": "Arma di prova",
            "price": 250,
            "iconIndex": 0,
            "wtypeId": 0,
            "etypeId": 0,
            "animationId": 0,
            "params": [0, 0, 10, 0, 0, 0, 0, 0],
            "note": "<custom>",
            "qualcheCampoCustom": "preservami",
            "campoDiUnPlugin": 123
          },
          null,
          { "id": 3, "name": "Placeholder", "price": 0 }
        ]
        """;

    private static async Task<RpgMakerDatabaseDocument> OpenWeaponsAsync(
        TestWorkspace workspace,
        string? json = null)
    {
        if (json is not null)
        {
            workspace.WriteFile("data/Weapons.json", json);
        }

        return Assert.IsType<RpgMakerDatabaseDocument>(
            await CreateDocumentService().OpenAsync(
                CreateProject(workspace.RootPath),
                CreateDescriptor("weapons", "Armi")));
    }

    private static JsonObject GetRecord(DocumentEditSession session, int id) =>
        session.WorkingState.AsArray()
            .OfType<JsonObject>()
            .Single(record => record["id"]!.GetValue<int>() == id);

    private static DocumentSaveService CreateSaveService()
    {
        var fileSystem = new FileSystemService();
        var snapshotService = new DocumentSnapshotService(fileSystem);
        return new DocumentSaveService(
            new DocumentValidationService(),
            new ExternalModificationDetector(fileSystem, snapshotService),
            new AtomicJsonFileWriter(fileSystem),
            snapshotService,
            new JsonTextPatchSerializer(fileSystem));
    }

    private static DocumentService CreateDocumentService()
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

    private static DocumentDescriptor CreateDescriptor(string resourceName, string displayName) => new()
    {
        Id = new DocumentId($"project:rpgmaker:database:{resourceName}"),
        DisplayName = displayName,
        Kind = DocumentKind.RpgMakerDatabase,
        ResourceId = new Uri($"rpgmaker://database/{resourceName}"),
    };
}
