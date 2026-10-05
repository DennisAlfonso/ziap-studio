using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Localization;
using ZiapStudio.Services.Fusion.Story;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Tests;

public sealed class StoryNavigationTests
{
    [Fact]
    public void MapHierarchy_PreservesOrderNestedMapsAndFallsBackForInvalidParents()
    {
        var maps = new[]
        {
            Map(1, "Mission maps", 10, 0), Map(2, "Prologue", 11, 1), Map(3, "Cutscene", 12, 2),
            Map(4, "Missing parent", 20, 99), Map(5, "Self", 21, 5),
            Map(6, "Cycle A", 22, 7), Map(7, "Cycle B", 23, 6),
        };

        var result = StoryMapHierarchy.Build(maps);

        Assert.Equal([1, 4, 5, 6, 7], result.Roots.Select(node => node.Map.Id));
        var root = result.Roots.Single(node => node.Map.Id == 1);
        Assert.Equal(2, root.Children.Single().Map.Id);
        Assert.Equal(3, root.Children[0].Children.Single().Map.Id);
        Assert.Equal([4, 5, 6, 7], result.FallbackRootMapIds.Order());
    }

    [Fact]
    public async Task Workspace_PreservesMapInfosParentExpandedAndDiagnosesInvalidHierarchy()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("data/MapInfos.json", """
            [null,
             {"id":1,"name":"Root","order":5,"parentId":0,"expanded":true},
             {"id":2,"name":"Child","order":6,"parentId":1,"expanded":false},
             {"id":3,"name":"Orphan","order":7,"parentId":99,"expanded":true}]
            """);
        workspace.WriteFile("data/Map001.json", "{\"events\":[]}");
        workspace.WriteFile("data/Map002.json", "{\"events\":[]}");
        workspace.WriteFile("data/Map003.json", "{\"events\":[]}");

        var document = await CreateService().LoadAsync(Project(workspace.RootPath), Descriptor());

        var root = document.Workspace.Maps.Single(map => map.Id == 1);
        var child = document.Workspace.Maps.Single(map => map.Id == 2);
        Assert.True(root.RpgMakerExpanded);
        Assert.False(child.RpgMakerExpanded);
        Assert.Equal(1, child.ParentId);
        Assert.Equal(6, child.Order);
        Assert.Contains(document.Workspace.Diagnostics, diagnostic => diagnostic.Code == "story.map-parent-invalid" && diagnostic.MapId == 3);
    }

    [Fact]
    public void SearchIndex_ReturnsExactDialogueLocationAndRanksIdsBeforeRawData()
    {
        var workspace = StoryWorkspaceFixture();
        var index = StorySearchIndex.Build(workspace);

        var dialogue = Assert.Single(index.Search(new StorySearchQuery {Text = "non c'erano dei"}).Results);
        Assert.Equal(StorySearchResultKind.Dialogue, dialogue.Kind);
        Assert.Equal(1, dialogue.Location.MapId);
        Assert.Equal(1, dialogue.Location.EventId);
        Assert.Equal(1, dialogue.Location.PageNumber);
        Assert.Equal(10, dialogue.Location.CommandStartIndex);
        Assert.Equal(11, dialogue.Location.CommandEndIndex);

        var map = index.Search(new StorySearchQuery {Text = "001"}).Results.First();
        Assert.Equal(StorySearchResultKind.Map, map.Kind);
        Assert.Equal(0, map.Rank);

        Assert.Empty(index.Search(new StorySearchQuery {Text = "technicalOnly"}).Results);
        Assert.Single(index.Search(new StorySearchQuery
        {
            Text = "technicalOnly",
            Filters = new StorySearchFilters {IncludeTechnicalRawData = true},
        }).Results);
    }

    [Fact]
    public void SearchIndex_AppliesSourceContentLocalizationAndPageFiltersWithoutText()
    {
        var index = StorySearchIndex.Build(StoryWorkspaceFixture());

        var dialogue = index.Search(new StorySearchQuery
        {
            Filters = new StorySearchFilters
            {
                Source = StorySearchSourceFilter.Maps,
                ContentTypes = new HashSet<StorySearchContentFilter> {StorySearchContentFilter.Dialogue},
            },
        });
        Assert.Single(dialogue.Results);
        Assert.Equal(StorySearchResultKind.Dialogue, dialogue.Results[0].Kind);

        var common = index.Search(new StorySearchQuery
        {
            Filters = new StorySearchFilters {Source = StorySearchSourceFilter.CommonEvents},
        });
        Assert.All(common.Results, result => Assert.Equal(StoryLocationSourceKind.CommonEvent, result.Location.SourceKind));

        var localized = index.Search(new StorySearchQuery
        {
            Filters = new StorySearchFilters {LocalizedOnly = true, EditableMasterOnly = true},
        });
        Assert.Single(localized.Results);
        Assert.Equal(StorySearchResultKind.Dialogue, localized.Results[0].Kind);

        var conditioned = index.Search(new StorySearchQuery
        {
            Filters = new StorySearchFilters {WithConditionsOnly = true},
        });
        Assert.All(conditioned.Results, result => Assert.Equal(1, result.Location.PageNumber));
    }

    [Fact]
    public void ExactJumpAndRestore_UseSemanticLocationAndNearestBlockFallback()
    {
        var workspace = StoryWorkspaceFixture();
        var exact = StoryLocation.ForBlock(1, 1, 1, workspace.Maps[0].Events[0].Pages[0].Blocks[2]);

        var resolved = StoryLocationResolver.Resolve(workspace, exact);
        Assert.Equal("Polka", resolved.Block?.Title);
        Assert.Equal(exact, resolved.EffectiveLocation);

        var afterRemoval = workspace with
        {
            Maps = [workspace.Maps[0] with
            {
                Events = [workspace.Maps[0].Events[0] with
                {
                    Pages = [workspace.Maps[0].Events[0].Pages[0] with {Blocks = [Block(8, 8, "Before"), Block(14, 14, "After")] }],
                }],
            }],
        };
        var fallback = StoryLocationResolver.Resolve(afterRemoval, exact);
        Assert.Equal("Before", fallback.Block?.Title);
        Assert.Equal(8, fallback.EffectiveLocation?.CommandStartIndex);
    }

    [Fact]
    public void LargeWorkspace_IndexIsBuiltOnceAndSearchIsCappedWithoutQuadraticNavigationRebuild()
    {
        var maps = Enumerable.Range(1, 150).Select(mapId => new StoryMap
        {
            Id = mapId,
            Name = $"Map {mapId:000}",
            Order = mapId,
            SourcePath = $"data/Map{mapId:000}.json",
            Events = Enumerable.Range(1, 10).Select(eventId => new StoryEvent
            {
                Id = eventId,
                Name = $"Event {eventId}",
                Pages = Enumerable.Range(1, 2).Select(pageNumber => new StoryPage
                {
                    Number = pageNumber, Trigger = 0,
                    Blocks = Enumerable.Range(0, 5).Select(index => Block(index, index, $"Dialogue {mapId}-{eventId}-{pageNumber}-{index}")).ToArray(),
                }).ToArray(),
            }).ToArray(),
        }).ToArray();
        var index = StorySearchIndex.Build(new StoryWorkspace {Maps = maps});

        Assert.True(index.Count >= 19_650);
        var result = index.Search(new StorySearchQuery
        {
            Text = "Dialogue",
            Filters = new StorySearchFilters {ContentTypes = new HashSet<StorySearchContentFilter> {StorySearchContentFilter.Dialogue}},
            MaximumResults = 200,
        });
        Assert.Equal(15_000, result.TotalResults);
        Assert.Equal(200, result.Results.Count);
        Assert.True(result.IsLimited);
    }

    private static StoryWorkspace StoryWorkspaceFixture() => new()
    {
        Maps = [new StoryMap
        {
            Id = 1, Name = "Cutscene", Order = 1, SourcePath = "data/Map001.json", RpgMakerExpanded = true,
            Events = [new StoryEvent
            {
                Id = 1, Name = "Cutscene", X = 41, Y = 9,
                Pages = [new StoryPage
                {
                    Number = 1, Trigger = 0, ConditionsSummary = "Switch 16",
                    Blocks =
                    [
                        new StoryBlock {Kind = StoryBlockKind.PluginCommand, Title = "GabeMZ_StepSound · eventStepSound", Summary = "Plugin", CommandStartIndex = 0, CommandEndIndex = 0, RawParameters = "[\"technicalOnly\"]"},
                        new StoryBlock {Kind = StoryBlockKind.Comment, Title = "Comment", DisplayText = "A note", CommandStartIndex = 1, CommandEndIndex = 1},
                        new StoryBlock
                        {
                            Kind = StoryBlockKind.Dialogue, Title = "Polka", DisplayText = "Non c'erano dei", CommandStartIndex = 10, CommandEndIndex = 11,
                            LocalizationOrigins = [new LocalizationReferenceOrigin
                            {
                                Namespace = "mdv", Locale = "it", SourceFile = "dialogue/mdv.json", Path = "mdv[0].DestinyOfBirth[0].newPrologoStory[8].text",
                                Segments = [LocalizationPathSegment.Index(0), LocalizationPathSegment.Property("DestinyOfBirth")],
                            }],
                        },
                    ],
                }],
            }],
        }],
        CommonEvents = [new StoryCommonEvent {Id = 1, Name = "Common epilogue", Blocks = [Block(0, 0, "Common block")] }],
    };

    private static StoryMap Map(int id, string name, int order, int parentId) => new()
    {
        Id = id, Name = name, Order = order, ParentId = parentId, SourcePath = $"data/Map{id:000}.json",
    };

    private static StoryBlock Block(int start, int end, string title) => new()
    {
        Kind = StoryBlockKind.Dialogue, Title = title, DisplayText = title, CommandStartIndex = start, CommandEndIndex = end,
    };

    private static FusionStoryWorkspaceService CreateService()
    {
        var fileSystem = new FileSystemService();
        return new FusionStoryWorkspaceService(fileSystem, new RpgMakerStoryCommandParser(
            new CompositeLocalizationTextResolver(new ZiapStudio.Services.Localization.LocalizationService([
                new ZiapStudio.Services.Localization.FusionLocalizationProvider(fileSystem),
            ]))));
    }

    private static ZiapStudio.Core.Models.ZiapProject Project(string path) => new()
    {
        Id = "test", Name = "Test", Path = path, ProjectType = ZiapStudio.Core.Models.KnownProjectTypes.RpgMakerMz, IsZiapInitialized = true,
    };

    private static ZiapStudio.Core.Documents.DocumentDescriptor Descriptor() => new()
    {
        Id = new ZiapStudio.Core.Documents.DocumentId("test:story-navigation"), DisplayName = "Story", Kind = ZiapStudio.Core.Documents.DocumentKind.ProjectIntegration, ResourceId = new Uri("fusionstory://workspace/"),
    };
}
