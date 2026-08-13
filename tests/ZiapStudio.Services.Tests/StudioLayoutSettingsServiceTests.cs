using System.Text.Json;

namespace ZiapStudio.Services.Tests;

public sealed class StudioLayoutSettingsServiceTests
{
    [Fact]
    public async Task SaveAndLoadAsync_PreservesWindowAndPanelLayout()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.RootPath, "settings", "layout.json");
        var service = new StudioLayoutSettingsService(new FileSystemService(), path);
        var expected = new StudioLayoutSettings
        {
            WindowX = 120,
            WindowY = 80,
            WindowWidth = 1720,
            WindowHeight = 980,
            IsMaximized = true,
            IsProjectPaneExpanded = true,
            ExplorerWidth = 372,
            DatabaseListWidth = 710,
        };

        await service.SaveAsync(expected);
        var actual = await service.LoadAsync();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaultsForInvalidJson()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteFile("layout.json", "{ invalid json");
        var service = new StudioLayoutSettingsService(new FileSystemService(), path);

        var result = await service.LoadAsync();

        Assert.Equal(new StudioLayoutSettings(), result);
    }

    [Fact]
    public async Task LoadAsync_ClampsUnsafePanelAndWindowDimensions()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.WriteFile(
            "layout.json",
            JsonSerializer.Serialize(new StudioLayoutSettings
            {
                WindowWidth = 10,
                WindowHeight = 20,
                ExplorerWidth = 9000,
                DatabaseListWidth = 1,
            }));
        var service = new StudioLayoutSettingsService(new FileSystemService(), path);

        var result = await service.LoadAsync();

        Assert.Equal(1100, result.WindowWidth);
        Assert.Equal(700, result.WindowHeight);
        Assert.Equal(550, result.ExplorerWidth);
        Assert.Equal(360, result.DatabaseListWidth);
    }
}
