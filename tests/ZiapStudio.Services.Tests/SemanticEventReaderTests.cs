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
            "data/States.json", "data/Skills.json",
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
        Assert.Contains(route.Details, detail => detail.Contains("Script: Jump to: 58, 8"));
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

    [Fact]
    public async Task Workspace_OpensConfiguredExternalProjectWithoutMutatingInputs()
    {
        var projectPath = Environment.GetEnvironmentVariable("ZIAP_STORY_REAL_PROJECT");
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        var dataPath = Path.Combine(projectPath, "data");
        var sourceFiles = Directory.EnumerateFiles(dataPath, "Map*.json")
            .Append(Path.Combine(dataPath, "CommonEvents.json"))
            .Append(Path.Combine(dataPath, "System.json"))
            .Append(Path.Combine(dataPath, "MapInfos.json"))
            .Append(Path.Combine(dataPath, "Actors.json"))
            .Append(Path.Combine(dataPath, "Items.json"))
            .Append(Path.Combine(dataPath, "Weapons.json"))
            .Append(Path.Combine(dataPath, "Armors.json"))
            .Append(Path.Combine(dataPath, "Animations.json"))
            .Append(Path.Combine(dataPath, "States.json"))
            .Append(Path.Combine(dataPath, "Skills.json"))
            .Append(Path.Combine(projectPath, "locales", "it", "dialogue", "mdv.json"))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var before = sourceFiles.ToDictionary(path => path, File.ReadAllBytes);

        var document = await CreateService().LoadAsync(Project(projectPath), Descriptor());

        Assert.NotEmpty(document.Workspace.Maps);
        Assert.NotEmpty(document.Workspace.CommonEvents);
        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public async Task Workspace_ProjectsCutsceneCoverageWithMetadataAndSafeFallbacks()
    {
        using var workspace = new TestWorkspace();
        WriteCoverageFixture(workspace);
        var trackedFiles = Directory.EnumerateFiles(Path.Combine(workspace.RootPath, "data"))
            .ToDictionary(path => path, File.ReadAllBytes);

        var document = await CreateService().LoadAsync(Project(workspace.RootPath), Descriptor());
        var blocks = document.Workspace.Maps.Single().Events.Single().Pages.Single().Blocks;

        Assert.DoesNotContain(blocks, block => block.SourceCommands.Any(command => command.Code == 0));
        var label = Assert.Single(blocks, block => block.Title == "Label");
        Assert.Equal("BossPhase2", label.LabelName);
        var jump = Assert.Single(blocks, block => block.Title == "Jump to Label" && block.LabelName == "BossPhase2");
        Assert.Equal(1, jump.CommandStartIndex);
        Assert.Equal(1, jump.CommandEndIndex);
        Assert.Contains(document.Workspace.Diagnostics, diagnostic => diagnostic.Code == "story.label-target-missing");

        var commonEvent = Assert.Single(blocks, block => block.CommonEventId == 42);
        Assert.Equal("Common Event #42 \"Chapter Call\"", commonEvent.DisplayText);
        Assert.Equal(42, commonEvent.TargetId);

        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Screen && block.DisplayText == "Transparency → ON");
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Screen && block.Title == "Tint Screen" && block.FrameDuration == 60);
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Screen && block.Title == "Shake Screen" && block.FrameDuration == 30);
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Picture && block.DisplayText == "Show #3 · CG_Prologue_01");
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Picture && block.Title == "Move Picture" && block.FrameDuration == 30);
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Picture && block.DisplayText == "Erase #3");
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.System && block.Title == "Menu Access" && block.DisplayText == "Disabled");

        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Actor && block.DisplayText.Contains("Polka") && block.DisplayText.Contains("HP -250"));
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Actor && block.DisplayText.Contains("Actor referenced by Variable #18 \"Target Actor\""));
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Actor && block.DisplayText.Contains("Add State #4 \"Poison\""));
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Actor && block.DisplayText.Contains("State #2"));
        Assert.DoesNotContain(document.Workspace.Diagnostics, diagnostic =>
            diagnostic.Code == "story.resource-target-missing" && diagnostic.Message.Contains("state #2", StringComparison.Ordinal));
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Battle && block.DisplayText.Contains("Enemy #2 · HP -500"));

        var plugin = Assert.Single(blocks, block => block.Kind == StoryBlockKind.PluginCommand && block.Title == "Plugin · Command");
        Assert.Equal(3, plugin.SourceCommands.Count);
        var orphan = Assert.Single(blocks, block => block.Kind == StoryBlockKind.Raw && block.Title == "Orphan Plugin Command Continuation");
        Assert.Equal(657, orphan.SourceCommands.Single().Code);
        Assert.Contains(document.Workspace.Diagnostics, diagnostic => diagnostic.Code == "story.plugin-continuation-orphan");
        Assert.Contains(blocks, block => block.Kind == StoryBlockKind.Raw && block.SourceCommands.Single().Code == 211);
        Assert.Contains(document.Workspace.Diagnostics, diagnostic => diagnostic.Code == "story.semantic-command-malformed");
        Assert.All(blocks, block => Assert.NotEmpty(block.SourceCommands));

        var search = StorySearchIndex.Build(document.Workspace).Search(new StorySearchQuery { Text = "Poison" });
        Assert.Contains(search.Results, result => result.Kind == StorySearchResultKind.Logic);

        foreach (var (path, before) in trackedFiles)
        {
            Assert.Equal(before, File.ReadAllBytes(path));
        }
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
        workspace.WriteFile("data/States.json", "[null]");
        workspace.WriteFile("data/Skills.json", "[null]");
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
                {"code":205,"indent":1,"parameters":[0,{"repeat":false,"skippable":true,"wait":true,"list":[{"code":17,"parameters":[]},{"code":12,"parameters":[]},{"code":15,"parameters":[20]},{"code":26,"parameters":[18]},{"code":45,"parameters":["Jump to: 58, 8"]},{"code":0,"parameters":[]}]}]},
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

    private static void WriteCoverageFixture(TestWorkspace workspace)
    {
        workspace.WriteFile("data/MapInfos.json", "[null,{\"id\":1,\"name\":\"Coverage\",\"order\":1}]");
        workspace.WriteFile("data/System.json", JsonSerializer.Serialize(new
        {
            switches = Array.Empty<string>(),
            variables = Names(18, "Target Actor"),
        }));
        workspace.WriteFile("data/Actors.json", "[null,{\"id\":1,\"name\":\"Polka\"}]");
        workspace.WriteFile("data/States.json", "[null,null,{\"id\":2,\"name\":\"\"},null,{\"id\":4,\"name\":\"Poison\"}]");
        workspace.WriteFile("data/Skills.json", "[null,{\"id\":1,\"name\":\"Slash\"}]");
        workspace.WriteFile("data/Items.json", "[null,{\"id\":1,\"name\":\"Potion\"}]");
        workspace.WriteFile("data/Weapons.json", "[null,{\"id\":1,\"name\":\"Blade\"}]");
        workspace.WriteFile("data/Armors.json", "[null,{\"id\":1,\"name\":\"Coat\"}]");
        workspace.WriteFile("data/Animations.json", "[null]");
        workspace.WriteFile("data/CommonEvents.json", "[null,{\"id\":42,\"name\":\"Chapter Call\",\"trigger\":0,\"switchId\":0,\"list\":[{\"code\":0,\"indent\":0,\"parameters\":[]}]}]");
        workspace.WriteFile("data/Map001.json", """
            {"events":[null,{"id":1,"name":"Coverage","pages":[{"trigger":0,"conditions":{},"list":[
              {"code":118,"indent":0,"parameters":["BossPhase2"]},
              {"code":119,"indent":0,"parameters":["BossPhase2"]},
              {"code":119,"indent":0,"parameters":["Missing"]},
              {"code":117,"indent":0,"parameters":[42]},
              {"code":211,"indent":0,"parameters":[0]},
              {"code":221,"indent":0,"parameters":[]},
              {"code":222,"indent":0,"parameters":[]},
              {"code":223,"indent":0,"parameters":[[-68,-68,0,68],60,true]},
              {"code":224,"indent":0,"parameters":[[255,255,255,170],30,false]},
              {"code":225,"indent":0,"parameters":[5,5,30,true]},
              {"code":231,"indent":0,"parameters":[3,"CG_Prologue_01",0,0,320,180,100,100,255,0]},
              {"code":232,"indent":0,"parameters":[3,0,0,0,11,12,100,100,255,0,30,true,0]},
              {"code":233,"indent":0,"parameters":[3,5]},
              {"code":234,"indent":0,"parameters":[3,[-68,-68,0,68],30,true]},
              {"code":235,"indent":0,"parameters":[3]},
              {"code":134,"indent":0,"parameters":[1]},
              {"code":135,"indent":0,"parameters":[0]},
              {"code":136,"indent":0,"parameters":[1]},
              {"code":137,"indent":0,"parameters":[0]},
              {"code":311,"indent":0,"parameters":[0,1,1,0,250,false]},
              {"code":312,"indent":0,"parameters":[1,18,0,1,9]},
              {"code":313,"indent":0,"parameters":[0,1,0,4]},
              {"code":313,"indent":0,"parameters":[0,1,0,2]},
              {"code":314,"indent":0,"parameters":[0,1]},
              {"code":315,"indent":0,"parameters":[0,1,0,0,300,false]},
              {"code":316,"indent":0,"parameters":[0,1,0,0,3,true]},
              {"code":318,"indent":0,"parameters":[0,1,0,1]},
              {"code":320,"indent":0,"parameters":[1,"Polka II"]},
              {"code":322,"indent":0,"parameters":[1,"Actor1",0,"Actor1",0,""]},
              {"code":326,"indent":0,"parameters":[0,1,0,0,15]},
              {"code":331,"indent":0,"parameters":[1,1,0,500,true]},
              {"code":125,"indent":0,"parameters":[0,0,50]},
              {"code":126,"indent":0,"parameters":[1,1,0,1]},
              {"code":127,"indent":0,"parameters":[1,0,0,1]},
              {"code":128,"indent":0,"parameters":[1,0,0,1,false]},
              {"code":129,"indent":0,"parameters":[1,0,true]},
              {"code":243,"indent":0,"parameters":[]},
              {"code":244,"indent":0,"parameters":[]},
              {"code":251,"indent":0,"parameters":[]},
              {"code":352,"indent":0,"parameters":[]},
              {"code":354,"indent":0,"parameters":[]},
              {"code":356,"indent":0,"parameters":["Legacy arg"]},
              {"code":657,"indent":0,"parameters":["orphan"]},
              {"code":357,"indent":0,"parameters":["Plugin","Command","",{}]},
              {"code":657,"indent":0,"parameters":["first"]},
              {"code":657,"indent":0,"parameters":["second"]},
              {"code":211,"indent":0,"parameters":["not-a-number"]},
              {"code":0,"indent":0,"parameters":[]}
            ]}]}]}
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
