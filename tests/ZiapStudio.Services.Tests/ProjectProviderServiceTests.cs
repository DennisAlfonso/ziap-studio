using ZiapStudio.Core.Models;
using ZiapStudio.Core.Documents;
using ZiapStudio.Services.Providers;

namespace ZiapStudio.Services.Tests;

public sealed class ProjectProviderServiceTests
{
    [Fact]
    public async Task BuildExplorerAsync_BuildsSemanticRpgMakerMzTree()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("data/Actors.json", "[]");
        workspace.WriteFile("data/Weapons.json", "[]");
        workspace.WriteFile("data/CommonEvents.json", "[]");
        workspace.WriteFile("data/FusionQuests.json", "[]");
        workspace.WriteFile("data/Map001.json", "{}");
        workspace.WriteFile("data/Map002.json", "{}");
        workspace.WriteFile(
            "data/MapInfos.json",
            """
            [null,
              { "id": 1, "name": "Prologo", "order": 2 },
              { "id": 2, "name": "Laboratorio", "order": 1 }
            ]
            """);
        workspace.WriteFile("js/plugins/FusionCore.js");
        workspace.WriteFile("img/pictures/placeholder.txt");
        workspace.WriteFile("audio/bgm/placeholder.txt");
        workspace.WriteFile("fonts/gamefont.css");
        workspace.WriteFile("package.json", "{}");
        workspace.WriteFile("Game.rmmzproject");

        var service = new ProjectProviderService(new FileSystemService());
        var nodes = await service.BuildExplorerAsync(
            CreateProject(workspace.RootPath, KnownProjectTypes.RpgMakerMz));

        Assert.Equal(["Database", "World", "System", "Assets", "Files"], nodes.Select(node => node.Name));

        var database = Assert.Single(nodes, node => node.Name == "Database");
        Assert.Contains(database.Children, node => node.Name == "Attori");
        var weapons = Assert.Single(database.Children, node => node.Name == "Armi");
        Assert.NotNull(weapons.Document);
        Assert.Equal(DocumentKind.RpgMakerDatabase, weapons.Document.Kind);
        Assert.Equal("Armi", weapons.Document.DisplayName);
        Assert.Equal("rpgmaker://database/weapons", weapons.Document.ResourceId.AbsoluteUri);
        var customData = Assert.Single(database.Children, node => node.Name == "Dati personalizzati");
        Assert.Contains(customData.Children, node => node.Name == "FusionQuests");

        var world = Assert.Single(nodes, node => node.Name == "World");
        var maps = Assert.Single(world.Children, node => node.Name == "Mappe");
        Assert.Equal(["002 — Laboratorio", "001 — Prologo"], maps.Children.Select(node => node.Name));
        Assert.Contains(world.Children, node => node.Name == "Eventi comuni");

        var system = Assert.Single(nodes, node => node.Name == "System");
        var plugins = Assert.Single(system.Children, node => node.Name == "Plugin");
        Assert.Contains(plugins.Children, node => node.Name == "FusionCore");

        var assets = Assert.Single(nodes, node => node.Name == "Assets");
        Assert.Equal(["Immagini", "Audio", "Font"], assets.Children.Select(node => node.Name));
    }

    [Fact]
    public async Task BuildExplorerAsync_UsesGenericFilesProviderForOtherTypes()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("src/app.ts", "export {};");
        workspace.WriteFile("README.md", "# Project");
        workspace.WriteFile(".git/HEAD", "ref: refs/heads/main");

        var service = new ProjectProviderService(new FileSystemService());
        var nodes = await service.BuildExplorerAsync(
            CreateProject(workspace.RootPath, KnownProjectTypes.Web));

        var files = Assert.Single(nodes);
        Assert.Equal("Files", files.Name);
        Assert.Contains(files.Children, node => node.Name == "src");
        Assert.Contains(files.Children, node => node.Name == "README.md");
        Assert.DoesNotContain(files.Children, node => node.Name == ".git");
    }

    [Fact]
    public async Task BuildExplorerAsync_ReportsUnreadableMapMetadataInsideTree()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("data/MapInfos.json", "{ invalid json");

        var service = new ProjectProviderService(new FileSystemService());
        var nodes = await service.BuildExplorerAsync(
            CreateProject(workspace.RootPath, KnownProjectTypes.RpgMakerMz));

        var maps = nodes
            .Single(node => node.Name == "World")
            .Children.Single(node => node.Name == "Mappe");
        Assert.Contains(maps.Children, node => node.Name == "MapInfos.json non leggibile");
    }

    private static ZiapProject CreateProject(string path, string projectType) => new()
    {
        Id = "project",
        Name = "Project",
        ProjectType = projectType,
        Path = path,
    };
}
