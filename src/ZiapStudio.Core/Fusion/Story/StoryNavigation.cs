namespace ZiapStudio.Core.Fusion.Story;

/// <summary>
/// A durable, semantic address in the Story workspace. It deliberately contains
/// identifiers and command coordinates rather than object references so a
/// selection survives a workspace reload.
/// </summary>
public enum StoryLocationSourceKind
{
    Map,
    CommonEvent,
}

public sealed record StoryLocation
{
    public required StoryLocationSourceKind SourceKind { get; init; }
    public int? MapId { get; init; }
    public int? EventId { get; init; }
    public int? PageNumber { get; init; }
    public int? CommandStartIndex { get; init; }
    public int? CommandEndIndex { get; init; }

    public static StoryLocation ForMap(int mapId) => new()
    {
        SourceKind = StoryLocationSourceKind.Map,
        MapId = mapId,
    };

    public static StoryLocation ForEvent(int mapId, int eventId) => new()
    {
        SourceKind = StoryLocationSourceKind.Map,
        MapId = mapId,
        EventId = eventId,
    };

    public static StoryLocation ForPage(int mapId, int eventId, int pageNumber) => new()
    {
        SourceKind = StoryLocationSourceKind.Map,
        MapId = mapId,
        EventId = eventId,
        PageNumber = pageNumber,
    };

    public static StoryLocation ForBlock(int mapId, int eventId, int pageNumber, StoryBlock block) => new()
    {
        SourceKind = StoryLocationSourceKind.Map,
        MapId = mapId,
        EventId = eventId,
        PageNumber = pageNumber,
        CommandStartIndex = block.CommandStartIndex,
        CommandEndIndex = block.CommandEndIndex,
    };

    public static StoryLocation ForCommonEvent(int commonEventId, StoryBlock? block = null) => new()
    {
        SourceKind = StoryLocationSourceKind.CommonEvent,
        EventId = commonEventId,
        CommandStartIndex = block?.CommandStartIndex,
        CommandEndIndex = block?.CommandEndIndex,
    };
}

/// <summary>Validated tree projection of the MapInfos parentId graph.</summary>
public sealed record StoryMapHierarchyNode
{
    public required StoryMap Map { get; init; }
    public IReadOnlyList<StoryMapHierarchyNode> Children { get; init; } = [];
}

public sealed record StoryMapHierarchyResult
{
    public IReadOnlyList<StoryMapHierarchyNode> Roots { get; init; } = [];
    public IReadOnlySet<int> FallbackRootMapIds { get; init; } = new HashSet<int>();
}

public static class StoryMapHierarchy
{
    public static StoryMapHierarchyResult Build(IReadOnlyList<StoryMap> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        var byId = maps.GroupBy(map => map.Id).ToDictionary(group => group.Key, group => group.First());
        var validParents = new Dictionary<int, int?>();
        var fallbackRoots = new HashSet<int>();

        foreach (var map in maps)
        {
            if (map.ParentId <= 0)
            {
                validParents[map.Id] = null;
            }
            else if (map.ParentId == map.Id || !byId.ContainsKey(map.ParentId))
            {
                validParents[map.Id] = null;
                fallbackRoots.Add(map.Id);
            }
            else
            {
                validParents[map.Id] = map.ParentId;
            }
        }

        // A parent chain is valid only if it does not return to a map already
        // traversed. Every participant in a cycle becomes a deterministic root.
        foreach (var map in maps)
        {
            var path = new List<int>();
            var seenAt = new Dictionary<int, int>();
            var current = map.Id;
            while (validParents.TryGetValue(current, out var parent) && parent is not null)
            {
                if (seenAt.TryGetValue(current, out var cycleStart))
                {
                    foreach (var cycleMapId in path.Skip(cycleStart))
                    {
                        validParents[cycleMapId] = null;
                        fallbackRoots.Add(cycleMapId);
                    }
                    break;
                }
                seenAt[current] = path.Count;
                path.Add(current);
                current = parent.Value;
            }
        }

        var childrenByParent = maps.ToDictionary(map => map.Id, _ => new List<StoryMap>());
        var roots = new List<StoryMap>();
        foreach (var map in maps)
        {
            if (validParents[map.Id] is { } parentId)
            {
                childrenByParent[parentId].Add(map);
            }
            else
            {
                roots.Add(map);
            }
        }

        static IOrderedEnumerable<StoryMap> Order(IEnumerable<StoryMap> source) => source
            .OrderBy(map => map.Order)
            .ThenBy(map => map.Id);

        StoryMapHierarchyNode CreateNode(StoryMap map) => new()
        {
            Map = map,
            Children = Order(childrenByParent[map.Id]).Select(CreateNode).ToArray(),
        };

        return new StoryMapHierarchyResult
        {
            Roots = Order(roots).Select(CreateNode).ToArray(),
            FallbackRootMapIds = fallbackRoots,
        };
    }
}

/// <summary>Deterministic lookup/fallback policy shared by reload and deep-link callers.</summary>
public sealed record StoryLocationResolution
{
    public StoryMap? Map { get; init; }
    public StoryEvent? Event { get; init; }
    public StoryPage? Page { get; init; }
    public StoryCommonEvent? CommonEvent { get; init; }
    public StoryBlock? Block { get; init; }
    public StoryLocation? EffectiveLocation { get; init; }
}

public static class StoryLocationResolver
{
    public static StoryLocationResolution Resolve(StoryWorkspace workspace, StoryLocation location)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(location);
        if (location.SourceKind == StoryLocationSourceKind.CommonEvent)
        {
            var commonEvent = workspace.CommonEvents.FirstOrDefault(@event => @event.Id == location.EventId);
            var commonBlock = SelectBlock(commonEvent?.Blocks ?? [], location);
            return new StoryLocationResolution
            {
                CommonEvent = commonEvent,
                Block = commonBlock,
                EffectiveLocation = commonEvent is null ? null : StoryLocation.ForCommonEvent(commonEvent.Id, commonBlock),
            };
        }

        var map = workspace.Maps.FirstOrDefault(candidate => candidate.Id == location.MapId);
        var @event = map?.Events.FirstOrDefault(candidate => candidate.Id == location.EventId);
        var page = @event?.Pages.FirstOrDefault(candidate => candidate.Number == location.PageNumber);
        var block = SelectBlock(page?.Blocks ?? [], location);
        return new StoryLocationResolution
        {
            Map = map,
            Event = @event,
            Page = page,
            Block = block,
            EffectiveLocation = map is null ? null : @event is null ? StoryLocation.ForMap(map.Id) : page is null
                ? StoryLocation.ForEvent(map.Id, @event.Id)
                : block is null ? StoryLocation.ForPage(map.Id, @event.Id, page.Number)
                : StoryLocation.ForBlock(map.Id, @event.Id, page.Number, block),
        };
    }

    private static StoryBlock? SelectBlock(IReadOnlyList<StoryBlock> blocks, StoryLocation location)
    {
        if (blocks.Count == 0 || location.CommandStartIndex is null)
        {
            return null;
        }
        return blocks.FirstOrDefault(block =>
                   block.CommandStartIndex == location.CommandStartIndex &&
                   block.CommandEndIndex == location.CommandEndIndex) ??
               blocks.OrderBy(block => Math.Abs(block.CommandStartIndex - location.CommandStartIndex.Value))
                   .ThenBy(block => block.CommandStartIndex)
                   .First();
    }
}

public enum StorySearchSourceFilter
{
    All,
    Maps,
    CommonEvents,
}

public enum StorySearchContentFilter
{
    Dialogue,
    Choices,
    Movement,
    Audio,
    Logic,
    Transfer,
    Plugin,
    Script,
    Comment,
    Raw,
}

public sealed record StorySearchFilters
{
    public StorySearchSourceFilter Source { get; init; } = StorySearchSourceFilter.All;
    public IReadOnlySet<StorySearchContentFilter> ContentTypes { get; init; } = new HashSet<StorySearchContentFilter>();
    public bool LocalizedOnly { get; init; }
    public bool RawLiteralOnly { get; init; }
    public bool EditableMasterOnly { get; init; }
    public bool WithConditionsOnly { get; init; }
    public bool WithoutConditionsOnly { get; init; }
    public bool IncludeTechnicalRawData { get; init; }

    public bool HasActiveFilters => Source != StorySearchSourceFilter.All || ContentTypes.Count > 0 ||
        LocalizedOnly || RawLiteralOnly || EditableMasterOnly || WithConditionsOnly || WithoutConditionsOnly || IncludeTechnicalRawData;
}

public sealed record StorySearchQuery
{
    public string Text { get; init; } = string.Empty;
    public StorySearchFilters Filters { get; init; } = new();
    public int MaximumResults { get; init; } = 200;
}

public enum StorySearchResultKind
{
    Map,
    Event,
    Page,
    Dialogue,
    Choices,
    Movement,
    Audio,
    Logic,
    Transfer,
    Plugin,
    Script,
    Comment,
    Raw,
}

public sealed record StorySearchResult
{
    public required StorySearchResultKind Kind { get; init; }
    public required string Title { get; init; }
    public string Snippet { get; init; } = string.Empty;
    public string Breadcrumb { get; init; } = string.Empty;
    public required StoryLocation Location { get; init; }
    public int Rank { get; init; }
}

public sealed record StorySearchQueryResult
{
    public IReadOnlyList<StorySearchResult> Results { get; init; } = [];
    public int TotalResults { get; init; }
    public bool IsLimited => TotalResults > Results.Count;
}
