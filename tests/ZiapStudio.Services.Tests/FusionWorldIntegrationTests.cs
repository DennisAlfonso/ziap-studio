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

    [Fact]
    public async Task Workspace_ReadsRegionLayerAndOptionalMetadataRegistry()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "data/Tilesets.json",
            "[null,{\"id\":1,\"name\":\"Test\",\"tilesetNames\":[]}]");
        workspace.WriteFile("data/MapInfos.json", "[null,{\"id\":1,\"name\":\"Arena\"}]");
        workspace.WriteFile(
            "data/Map001.json",
            $"{{\"width\":2,\"height\":2,\"tilesetId\":1,\"data\":[{BuildRegionData()}]}}");
        workspace.WriteFile(
            ".ziap/world/regions.json",
            """
            {
              "232":{"name":"Encounter Spawn","category":"Combat","owner":"FusionEncounter","reserved":true},
              "254":{"name":"Hard Block","category":"Traversal","owner":"Movement","reserved":true}
            }
            """);
        var service = new FusionWorldWorkspaceService(new FileSystemService());

        var document = await service.LoadAsync(CreateProject(workspace.RootPath), CreateDescriptor());

        Assert.True(document.RegionRegistryExists);
        Assert.Equal(2, document.ActiveRegionCount);
        Assert.Equal(2, document.RegisteredRegionCount);
        var encounterSpawn = Assert.Single(document.Regions, region => region.Id == 232);
        Assert.Equal("Encounter Spawn", encounterSpawn.Name);
        Assert.Equal("Combat", encounterSpawn.Category);
        Assert.True(encounterSpawn.IsReserved);
        Assert.Equal(2, encounterSpawn.CellCount);
        Assert.Equal("Arena", Assert.Single(encounterSpawn.Usages).MapName);
        var hardBlock = Assert.Single(document.Regions, region => region.Id == 254);
        Assert.Equal(1, hardBlock.CellCount);
        Assert.DoesNotContain(document.Diagnostics, issue => issue.Code == "region.unregistered");
    }

    [Fact]
    public async Task Workspace_PreservesMapDataAndTilesetFlagsForSharedTilemapPreview()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "data/Tilesets.json",
            """
            [null,{
              "id":1,
              "name":"Dungeon",
              "tilesetNames":["Dungeon/A1","","","","","Dungeon/B","","",""],
              "flags":[0,15]
            }]
            """);
        workspace.WriteFile("data/MapInfos.json", "[null,{\"id\":1,\"name\":\"Ingresso\"}]");
        workspace.WriteFile(
            "data/Map001.json",
            $"{{\"width\":2,\"height\":2,\"scrollType\":3,\"tilesetId\":1,\"data\":[{BuildRegionData()}]}}");
        var service = new FusionWorldWorkspaceService(new FileSystemService());

        var document = await service.LoadAsync(CreateProject(workspace.RootPath), CreateDescriptor());

        var map = Assert.Single(document.Maps);
        Assert.Equal(1, map.MapId);
        Assert.Equal(2, map.Width);
        Assert.Equal(2, map.Height);
        Assert.Equal(3, map.ScrollType);
        Assert.Equal("Dungeon/A1", map.TilesetNames[0]);
        Assert.Equal([0, 15], map.TilesetFlags);
        Assert.Equal(24, map.MapData.Count);
    }

    [Fact]
    public async Task Preflight_ReportsUsedRegionsWithoutMetadata()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "data/Tilesets.json",
            "[null,{\"id\":1,\"name\":\"Test\",\"tilesetNames\":[]}]");
        workspace.WriteFile(
            "data/Map001.json",
            $"{{\"width\":2,\"height\":2,\"tilesetId\":1,\"data\":[{BuildRegionData()}]}}");
        var fileSystem = new FileSystemService();
        var provider = new FusionWorldPreflightProvider(
            new FusionWorldWorkspaceService(fileSystem),
            new FusionWorldIntegrationProvider(fileSystem));

        var issues = await provider.ScanAsync(CreateProject(workspace.RootPath));

        Assert.Contains(issues, issue =>
            issue.RuleId == "world.region.unregistered" &&
            issue.Severity == PreflightSeverity.Warning &&
            issue.Scope == "WorldNavigation");
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

    private static string BuildRegionData() => string.Join(
        ',',
        Enumerable.Repeat(0, 20).Concat([232, 232, 254, 0]));

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
