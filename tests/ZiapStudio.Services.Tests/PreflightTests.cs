using System.Text.Json.Nodes;
using ZiapStudio.Core.Models;
using ZiapStudio.Core.Preflight;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Fusion.Preflight;
using ZiapStudio.Services.Fusion.Preflight.Weapons;
using ZiapStudio.Services.Fusion.Weapons;

namespace ZiapStudio.Services.Tests;

public sealed class PreflightTests
{
    [Fact]
    public async Task WeaponProfile_ScansOnlyRealWeaponsAndAppliesInitialPolicy()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "data/Weapons.json",
            """
            [
              null,
              {"id":1,"name":"Incompleta","wtypeId":1,"note":""},
              {"id":2,"name":"Segnaposto","wtypeId":0,"note":""},
              {"id":3,"name":"Non valida","wtypeId":1,"note":"<itemRare:8>\n<perk:a,b>\n<lvReq:0>"},
              {"id":4,"name":"Cataloghi","wtypeId":1,"note":"<itemRare:0>\n<perk:sconosciuto,random,nullo>\n<lvReq:1>\n<cp[99]: 2>\n<Disassemble Pool>\nx1 {db[0].missing}\n</Disassemble Pool>"}
            ]
            """);
        var fileSystem = new FileSystemService();
        var provider = new WeaponPreflightProvider(
            fileSystem,
            new WeaponNotetagCatalogProvider(fileSystem));

        var issues = await provider.ScanAsync(CreateProject(workspace.RootPath));

        Assert.Contains(issues, issue =>
            issue.RecordId == 1 && issue.RuleId == "weapon.rarity.missing" &&
            issue.Severity == PreflightSeverity.Error);
        Assert.Contains(issues, issue =>
            issue.RecordId == 1 && issue.RuleId == "weapon.perk.missing" &&
            issue.Severity == PreflightSeverity.Warning);
        Assert.Contains(issues, issue =>
            issue.RecordId == 3 && issue.RuleId == "weapon.rarity.invalid");
        Assert.Contains(issues, issue =>
            issue.RecordId == 3 && issue.RuleId == "weapon.perk.columns");
        Assert.Contains(issues, issue =>
            issue.RecordId == 3 && issue.RuleId == "weapon.required-level.invalid");
        Assert.Contains(issues, issue =>
            issue.RecordId == 4 && issue.RuleId == "weapon.perk.unknown");
        Assert.Contains(issues, issue =>
            issue.RecordId == 4 && issue.RuleId == "weapon.custom-parameter.unknown" &&
            issue.Severity == PreflightSeverity.Warning);
        Assert.Contains(issues, issue =>
            issue.RecordId == 4 && issue.RuleId == "weapon.disassembly.resource");
        Assert.DoesNotContain(issues, issue => issue.RecordId == 2);
    }

    [Fact]
    public async Task Scanner_SeparatesActiveIgnoredAndObsoleteSuppressions()
    {
        var activeIssue = CreateIssue("rule.active", 4);
        var ignoredIssue = CreateIssue("rule.ignored", 5);
        var ignoredSuppression = CreateSuppression(ignoredIssue);
        var obsoleteSuppression = new PreflightSuppression
        {
            RuleId = "rule.obsolete",
            Scope = "Weapons",
            RecordId = 99,
            IgnoredAt = DateTimeOffset.UtcNow,
        };
        var scanner = new PreflightScanner([new FixedProvider(activeIssue, ignoredIssue)]);

        var result = await scanner.ScanAsync(
            CreateProject("C:\\project"),
            [ignoredSuppression, obsoleteSuppression]);

        Assert.Equal(activeIssue, Assert.Single(result.ActiveIssues));
        Assert.Equal(ignoredIssue, Assert.Single(result.IgnoredIssues).Issue);
        Assert.Equal(obsoleteSuppression, Assert.Single(result.ObsoleteSuppressions));
    }

    [Fact]
    public async Task SuppressionStore_PersistsUnderZiapMetadataWithoutTouchingUnknownFields()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            ".ziap/project.json",
            """
            {"schemaVersion":1,"id":"test","name":"Test","custom":{"keep":true}}
            """);
        var weaponsPath = workspace.WriteFile(
            "data/Weapons.json",
            "[null,{\"id\":1,\"wtypeId\":1,\"note\":\"\"}]");
        var weaponsBefore = await File.ReadAllBytesAsync(weaponsPath);
        var fileSystem = new FileSystemService();
        var store = new PreflightSuppressionStore(
            fileSystem,
            new AtomicJsonFileWriter(fileSystem));
        var issue = CreateIssue("weapon.rarity.missing", 7);
        var suppression = CreateSuppression(issue) with { Reason = "Eccezione di design" };

        await store.IgnoreAsync(workspace.RootPath, suppression);

        Assert.Equal(suppression, Assert.Single(await store.LoadAsync(workspace.RootPath)));
        var root = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(workspace.RootPath, ".ziap", "project.json")))!;
        Assert.True(root["custom"]!["keep"]!.GetValue<bool>());
        Assert.Equal(weaponsBefore, await File.ReadAllBytesAsync(weaponsPath));

        await store.RestoreAsync(workspace.RootPath, issue.Identity);
        Assert.Empty(await store.LoadAsync(workspace.RootPath));
    }

    private static ZiapProject CreateProject(string path) => new()
    {
        Id = "test",
        Name = "Test",
        ProjectType = KnownProjectTypes.RpgMakerMz,
        Path = path,
        IsZiapInitialized = true,
    };

    private static PreflightIssue CreateIssue(string ruleId, int recordId) => new()
    {
        RuleId = ruleId,
        Scope = "Weapons",
        RecordId = recordId,
        RecordName = $"Arma {recordId}",
        Severity = PreflightSeverity.Error,
        Message = "Problema",
    };

    private static PreflightSuppression CreateSuppression(PreflightIssue issue) => new()
    {
        RuleId = issue.RuleId,
        Scope = issue.Scope,
        RecordId = issue.RecordId,
        IgnoredAt = DateTimeOffset.UtcNow,
    };

    private sealed class FixedProvider(params PreflightIssue[] issues) : IPreflightProvider
    {
        public string Scope => "Weapons";

        public Task<IReadOnlyList<PreflightIssue>> ScanAsync(
            ZiapProject project,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PreflightIssue>>(issues);
    }
}
