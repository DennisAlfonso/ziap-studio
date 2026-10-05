using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Models;
using ZiapStudio.Services;
using ZiapStudio.Services.Fusion.Story;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Tests;

public sealed class SemanticEventReaderTests
{
    [Fact]
    public async Task Workspace_ProjectsSemanticCommandsAndKeepsEveryInputByteIdentical()
    {
        using var workspace = new TestWorkspace();
        WriteFixture(workspace);
        var trackedFiles = new[]
        {
            "data/Map001.json", "data/CommonEvents.json", "data/System.json", "data/MapInfos.json",
            "data/Actors.json", "data/Items.json", "data/Weapons.json", "data/Armors.json", "data/Animations.json",
        };
        var before = trackedFiles.ToDictionary(
            path => path,
            path => File.ReadAllBytes(Path.Combine(workspace.RootPath, path)));

        var document = await CreateService().LoadAsync(Project(workspace.RootPath), Descriptor());
        var blocks = document.Workspace.Maps.Single(map => map.Id == 1)
            .Events.Single(@event => @event.Id == 1).Pages.Single().Blocks;

        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Wait && block.DisplayText == "60 frames · ~1.0 s");
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.SwitchVariable &&
            block.DisplayText.Contains("Switch #18 \"Prologo completato\" → ON"));
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.SwitchVariable &&
            block.DisplayText.Contains("Variable #11 \"Jump Value\" Set → 12"));
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Transfer &&
            block.DisplayText.Contains("007 — Laboratorio") && block.DisplayText.Contains("Facing Down"));
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Audio &&
            block.DisplayText.Contains("DestinyOfBirth · Volume 80 · Pitch 100 · Pan 0"));
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Animation &&
            block.DisplayText.Contains("Player · Animation #3 \"Flash\""));
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Animation &&
            block.DisplayText.Contains("Event #2 \"Guide\" · Exclamation"));
        var route = Assert.Single(blocks.Where(block => block.Kind == StoryBlockKind.MovementRoute));
        Assert.Equal(11, route.CommandStartIndex);
        Assert.Equal(12, route.CommandEndIndex);
        Assert.Equal(2, route.SourceCommands.Count);
        Assert.Contains(route.Details, detail => detail.Contains("Turn Left"));
        Assert.Contains(route.Details, detail => detail.Contains("Move Forward"));
        Assert.Contains(route.Details, detail => detail.Contains("Wait 20 frames"));
        Assert.Contains(route.Details, detail => detail.Contains("Switch ON Switch #18 \"Prologo completato\""));
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.ControlFlow &&
            block.DisplayText.Contains("Switch #18 \"Prologo completato\" is ON") && block.Indent == 0);
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Raw && block.SourceCommands.Single().Code == 999);
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Raw && block.SourceCommands.Single().Code == 230);
        Assert.Contains(document.Workspace.Diagnostics, diagnostic => diagnostic.Code == "story.wait-malformed");
        Assert.All(blocks, block => Assert.NotEmpty(block.SourceCommands));

        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(workspace.RootPath, path)));
        }
    }

    [Theory]
    [InlineData("Prologo completato", StorySearchResultKind.Logic)]
    [InlineData("Laboratorio", StorySearchResultKind.Transfer)]
    [InlineData("DestinyOfBirth", StorySearchResultKind.Audio)]
    public async Task Search_IndexesSemanticDisplayText(string query, StorySearchResultKind expectedKind)
    {
        using var workspace = new TestWorkspace();
        WriteFixture(workspace);
        var document = await CreateService().LoadAsync(Project(workspace.RootPath), Descriptor());

        var result = StorySearchIndex.Build(document.Workspace).Search(new StorySearchQuery { Text = query });

        Assert.Contains(result.Results, entry => entry.Kind == expectedKind);
    }

    private static FusionStoryWorkspaceService CreateService()
    {
        var fileSystem = new FileSystemService();
        return new FusionStoryWorkspaceService(fileSystem, new RpgMakerStoryCommandParser(
            new CompositeLocalizationTextResolver(new LocalizationService([
                new FusionLocalizationProvider(fileSystem),
            ]))));
    }

    private static void WriteFixture(TestWorkspace workspace)
    {
        workspace.WriteFile("data/MapInfos.json", """
            [null,{"id":1,"name":"Intro","order":1},{"id":7,"name":"Laboratorio","order":7}]
            """);
        workspace.WriteFile("data/System.json", JsonSerializer.Serialize(new
        {
            switches = Names(18, "Prologo completato"),
            variables = Names(11, "Jump Value"),
        }));
        workspace.WriteFile("data/Actors.json", "[null,{\"id\":1,\"name\":\"Polka\"}]");
        workspace.WriteFile("data/Items.json", "[null,{\"id\":1,\"name\":\"Key\"}]");
        workspace.WriteFile("data/Weapons.json", "[null,{\"id\":1,\"name\":\"Blade\"}]");
        workspace.WriteFile("data/Armors.json", "[null,{\"id\":1,\"name\":\"Coat\"}]");
        workspace.WriteFile("data/Animations.json", "[null,null,null,{\"id\":3,\"name\":\"Flash\"}]");
        workspace.WriteFile("data/CommonEvents.json", "[null,{\"id\":1,\"name\":\"Common\",\"list\":[{\"code\":0,\"indent\":0,\"parameters\":[]}]}]");
        workspace.WriteFile("data/Map007.json", "{\"events\":[]}");
        workspace.WriteFile("data/Map001.json", """
            {"events":[null,
              {"id":1,"name":"Cutscene","pages":[{"trigger":0,"conditions":{},"list":[
                {"code":111,"indent":0,"parameters":[0,18,0]},
                {"code":101,"indent":1,"parameters":["",0,0,2,""]},
                {"code":401,"indent":1,"parameters":["Literal dialogue"]},
                {"code":230,"indent":1,"parameters":[60]},
                {"code":121,"indent":1,"parameters":[18,18,0]},
                {"code":122,"indent":1,"parameters":[11,11,0,0,12]},
                {"code":123,"indent":1,"parameters":["A",1]},
                {"code":201,"indent":1,"parameters":[0,7,12,8,2,0]},
                {"code":241,"indent":1,"parameters":[{"name":"DestinyOfBirth","volume":80,"pitch":100,"pan":0}]},
                {"code":212,"indent":1,"parameters":[-1,3,true]},
                {"code":213,"indent":1,"parameters":[2,1,false]},
                {"code":205,"indent":1,"parameters":[0,{"repeat":false,"skippable":true,"wait":true,"list":[{"code":17,"parameters":[]},{"code":12,"parameters":[]},{"code":15,"parameters":[20]},{"code":26,"parameters":[18]},{"code":0,"parameters":[]}]}]},
                {"code":505,"indent":1,"parameters":[{"code":17,"parameters":[]}]},
                {"code":230,"indent":1,"parameters":[]},
                {"code":999,"indent":1,"parameters":["keep",42]},
                {"code":411,"indent":0,"parameters":[]},
                {"code":412,"indent":0,"parameters":[]},
                {"code":0,"indent":0,"parameters":[]}
              ]}]},
              {"id":2,"name":"Guide","pages":[]}
            ]}
            """);
    }

    private static string?[] Names(int index, string name)
    {
        var result = new string?[index + 1];
        result[index] = name;
        return result;
    }

    private static ZiapProject Project(string path) => new()
    {
        Id = "test", Name = "Test", Path = path, ProjectType = KnownProjectTypes.RpgMakerMz, IsZiapInitialized = true,
    };

    private static DocumentDescriptor Descriptor() => new()
    {
        Id = new DocumentId("test:semantic-reader"), DisplayName = "Story", Kind = DocumentKind.ProjectIntegration,
        ResourceId = new Uri("fusionstory://workspace/"),
    };
}
