using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;
using ZiapStudio.Core.Preflight;
using ZiapStudio.Services;
using ZiapStudio.Services.Fusion.Preflight.World;
using ZiapStudio.Services.Fusion.World;
using ZiapStudio.Services.Integrations;
using ZiapStudio.Services.Providers;

namespace ZiapStudio.Services.Tests;

public sealed class FusionWorldIntegrationTests
{
    [Fact]
    public async Task ProjectIntegration_AddsWorldWorkspaceForRpgMakerTilesets()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("data/System.json", "{\"gameTitle\":\"Test\"}");
        workspace.WriteFile("data/Tilesets.json", "[null,{\"id\":1,\"name\":\"Test\",\"tilesetNames\":[]}]");
        var service = new ProjectProviderService(new FileSystemService());

        var roots = await service.BuildExplorerAsync(CreateProject(workspace.RootPath));

        var documentNode = roots.Single(node => node.Name == "System")
            .Children.Single(node => node.Name == "Plugin")
            .Children.Single(node => node.Name == "World & Navigation");
        Assert.Equal("fusionworld://workspace/", documentNode.Document!.ResourceId.AbsoluteUri);
    }

    [Fact]
    public async Task Workspace_BuildsTilesetAssetAndMapDependencyGraph()
    {
        using var workspace = new TestWorkspace();
        WriteWorldData(workspace);
        workspace.WriteBytes("img/tilesets/Dungeon/Dungeon_A1.png", [1, 2, 3, 4]);
        workspace.WriteBytes("img/tilesets/Dungeon/Same.png", [5, 6, 7]);
        workspace.WriteBytes("img/tilesets/Archive/Copy.png", [5, 6, 7]);
        workspace.WriteBytes("img/tilesets/OldStuff.png", [8, 9]);
        var service = new FusionWorldWorkspaceService(new FileSystemService());

        var document = await service.LoadAsync(CreateProject(workspace.RootPath), CreateDescriptor());

        Assert.Equal(2, document.UsedTilesetCount);
        Assert.Equal(2, document.Maps.Count);
        Assert.Equal(1, document.MissingAssetCount);
        Assert.Equal(1, document.OrphanAssetCount);
        Assert.Equal(2, document.DuplicateAssetCount);
        var dungeon = Assert.Single(document.Tilesets, tileset => tileset.Id == 1);
        Assert.Single(dungeon.Maps);
        Assert.Equal("Dungeon/Dungeon_A1", dungeon.Slots[0].AssetPath);
        Assert.True(dungeon.Slots[0].Exists);
        Assert.False(dungeon.Slots[1].Exists);
        var shared = Assert.Single(document.Assets, asset => asset.AssetPath == "Dungeon/Dungeon_A1");
        Assert.Equal(2, shared.Tilesets.Count);
        Assert.Equal(2, shared.Maps.Count);
        Assert.Contains(document.Diagnostics, issue => issue.Code == "asset.missing");
        Assert.Contains(document.Diagnostics, issue => issue.Code == "asset.orphan");
        Assert.Contains(document.Diagnostics, issue => issue.Code == "asset.duplicate");
    }

    [Fact]
    public async Task Preflight_ReportsMissingOrphanAndDuplicateWorldAssets()
    {
        using var workspace = new TestWorkspace();
        WriteWorldData(workspace);
        workspace.WriteBytes("img/tilesets/Dungeon/Dungeon_A1.png", [1, 2, 3, 4]);
        workspace.WriteBytes("img/tilesets/Dungeon/Same.png", [5, 6, 7]);
        workspace.WriteBytes("img/tilesets/Archive/Copy.png", [5, 6, 7]);
        workspace.WriteBytes("img/tilesets/OldStuff.png", [8, 9]);
        var fileSystem = new FileSystemService();
        var provider = new FusionWorldPreflightProvider(
            new FusionWorldWorkspaceService(fileSystem),
            new FusionWorldIntegrationProvider(fileSystem));

        var issues = await provider.ScanAsync(CreateProject(workspace.RootPath));

        Assert.Contains(issues, issue =>
            issue.RuleId == "world.asset.missing" && issue.Severity == PreflightSeverity.Error);
        Assert.Contains(issues, issue => issue.RuleId == "world.asset.orphan");
        Assert.Contains(issues, issue => issue.RuleId == "world.asset.duplicate");
        Assert.All(issues, issue => Assert.Equal("WorldNavigation", issue.Scope));
    }

    private static void WriteWorldData(TestWorkspace workspace)
    {
        workspace.WriteFile(
            "data/Tilesets.json",
            """
            [
              null,
              {"id":1,"name":"Dungeon","tilesetNames":["Dungeon/Dungeon_A1","Missing","","","","Dungeon/Same","Archive/Copy"]},
              {"id":2,"name":"Dungeon riuso","tilesetNames":["Dungeon/Dungeon_A1"]}
            ]
            """);
        workspace.WriteFile(
            "data/MapInfos.json",
            """
            [null,{"id":1,"name":"Ingresso"},{"id":2,"name":"Profondita"}]
            """);
        workspace.WriteFile("data/Map001.json", "{\"tilesetId\":1}");
        workspace.WriteFile("data/Map002.json", "{\"tilesetId\":2}");
    }

    private static DocumentDescriptor CreateDescriptor() => new()
    {
        Id = new DocumentId("test:integration:fusion-world"),
        DisplayName = "World & Navigation",
        Kind = DocumentKind.ProjectIntegration,
        ResourceId = new Uri(FusionWorldIntegrationProvider.ResourceUri),
    };

    private static ZiapProject CreateProject(string path) => new()
    {
        Id = "test",
        Name = "Test",
        ProjectType = KnownProjectTypes.RpgMakerMz,
        Path = path,
        IsZiapInitialized = true,
    };
}
