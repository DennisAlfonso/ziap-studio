using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Models;
using ZiapStudio.Services;
using ZiapStudio.Services.Fusion.Story;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Tests;

public sealed class ChoiceBranchResolutionTests
{
    [Fact]
    public async Task Workspace_ResolvesChoiceBranchesFromTheirStructurallyAssociatedChoices()
    {
        using var workspace = new TestWorkspace();
        WriteFixture(workspace);
        var trackedFiles = Directory.EnumerateFiles(Path.Combine(workspace.RootPath, "data"))
            .Append(Path.Combine(workspace.RootPath, "locales", "it", "dialogue", "mdv.json"))
            .ToDictionary(path => path, File.ReadAllBytes);

        var document = await CreateService().LoadAsync(CreateProject(workspace.RootPath), CreateDescriptor());
        var blocks = document.Workspace.Maps.Single().Events.Single().Pages.Single().Blocks;

        var choices = Assert.Single(blocks, block => block.Kind == StoryBlockKind.Choices && block.CommandStartIndex == 0);
        Assert.Contains("Né perché tu mi stia portando qui.", choices.DisplayText);

        var canonicalBranch = Assert.Single(blocks, block => block.Kind == StoryBlockKind.ControlFlow && block.CommandStartIndex == 2);
        Assert.Equal("\\i[1668] Né perché tu mi stia portando qui.", canonicalBranch.DisplayText);
        Assert.Equal(2, canonicalBranch.ChoiceIndex);
        Assert.Equal(0, canonicalBranch.ChoiceSourceCommandIndex);
        Assert.True(canonicalBranch.IsDerivedStructuralUsage);
        Assert.Equal(0, canonicalBranch.Indent);
        Assert.Equal(2, canonicalBranch.CommandEndIndex);
        Assert.Equal(402, canonicalBranch.SourceCommands.Single().Code);
        Assert.Equal("\\i[1668] {mdv[0].DestinyOfBirth[60].choices[2]}", canonicalBranch.RawText);
        Assert.Contains(canonicalBranch.LocalizationOrigins, origin =>
            origin.SourceFile == "dialogue/mdv.json" && origin.Path.EndsWith(".choices.2", StringComparison.Ordinal));

        var nestedBranch = Assert.Single(blocks, block => block.CommandStartIndex == 7);
        Assert.Equal("\\i[1668] Inner two", nestedBranch.DisplayText);
        Assert.Equal(1, nestedBranch.ChoiceIndex);
        Assert.Equal(6, nestedBranch.ChoiceSourceCommandIndex);
        Assert.Equal(1, nestedBranch.Indent);

        var outerAfterNested = Assert.Single(blocks, block => block.CommandStartIndex == 9);
        Assert.Equal("\\i[1668] Outer two", outerAfterNested.DisplayText);
        Assert.Equal(1, outerAfterNested.ChoiceIndex);
        Assert.Equal(4, outerAfterNested.ChoiceSourceCommandIndex);

        var sequentialBranch = Assert.Single(blocks, block => block.CommandStartIndex == 13);
        Assert.Equal("\\i[1668] Sequential", sequentialBranch.DisplayText);
        Assert.Equal(12, sequentialBranch.ChoiceSourceCommandIndex);

        var literalFallback = Assert.Single(blocks, block => block.CommandStartIndex == 15);
        Assert.Equal("\\i[1668] Fallback literal", literalFallback.DisplayText);
        Assert.Null(literalFallback.ChoiceSourceCommandIndex);
        Assert.Equal("\\i[1668] {mdv[0].DestinyOfBirth[60].fallback[0]}", literalFallback.RawText);

        var invalidIndex = Assert.Single(blocks, block => block.CommandStartIndex == 17);
        Assert.Equal(5, invalidIndex.ChoiceIndex);
        Assert.Equal(16, invalidIndex.ChoiceSourceCommandIndex);
        Assert.Equal("Invalid branch label", invalidIndex.DisplayText);
        Assert.Contains(document.Workspace.Diagnostics, diagnostic => diagnostic.Code == "story.choice-branch-index-out-of-range");

        var missingLabel = Assert.Single(blocks, block => block.CommandStartIndex == 19);
        Assert.Equal("Choice #3", missingLabel.DisplayText);
        Assert.Empty(missingLabel.RawText);
        Assert.Null(missingLabel.ChoiceSourceCommandIndex);

        Assert.Contains(blocks, block => block.CommandStartIndex == 10 && block.Title == "Cancel branch");
        var search = StorySearchIndex.Build(document.Workspace).Search(new StorySearchQuery
        {
            Text = "Né perché tu mi stia portando qui",
        });
        Assert.Contains(search.Results, result => result.Kind == StorySearchResultKind.Choices);
        Assert.Contains(search.Results, result => result.Kind == StorySearchResultKind.Logic && result.Title == "Branch scelta");

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
        workspace.WriteFile("data/MapInfos.json", "[null,{\"id\":1,\"name\":\"Choice coverage\",\"order\":1}]");
        workspace.WriteFile("data/Map001.json", JsonSerializer.Serialize(new
        {
            events = new object?[]
            {
                null,
                new
                {
                    id = 1,
                    name = "Choice coverage",
                    pages = new[]
                    {
                        new
                        {
                            trigger = 0,
                            conditions = new { },
                            list = new object[]
                            {
                                Choices(0, Choice("choices", 0), Choice("choices", 1), Choice("choices", 2)),
                                Branch(0, 0, Choice("choices", 0)),
                                Branch(0, 2, Choice("choices", 2)),
                                Command(404, 0),
                                Choices(0, Choice("outer", 0), Choice("outer", 1)),
                                Branch(0, 0, Choice("outer", 0)),
                                Choices(1, Choice("inner", 0), Choice("inner", 1)),
                                Branch(1, 1, Choice("inner", 1)),
                                Command(404, 1),
                                Branch(0, 1, Choice("outer", 1)),
                                Command(403, 0),
                                Command(404, 0),
                                Choices(0, Choice("sequential", 0)),
                                Branch(0, 0, Choice("sequential", 0)),
                                Command(404, 0),
                                Branch(0, 0, Choice("fallback", 0)),
                                Choices(0, Choice("sequential", 0)),
                                Branch(0, 5, "Invalid branch label"),
                                Command(404, 0),
                                Command(402, 0, 3),
                                Command(0, 0),
                            },
                        },
                    },
                },
            },
        }));

        var destinations = Enumerable.Range(0, 61)
            .Select(index => index == 60
                ? (object)new
                {
                    choices = new[] { "First", "Second", "Né perché tu mi stia portando qui." },
                    outer = new[] { "Outer one", "Outer two" },
                    inner = new[] { "Inner one", "Inner two" },
                    sequential = new[] { "Sequential" },
                    fallback = new[] { "Fallback literal" },
                }
                : new { })
            .ToArray();
        workspace.WriteFile("locales/it/dialogue/mdv.json", JsonSerializer.Serialize(new[]
        {
            new { DestinyOfBirth = destinations },
        }));
    }

    private static object Choices(int indent, params string[] values) => Command(102, indent,
        new object[] { values, -1, 0, 2, 0 });

    private static object Branch(int indent, int choiceIndex, string label) => Command(402, indent,
        new object[] { choiceIndex, label });

    private static object Command(int code, int indent, params object[] parameters) => new
    {
        code,
        indent,
        parameters,
    };

    private static string Choice(string property, int index) =>
        $"\\i[1668] {{mdv[0].DestinyOfBirth[60].{property}[{index}]}}";

    private static ZiapProject CreateProject(string path) => new()
    {
        Id = "test",
        Name = "Test",
        Path = path,
        ProjectType = KnownProjectTypes.RpgMakerMz,
        IsZiapInitialized = true,
    };

    private static DocumentDescriptor CreateDescriptor() => new()
    {
        Id = new DocumentId("test:choice-branches"),
        DisplayName = "Story",
        Kind = DocumentKind.ProjectIntegration,
        ResourceId = new Uri("fusionstory://workspace/"),
    };
}
