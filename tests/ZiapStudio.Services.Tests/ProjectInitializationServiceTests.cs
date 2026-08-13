using System.Text.Json;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Initialization;

namespace ZiapStudio.Services.Tests;

public sealed class ProjectInitializationServiceTests
{
    private readonly FileSystemService _fileSystem = new();
    private readonly ProjectIdGenerator _projectIdGenerator = new();

    [Fact]
    public async Task InitializeAsync_CreatesMinimalMetadataAndResolverReloadsIt()
    {
        using var workspace = new TestWorkspace();
        var service = new ProjectInitializationService(_fileSystem, _projectIdGenerator);

        await service.InitializeAsync(
            workspace.RootPath,
            new ProjectInitializationOptions
            {
                Id = "fusion-hexella-dive",
                Name = "Fusion: Hexella Dive",
                Type = KnownProjectTypes.RpgMakerMz,
                Version = null,
                Publisher = "Zenkaiverse",
            });

        var metadataPath = Path.Combine(workspace.RootPath, ".ziap", "project.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
        var root = document.RootElement;

        Assert.Equal(6, root.EnumerateObject().Count());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("fusion-hexella-dive", root.GetProperty("id").GetString());
        Assert.Equal("Fusion: Hexella Dive", root.GetProperty("name").GetString());
        Assert.Equal("rpgmaker-mz", root.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("version").ValueKind);
        Assert.Equal("Zenkaiverse", root.GetProperty("publisher").GetString());
        Assert.False(root.TryGetProperty("package", out _));

        var reloadedProject = await new ProjectService(_fileSystem).LoadAsync(workspace.RootPath);
        Assert.True(reloadedProject.IsZiapInitialized);
        Assert.Equal("fusion-hexella-dive", reloadedProject.Id);
        Assert.Equal("Fusion: Hexella Dive", reloadedProject.Name);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotOverwriteExistingIdentity()
    {
        using var workspace = new TestWorkspace();
        const string existingMetadata =
            """{ "schemaVersion": 1, "id": "stable-id", "name": "Existing", "type": "web" }""";
        var metadataPath = workspace.WriteFile(".ziap/project.json", existingMetadata);
        var service = new ProjectInitializationService(_fileSystem, _projectIdGenerator);

        var exception = await Assert.ThrowsAsync<ProjectInitializationException>(
            () => service.InitializeAsync(
                workspace.RootPath,
                CreateOptions("replacement-id")));

        Assert.Contains("non verrà sovrascritto", exception.Message);
        Assert.Equal(existingMetadata, await File.ReadAllTextAsync(metadataPath));
    }

    [Fact]
    public async Task InitializeAsync_RejectsInvalidIdWithoutWritingProject()
    {
        using var workspace = new TestWorkspace();
        var service = new ProjectInitializationService(_fileSystem, _projectIdGenerator);

        await Assert.ThrowsAsync<ProjectInitializationException>(
            () => service.InitializeAsync(
                workspace.RootPath,
                CreateOptions("Invalid ID")));

        Assert.False(Directory.Exists(Path.Combine(workspace.RootPath, ".ziap")));
    }

    private static ProjectInitializationOptions CreateOptions(string projectId) => new()
    {
        Id = projectId,
        Name = "Project",
        Type = KnownProjectTypes.Web,
        Version = null,
        Publisher = null,
    };
}
