using System.Text.Json.Nodes;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Weapons;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Documents;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Fusion.Weapons;

namespace ZiapStudio.Services.Tests;

public sealed class WeaponCreationServiceTests
{
    [Fact]
    public async Task Create_UsesFreeSlotsAndPersistsCompleteFirearmSchemas()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = workspace.WriteFile(
            "data/Weapons.json",
            """
            [
              null,
              {"id":1,"animationId":0,"description":"","etypeId":1,"traits":[],"iconIndex":0,"name":"","note":"","params":[0,0,0,0,0,0,0,0],"price":0,"wtypeId":0},
              {"id":2,"animationId":0,"description":"","etypeId":1,"traits":[],"iconIndex":0,"name":"","note":"","params":[0,0,0,0,0,0,0,0],"price":0,"wtypeId":0},
              {"id":3,"animationId":0,"description":"","etypeId":1,"traits":[],"iconIndex":0,"name":"Placeholder","note":"<custom:keep>","params":[0,0,0,0,0,0,0,0],"price":0,"wtypeId":0}
            ]
            """);
        var document = await OpenWeaponsAsync(workspace.RootPath);
        var session = new DocumentEditSessionFactory().Create(document);
        var service = new WeaponCreationService();

        var firstId = service.Create(session, WeaponCreationDraft.Firearm(
            "Aster-9",
            "Pistola semiautomatica agile.") with
        {
            IconIndex = 384,
            Power = 18,
            MagazineSize = 15,
            AttackInterval = 0.22,
            Accuracy = 72,
            Stability = 62,
            Handling = 84,
            Perks = ["heavyMagazine", "nullo", "nullo"],
        });
        var secondId = service.Create(session, WeaponCreationDraft.Firearm(
            "Vesper-12",
            "Pistola pesante precisa e stabile.") with
        {
            IconIndex = 385,
            Power = 32,
            MagazineSize = 8,
            ReloadDuration = 1.65,
            AttackInterval = 0.42,
            AttackRange = 11,
            ProjectileSpeed = 10,
            OverrideAttackBehavior = true,
            Accuracy = 82,
            Stability = 70,
            Handling = 64,
            Rarity = 2,
        });
        service.SetAttackSkill(session, firstId, 344);
        service.SetAttackElement(session, secondId, 4);

        Assert.Equal(1, firstId);
        Assert.Equal(2, secondId);
        Assert.True(session.IsDirty);
        Assert.Equal(DocumentSaveStatus.Saved, (await CreateSaveService().SaveAsync(session)).Status);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(sourcePath))!.AsArray();
        var first = root[1]!.AsObject();
        var second = root[2]!.AsObject();
        Assert.Equal("Aster-9", first["name"]!.GetValue<string>());
        Assert.Equal(18, first["params"]![2]!.GetValue<int>());
        Assert.Equal(344, Assert.Single(
            first["traits"]!.AsArray().OfType<JsonObject>(),
            trait => trait["code"]!.GetValue<int>() == 35)["dataId"]!.GetValue<int>());
        Assert.Equal("Vesper-12", second["name"]!.GetValue<string>());
        Assert.Equal(32, second["params"]![2]!.GetValue<int>());
        Assert.Equal(4, Assert.Single(
            second["traits"]!.AsArray().OfType<JsonObject>(),
            trait => trait["code"]!.GetValue<int>() == 31)["dataId"]!.GetValue<int>());
        Assert.Equal("Placeholder", root[3]!["name"]!.GetValue<string>());
        Assert.Equal("<custom:keep>", root[3]!["note"]!.GetValue<string>());

        var metadataProvider = new WeaponAdvancedMetadataProvider();
        var firstMetadata = metadataProvider.Parse(first["note"]!.GetValue<string>());
        Assert.Equal("firearm", firstMetadata.Classification.Family);
        Assert.Equal("pistol", firstMetadata.Classification.Subtype);
        Assert.True(firstMetadata.CombatProfile.Enabled);
        Assert.Null(firstMetadata.CombatProfile.AttackInterval);
        Assert.DoesNotContain("<attackRange:", first["note"]!.GetValue<string>());
        Assert.Equal(15, firstMetadata.Firearm.MagazineSize);
        Assert.Equal(72, firstMetadata.Firearm.Accuracy);
        Assert.DoesNotContain(firstMetadata.Diagnostics, diagnostic =>
            diagnostic.Severity == WeaponNotetagDiagnosticSeverity.Error);
    }

    [Fact]
    public void Metadata_ValidatesInvalidFamilyAndFirearmRanges()
    {
        const string note =
            "<weaponFamily:firearm>\n" +
            "<weaponSubtype:laser>\n" +
            "<handedness:3>\n" +
            "<weaponCombatProfile:true>\n" +
            "<attackRange:0>\n" +
            "<firearmAccuracy:101>\n" +
            "<firearmAimMinDistance:8>\n" +
            "<firearmAimMaxDistance:4>";

        var metadata = new WeaponAdvancedMetadataProvider().Parse(note);

        Assert.Contains(metadata.Diagnostics, issue => issue.Code == "weapon.firearm.subtype.invalid");
        Assert.Contains(metadata.Diagnostics, issue => issue.Code == "weapon.handedness.invalid");
        Assert.Contains(metadata.Diagnostics, issue => issue.Code == "weapon.attack-range.invalid");
        Assert.Contains(metadata.Diagnostics, issue => issue.Code == "weapon.firearm.accuracy.invalid");
        Assert.Contains(metadata.Diagnostics, issue => issue.Code == "weapon.firearm.aim-range.invalid");
    }

    private static async Task<RpgMakerDatabaseDocument> OpenWeaponsAsync(string path)
    {
        var fileSystem = new FileSystemService();
        var service = new DocumentService(new DocumentResolver(
        [
            new RpgMakerDatabaseDocumentProvider(fileSystem),
        ]));
        return Assert.IsType<RpgMakerDatabaseDocument>(await service.OpenAsync(
            new ZiapProject
            {
                Id = "project",
                Name = "Project",
                ProjectType = KnownProjectTypes.RpgMakerMz,
                Path = path,
            },
            new DocumentDescriptor
            {
                Id = new DocumentId("project:rpgmaker:database:weapons"),
                DisplayName = "Armi",
                Kind = DocumentKind.RpgMakerDatabase,
                ResourceId = new Uri("rpgmaker://database/weapons"),
            }));
    }

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
}
