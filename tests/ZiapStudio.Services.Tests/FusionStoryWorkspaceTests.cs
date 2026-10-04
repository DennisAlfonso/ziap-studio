using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Models;
using ZiapStudio.Services;
using ZiapStudio.Services.Fusion.Story;
using ZiapStudio.Services.Integrations;
using ZiapStudio.Services.Localization;
using ZiapStudio.Services.Providers;

namespace ZiapStudio.Services.Tests;

public sealed class FusionStoryWorkspaceTests
{
    [Fact]
    public async Task ProjectIntegration_PublishesStoryWorkspaceInWorld()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("data/System.json", "{\"gameTitle\":\"Test\"}");
        workspace.WriteFile("data/MapInfos.json", "[null,{\"id\":1,\"name\":\"Intro\"}]");
        var fileSystem = new FileSystemService();
        var provider = new ProjectProviderService(
            fileSystem,
            new ProjectIntegrationService([new FusionStoryIntegrationProvider(fileSystem)]));

        var roots = await provider.BuildExplorerAsync(CreateProject(workspace.RootPath));

        var node = roots.Single(root => root.Name == "World")
            .Children.Single(child => child.Name == "Story & Events");
        Assert.Equal(FusionStoryIntegrationProvider.ResourceUri, node.Document!.ResourceId.AbsoluteUri);
    }

    [Fact]
    public async Task Workspace_ReadsSemanticBlocksAndPreservesAllSourceData()
    {
        using var workspace = new TestWorkspace();
        WriteStoryFixture(workspace);
        var mapPath = workspace.RootPath + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "Map001.json";
        var commonPath = workspace.RootPath + Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar + "CommonEvents.json";
        var mapBefore = File.ReadAllBytes(mapPath);
        var commonBefore = File.ReadAllBytes(commonPath);

        var document = await CreateService().LoadAsync(CreateProject(workspace.RootPath), CreateDescriptor());

        var map = Assert.Single(document.Workspace.Maps);
        Assert.Equal("001 — Cutscene iniziale", map.DisplayName);
        var storyEvent = Assert.Single(map.Events);
        Assert.Equal(2, storyEvent.Pages.Count);
        var page = storyEvent.Pages[0];
        Assert.Empty(storyEvent.Pages[1].Blocks);
        var dialogue = Assert.Single(page.Blocks, block =>
            block.Kind == StoryBlockKind.Dialogue && !block.RawText.Contains("missing"));
        Assert.Equal("Polka", dialogue.Title);
        Assert.Equal("Non c'erano dei.", dialogue.DisplayText);
        Assert.Equal("{mdv[0].DestinyOfBirth[0].newPrologoStory[0].text}", dialogue.RawText);
        Assert.Contains(dialogue.LocalizationOrigins, origin =>
            origin.SourceFile == "dialogue/mdv.json" && origin.Path.EndsWith(".text"));
        Assert.Single(page.Blocks, block => block.Kind == StoryBlockKind.Choices && block.DisplayText.Contains("Continua"));
        Assert.Single(page.Blocks, block => block.Kind == StoryBlockKind.Comment && block.SourceCommands.Count == 2);
        Assert.Single(page.Blocks, block => block.Kind == StoryBlockKind.Script && block.SourceCommands.Count == 2);
        Assert.Single(page.Blocks, block => block.Kind == StoryBlockKind.PluginCommand && block.SourceCommands.Count == 2);
        var movement = Assert.Single(page.Blocks, block => block.Kind == StoryBlockKind.MovementRoute);
        Assert.Equal(2, movement.SourceCommands.Count);
        var raw = Assert.Single(page.Blocks, block => block.Kind == StoryBlockKind.Raw);
        Assert.Equal(999, raw.SourceCommands.Single().Code);
        Assert.Equal("[\"keep\",42]", raw.RawParameters);
        var commonEvent = Assert.Single(document.Workspace.CommonEvents);
        Assert.Equal("Epilogo", commonEvent.Name);
        Assert.Single(commonEvent.Blocks, block => block.Kind == StoryBlockKind.Wait);
        Assert.Equal(mapBefore, File.ReadAllBytes(mapPath));
        Assert.Equal(commonBefore, File.ReadAllBytes(commonPath));
    }

    [Fact]
    public async Task Workspace_LeavesMissingLocalizationTokenRawAndAddsNoFailure()
    {
        using var workspace = new TestWorkspace();
        WriteStoryFixture(workspace);
        var document = await CreateService().LoadAsync(CreateProject(workspace.RootPath), CreateDescriptor());

        var page = document.Workspace.Maps.Single().Events.Single().Pages[0];
        var dialogue = page.Blocks.OfType<StoryBlock>().Single(block =>
            block.Kind == StoryBlockKind.Dialogue && block.RawText.Contains("missing"));

        Assert.Equal("{mdv[0].DestinyOfBirth[0].newPrologoStory[0].missing}", dialogue.DisplayText);
        Assert.Single(dialogue.LocalizationOrigins);
        Assert.Equal("dialogue/mdv.json", dialogue.LocalizationOrigins[0].SourceFile);
        Assert.DoesNotContain(document.Workspace.Diagnostics, diagnostic =>
            diagnostic.Severity == StoryDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task CompositeResolver_PreservesEscapeCodesAroundLocalizedText()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "locales/it/dialogue/mdv.json",
            """[{"DestinyOfBirth":[{"newPrologoStory":[{"choices":["Continua"]}]}]}]""");
        var resolver = new CompositeLocalizationTextResolver(CreateLocalizationService());

        var text = await resolver.ResolveAsync(
            workspace.RootPath,
            "\\i[1668] {mdv[0].DestinyOfBirth[0].newPrologoStory[0].choices[0]}");

        Assert.Equal("\\i[1668] Continua", text.DisplayText);
        Assert.Equal("\\i[1668] {mdv[0].DestinyOfBirth[0].newPrologoStory[0].choices[0]}", text.RawText);
        Assert.True(Assert.Single(text.Tokens).IsResolved);
    }

    private static FusionStoryWorkspaceService CreateService()
    {
        var fileSystem = new FileSystemService();
        return new FusionStoryWorkspaceService(
            fileSystem,
            new RpgMakerStoryCommandParser(
                new CompositeLocalizationTextResolver(CreateLocalizationService(fileSystem))));
    }

    private static LocalizationService CreateLocalizationService(FileSystemService? fileSystem = null) => new(
    [
        new FusionLocalizationProvider(fileSystem ?? new FileSystemService()),
    ]);

    private static void WriteStoryFixture(TestWorkspace workspace)
    {
        workspace.WriteFile("data/MapInfos.json", "[null,{\"id\":1,\"name\":\"Cutscene iniziale\",\"order\":1}]");
        workspace.WriteFile(
            "locales/it/dialogue/mdv.json",
            """
            [{"DestinyOfBirth":[{"newPrologoStory":[{
              "name":"Polka",
              "text":"Non c'erano dei.",
              "choices":["Continua"]
            }]}]}]
            """);
        workspace.WriteFile(
            "data/UnusedFixture.json",
            """
            {
              "events":[null,{"id":1,"name":"Prologo","x":4,"y":8,"pages":[
                {"trigger":0,"conditions":{},"list":[
                  {"code":101,"indent":0,"parameters":["Actor1",0,0,2,"{mdv[0].DestinyOfBirth[0].newPrologoStory[0].name}"]},
                  {"code":401,"indent":0,"parameters":["{mdv[0].DestinyOfBirth[0].newPrologoStory[0].text}"]},
                  {"code":101,"indent":0,"parameters":["",0,0,2,""]},
                  {"code":401,"indent":0,"parameters":["{mdv[0].DestinyOfBirth[0].newPrologoStory[0].missing}"]},
                  {"code":102,"indent":0,"parameters":[["\\i[1668] {mdv[0].DestinyOfBirth[0].newPrologoStory[0].choices[0]}"],-1,0,2,0]},
                  {"code":402,"indent":0,"parameters":[0,"Opzione"]},
                  {"code":404,"indent":0,"parameters":[]},
                  {"code":108,"indent":0,"parameters":["Nota"]},
                  {"code":408,"indent":0,"parameters":["continua"]},
                  {"code":355,"indent":0,"parameters":["let a = 1;"]},
                  {"code":655,"indent":0,"parameters":["a++;"]},
                  {"code":357,"indent":0,"parameters":["Plugin","Message","Message",{"messageText":"{mdv[0].DestinyOfBirth[0].newPrologoStory[0].text}"}]},
                  {"code":657,"indent":0,"parameters":["continuazione"]},
                  {"code":205,"indent":0,"parameters":[0,{"repeat":false,"skippable":true,"wait":true,"list":[{"code":1,"parameters":[]]}]},
                  {"code":505,"indent":0,"parameters":[{"code":2,"parameters":[]}]},
                  {"code":999,"indent":0,"parameters":["keep",42]}
                ]},
                {"trigger":0,"conditions":{},"list":[]}
              ]}]
            }
            """);
        workspace.WriteFile(
            "data/Map001.json",
            """
            {"events":[null,{"id":1,"name":"Prologo","x":4,"y":8,"pages":[
              {"trigger":0,"conditions":{},"list":[
                {"code":101,"indent":0,"parameters":["Actor1",0,0,2,"{mdv[0].DestinyOfBirth[0].newPrologoStory[0].name}"]},
                {"code":401,"indent":0,"parameters":["{mdv[0].DestinyOfBirth[0].newPrologoStory[0].text}"]},
                {"code":101,"indent":0,"parameters":["",0,0,2,""]},
                {"code":401,"indent":0,"parameters":["{mdv[0].DestinyOfBirth[0].newPrologoStory[0].missing}"]},
                {"code":102,"indent":0,"parameters":[["\\i[1668] {mdv[0].DestinyOfBirth[0].newPrologoStory[0].choices[0]}"],-1,0,2,0]},
                {"code":402,"indent":0,"parameters":[0,"Opzione"]},
                {"code":404,"indent":0,"parameters":[]},
                {"code":108,"indent":0,"parameters":["Nota"]},
                {"code":408,"indent":0,"parameters":["continua"]},
                {"code":355,"indent":0,"parameters":["let a = 1;"]},
                {"code":655,"indent":0,"parameters":["a++;"]},
                {"code":357,"indent":0,"parameters":["Plugin","Message","Message",{"messageText":"{mdv[0].DestinyOfBirth[0].newPrologoStory[0].text}"}]},
                {"code":657,"indent":0,"parameters":["continuazione"]},
                {"code":205,"indent":0,"parameters":[0,{"repeat":false,"skippable":true,"wait":true,"list":[]}]},
                {"code":505,"indent":0,"parameters":[{"code":2,"parameters":[]}]},
                {"code":999,"indent":0,"parameters":["keep",42]}
              ]},
              {"trigger":0,"conditions":{},"list":[]}
            ]}]}
            """);
        workspace.WriteFile(
            "data/CommonEvents.json",
            """[null,{"id":1,"name":"Epilogo","trigger":0,"switchId":0,"list":[{"code":230,"indent":0,"parameters":[60]}]}]""");
    }

    private static DocumentDescriptor CreateDescriptor() => new()
    {
        Id = new DocumentId("test:integration:fusion-story"),
        DisplayName = "Story & Events",
        Kind = DocumentKind.ProjectIntegration,
        ResourceId = new Uri("fusionstory://workspace/"),
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
