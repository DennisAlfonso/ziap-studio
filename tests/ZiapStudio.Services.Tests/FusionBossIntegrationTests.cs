using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;
using ZiapStudio.Core.Preflight;
using ZiapStudio.Services;
using ZiapStudio.Services.Fusion.Bosses;
using ZiapStudio.Services.Fusion.Preflight.Bosses;
using ZiapStudio.Services.Integrations;
using ZiapStudio.Services.Providers;

namespace ZiapStudio.Services.Tests;

public sealed class FusionBossIntegrationTests
{
    [Fact]
    public async Task ProjectIntegration_AddsBossWorkspaceWhenCorePluginIsActive()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("data/System.json", "{\"gameTitle\":\"Test\"}");
        WritePlugins(workspace);
        var service = new ProjectProviderService(new FileSystemService());

        var roots = await service.BuildExplorerAsync(CreateProject(workspace.RootPath));

        var documentNode = roots.Single(node => node.Name == "System")
            .Children.Single(node => node.Name == "Plugin")
            .Children.Single(node => node.Name == "Fusion Boss Battle");
        Assert.Equal(ProjectExplorerNodeKind.Integration, documentNode.Kind);
        Assert.Equal("fusionboss://workspace/", documentNode.Document!.ResourceId.AbsoluteUri);
        Assert.Equal(
            Path.Combine(workspace.RootPath, "data"),
            documentNode.Path);
    }

    [Fact]
    public async Task Workspace_LoadsLinkedDatabasesAndValidatesMapAnchors()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);
        var service = CreateWorkspaceService();

        var document = await service.LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));

        Assert.True(document.IsValid);
        Assert.Empty(document.Diagnostics);
        Assert.Equal(1, document.GetRecordCount("bosses"));
        Assert.Equal(1, document.GetRecordCount("encounters"));
        Assert.Equal(1, document.GetRecordCount("arenas"));
        Assert.All(document.Databases, database => Assert.True(database.Exists));
    }

    [Fact]
    public async Task Workspace_ReportsBrokenCrossReferencesAndMissingAnchors()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);
        workspace.WriteFile(
            "data/FusionArenas.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "arenas":{
                "testArena":{
                  "encounterId":"missingEncounter",
                  "mapIds":[1],
                  "bindings":{"boss":{"entityId":"missingEntity"}},
                  "anchors":{"bossSpawn":{"count":2}},
                  "completionProfile":"missingCompletion"
                }
              },
              "completionProfiles":{"complete":{}}
            }
            """);

        var document = await CreateWorkspaceService().LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));

        Assert.Contains(document.Diagnostics, issue => issue.Code == "arena.encounter-missing");
        Assert.Contains(document.Diagnostics, issue => issue.Code == "arena.entity-missing");
        Assert.Contains(document.Diagnostics, issue => issue.Code == "arena.completion-profile-missing");
        Assert.Contains(document.Diagnostics, issue => issue.Code == "arena.anchor-count");
        Assert.False(document.IsValid);
    }

    [Fact]
    public async Task Preflight_MapsBossDiagnosticsToNavigableIssues()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);
        workspace.WriteFile(
            "data/FusionEncounters.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "encounters":{
                "testEncounter":{
                  "boss":"missingBoss",
                  "initialPhase":"intro",
                  "phases":{"intro":{}}
                }
              }
            }
            """);
        var fileSystem = new FileSystemService();
        var registry = new RpgMakerPluginRegistryService(fileSystem);
        var service = new FusionBossWorkspaceService(fileSystem, registry);
        var provider = new FusionBossPreflightProvider(
            service,
            new FusionBossIntegrationProvider(registry));

        var issues = await provider.ScanAsync(CreateProject(workspace.RootPath));

        var issue = Assert.Single(issues, issue =>
            issue.RuleId == "fusion-boss.encounter.boss-missing");
        Assert.Equal("FusionBoss", issue.Scope);
        Assert.Equal(PreflightSeverity.Error, issue.Severity);
        Assert.StartsWith("fusionboss://workspace/", issue.NavigationTarget!.AbsoluteUri);
    }

    private static FusionBossWorkspaceService CreateWorkspaceService()
    {
        var fileSystem = new FileSystemService();
        return new FusionBossWorkspaceService(
            fileSystem,
            new RpgMakerPluginRegistryService(fileSystem));
    }

    private static DocumentDescriptor CreateDescriptor(string projectPath) => new()
    {
        Id = new DocumentId("test:integration:fusion-boss"),
        DisplayName = "Fusion Boss Battle",
        Kind = DocumentKind.ProjectIntegration,
        ResourceId = new Uri(FusionBossIntegrationProvider.ResourceUri),
        SourcePath = Path.Combine(projectPath, "data"),
    };

    private static void WritePlugins(TestWorkspace workspace) => workspace.WriteFile(
        "js/plugins.js",
        """
        var $plugins = [
          {"name":"zenkaiDevPlugins/ZDP_FusionCombat","status":true,"parameters":{}},
          {"name":"zenkaiDevPlugins/ZDP_FusionEncounter","status":true,"parameters":{}},
          {"name":"zenkaiDevPlugins/ZDP_FusionArena","status":true,"parameters":{}}
        ];
        """);

    private static void WriteValidWorkspace(TestWorkspace workspace)
    {
        workspace.WriteFile(
            "data/FusionCombat.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "scalingProfiles":{"story":{}},
              "rewardRules":{},
              "enemies":{"bossEntity":{}},
              "bosses":{"testBoss":{"enemy":"bossEntity","scaling":{"profile":"story"}}}
            }
            """);
        workspace.WriteFile(
            "data/FusionEncounters.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "encounters":{
                "testEncounter":{
                  "boss":"testBoss",
                  "initialPhase":"intro",
                  "phases":{"intro":{}}
                }
              }
            }
            """);
        workspace.WriteFile(
            "data/FusionArenas.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "arenas":{
                "testArena":{
                  "encounterId":"testEncounter",
                  "mapIds":[1],
                  "bindings":{"boss":{"entityId":"bossEntity"}},
                  "anchors":{"bossSpawn":{"count":1}},
                  "completionProfile":"complete"
                }
              },
              "completionProfiles":{"complete":{}}
            }
            """);
        workspace.WriteFile(
            "data/FusionPuzzles.json",
            """
            {"schemaVersion":1,"databaseVersion":"1.0.0","puzzles":{}}
            """);
        workspace.WriteFile(
            "data/Map001.json",
            """
            {
              "events":[null,{"id":1,"note":"<FusionArena:testArena>\n<FusionAnchor:bossSpawn>","x":3,"y":4}]
            }
            """);
    }

    private static ZiapProject CreateProject(string path) => new()
    {
        Id = "test",
        Name = "Test",
        ProjectType = KnownProjectTypes.RpgMakerMz,
        Path = path,
        IsZiapInitialized = true,
    };
}
