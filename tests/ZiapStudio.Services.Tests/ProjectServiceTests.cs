using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Tests;

public sealed class ProjectServiceTests
{
    private readonly ProjectService _service = new(new FileSystemService());

    [Fact]
    public async Task LoadAsync_PrefersZiapMetadataAndReadsPackageName()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            ".ziap/project.json",
            """
            {
              "schemaVersion": 1,
              "id": "fusion-hexella-dive",
              "name": "Fusion: Hexella Dive",
              "version": "0.8.0-studio",
              "type": "rpgmaker-mz",
              "publisher": "Zenkaiverse"
            }
            """);
        workspace.WriteFile(
            "package.json",
            """{ "name": "fusion-hexella-dive", "version": "0.8.0" }""");
        workspace.WriteFile("js/rmmz_core.js");
        workspace.WriteFile(
            "data/System.json",
            """{ "gameTitle": "Engine title overridden by ZIAP" }""");

        var project = await _service.LoadAsync(workspace.RootPath);

        Assert.Equal("fusion-hexella-dive", project.Id);
        Assert.Equal("Fusion: Hexella Dive", project.Name);
        Assert.Equal("fusion-hexella-dive", project.PackageName);
        Assert.Equal("0.8.0-studio", project.Version);
        Assert.Equal(KnownProjectTypes.RpgMakerMz, project.ProjectType);
        Assert.Equal("Zenkaiverse", project.Publisher);
        Assert.Equal(workspace.RootPath, project.Path);
        Assert.True(project.IsZiapInitialized);
    }

    [Fact]
    public async Task LoadAsync_UsesRpgMakerGameTitleWithoutTreatingPackageAsIdentity()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "package.json",
            """{ "name": "rmmz-game" }""");
        workspace.WriteFile("Game.rmmzproject");
        workspace.WriteFile(
            "data/System.json",
            """{ "gameTitle": "Fusion: Hexella Dive", "versionId": 96786120 }""");

        var project = await _service.LoadAsync(workspace.RootPath);

        var directoryName = new DirectoryInfo(workspace.RootPath).Name;
        Assert.Equal(directoryName, project.Id);
        Assert.Equal("Fusion: Hexella Dive", project.Name);
        Assert.Equal("rmmz-game", project.PackageName);
        Assert.Null(project.Version);
        Assert.Equal(KnownProjectTypes.RpgMakerMz, project.ProjectType);
        Assert.Equal("RPG Maker MZ", project.ProjectTypeDisplayName);
        Assert.False(project.IsZiapInitialized);
        Assert.False(File.Exists(Path.Combine(workspace.RootPath, ".ziap", "project.json")));
    }

    [Fact]
    public async Task LoadAsync_UsesFolderNameAsIdentityForNodePackage()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "package.json",
            """{ "name": "myzenkai-ziap-editor", "version": "6.0.0-web-beta.1" }""");
        workspace.WriteFile("index.html");

        var project = await _service.LoadAsync(workspace.RootPath);

        var directoryName = new DirectoryInfo(workspace.RootPath).Name;
        Assert.Equal(directoryName, project.Id);
        Assert.Equal(directoryName, project.Name);
        Assert.Equal("myzenkai-ziap-editor", project.PackageName);
        Assert.Equal("6.0.0-web-beta.1", project.Version);
        Assert.Equal(KnownProjectTypes.Web, project.ProjectType);
    }

    [Fact]
    public async Task LoadAsync_RejectsUnrecognizedFolder()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("notes.txt", "not a project");

        var exception = await Assert.ThrowsAsync<ProjectLoadException>(
            () => _service.LoadAsync(workspace.RootPath));

        Assert.Contains("non contiene", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsInvalidZiapSchema()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            ".ziap/project.json",
            """{ "schemaVersion": 0, "name": "Invalid" }""");

        var exception = await Assert.ThrowsAsync<ProjectLoadException>(
            () => _service.LoadAsync(workspace.RootPath));

        Assert.Contains("schemaVersion", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_ReportsInvalidRpgMakerSystemMetadata()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("Game.rmmzproject");
        workspace.WriteFile("data/System.json", "{ invalid json");

        var exception = await Assert.ThrowsAsync<ProjectLoadException>(
            () => _service.LoadAsync(workspace.RootPath));

        Assert.Contains("data/System.json", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsUnsupportedFutureZiapSchema()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            ".ziap/project.json",
            """{ "schemaVersion": 2, "id": "future", "name": "Future" }""");

        var exception = await Assert.ThrowsAsync<ProjectLoadException>(
            () => _service.LoadAsync(workspace.RootPath));

        Assert.Contains("supporta fino", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsMissingDirectory()
    {
        using var workspace = new TestWorkspace();
        var missingPath = Path.Combine(workspace.RootPath, "missing");

        var exception = await Assert.ThrowsAsync<ProjectLoadException>(
            () => _service.LoadAsync(missingPath));

        Assert.Contains("non esiste", exception.Message);
    }
}
