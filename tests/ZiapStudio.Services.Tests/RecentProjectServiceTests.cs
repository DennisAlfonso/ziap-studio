using System.Text.Json;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Tests;

public sealed class RecentProjectServiceTests
{
    [Fact]
    public async Task AddAsync_PersistsNewestProjectFirstAndDeduplicatesPath()
    {
        using var workspace = new TestWorkspace();
        var settingsPath = Path.Combine(workspace.RootPath, "settings", "settings.json");
        var service = new RecentProjectService(new FileSystemService(), settingsPath);
        var first = CreateProject("First", Path.Combine(workspace.RootPath, "first"));
        var second = CreateProject("Second", Path.Combine(workspace.RootPath, "second"));
        var refreshedFirst = first with { Version = "2.0.0" };

        await service.AddAsync(first);
        await service.AddAsync(second);
        var recentProjects = await service.AddAsync(refreshedFirst);

        Assert.Collection(
            recentProjects,
            project =>
            {
                Assert.Equal("First", project.Name);
                Assert.Equal("2.0.0", project.Version);
            },
            project => Assert.Equal("Second", project.Name));

        var reloaded = await service.GetRecentProjectsAsync();
        Assert.Equal(recentProjects, reloaded);
    }

    [Fact]
    public async Task AddAsync_KeepsAtMostTenProjects()
    {
        using var workspace = new TestWorkspace();
        var settingsPath = Path.Combine(workspace.RootPath, "settings.json");
        var service = new RecentProjectService(new FileSystemService(), settingsPath);

        for (var index = 0; index < 12; index++)
        {
            await service.AddAsync(
                CreateProject($"Project {index}", Path.Combine(workspace.RootPath, $"project-{index}")));
        }

        var recentProjects = await service.GetRecentProjectsAsync();

        Assert.Equal(RecentProjectService.MaximumRecentProjects, recentProjects.Count);
        Assert.Equal("Project 11", recentProjects[0].Name);
        Assert.Equal("Project 2", recentProjects[^1].Name);
    }

    [Fact]
    public async Task GetRecentProjectsAsync_ReportsInvalidSettings()
    {
        using var workspace = new TestWorkspace();
        var settingsPath = workspace.WriteFile("settings.json", "{ invalid json");
        var service = new RecentProjectService(new FileSystemService(), settingsPath);

        await Assert.ThrowsAsync<SettingsException>(() => service.GetRecentProjectsAsync());
    }

    [Fact]
    public async Task AddAsync_WritesSchemaVersion()
    {
        using var workspace = new TestWorkspace();
        var settingsPath = Path.Combine(workspace.RootPath, "settings.json");
        var service = new RecentProjectService(new FileSystemService(), settingsPath);

        await service.AddAsync(CreateProject("Project", Path.Combine(workspace.RootPath, "project")));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    private static ZiapProject CreateProject(string name, string path) => new()
    {
        Id = name.ToLowerInvariant().Replace(' ', '-'),
        Name = name,
        PackageName = name.ToLowerInvariant().Replace(' ', '-'),
        Version = "1.0.0",
        ProjectType = KnownProjectTypes.Web,
        Publisher = "Zenkaiverse",
        Path = path,
    };
}
