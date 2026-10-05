using ZiapStudio.Core.Localization;

namespace ZiapStudio.Core.Fusion.Story;

/// <summary>
/// In-memory, immutable search projection of a Story workspace. Building the
/// index is separate from querying it so typing never rebuilds the navigator or
/// reparses event files.
/// </summary>
public sealed class StorySearchIndex
{
    private readonly IReadOnlyList<Entry> _entries;

    private StorySearchIndex(IReadOnlyList<Entry> entries) => _entries = entries;

    public int Count => _entries.Count;

    public static StorySearchIndex Build(StoryWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var entries = new List<Entry>();
        var breadcrumbs = BuildMapBreadcrumbs(workspace.Maps);

        foreach (var map in workspace.Maps.OrderBy(map => map.Order).ThenBy(map => map.Id))
        {
            var mapBreadcrumb = breadcrumbs.TryGetValue(map.Id, out var value)
                ? value
                : map.DisplayName;
            entries.Add(new Entry(
                StorySearchResultKind.Map,
                map.DisplayName,
                $"{map.Events.Count} event{(map.Events.Count == 1 ? string.Empty : "i")}",
                mapBreadcrumb,
                StoryLocation.ForMap(map.Id),
                StoryLocationSourceKind.Map,
                [map.Id.ToString(), map.Id.ToString("000"), map.Name, map.DisplayName],
                [map.Name, map.DisplayName], [], [], [], string.Empty, false, false, false));

            foreach (var @event in map.Events.OrderBy(@event => @event.Id))
            {
                var eventBreadcrumb = $"{mapBreadcrumb} › Event {@event.Id} {@event.Name}";
                entries.Add(new Entry(
                    StorySearchResultKind.Event,
                    @event.DisplayName,
                    $"x {@event.X} · y {@event.Y} · {@event.Pages.Count} page{(@event.Pages.Count == 1 ? string.Empty : "s")}",
                    eventBreadcrumb,
                    StoryLocation.ForEvent(map.Id, @event.Id),
                    StoryLocationSourceKind.Map,
                    [@event.Id.ToString(), $"event {@event.Id}", @event.Name, @event.DisplayName],
                    [@event.Name, @event.DisplayName], [], [], [], string.Empty, false, false, false));

                foreach (var page in @event.Pages.OrderBy(page => page.Number))
                {
                    var hasConditions = !page.ConditionsSummary.Equals("Nessuna condizione", StringComparison.OrdinalIgnoreCase);
                    var pageBreadcrumb = $"{eventBreadcrumb} › Page {page.Number}";
                    entries.Add(new Entry(
                        StorySearchResultKind.Page,
                        page.DisplayName,
                        $"{page.ConditionsSummary} · {page.Blocks.Count} block{(page.Blocks.Count == 1 ? string.Empty : "s")}",
                        pageBreadcrumb,
                        StoryLocation.ForPage(map.Id, @event.Id, page.Number),
                        StoryLocationSourceKind.Map,
                        [page.Number.ToString(), page.DisplayName, page.ConditionsSummary],
                        [page.DisplayName, page.ConditionsSummary], [], [], [], string.Empty, hasConditions, false, false));

                    entries.AddRange(page.Blocks.SelectMany(block => CreateBlockEntry(
                        block,
                        StoryLocation.ForBlock(map.Id, @event.Id, page.Number, block),
                        StoryLocationSourceKind.Map,
                        pageBreadcrumb,
                        hasConditions)));
                }
            }
        }

        foreach (var commonEvent in workspace.CommonEvents.OrderBy(commonEvent => commonEvent.Id))
        {
            var breadcrumb = $"Common Events › {commonEvent.DisplayName}";
            entries.Add(new Entry(
                StorySearchResultKind.Event,
                commonEvent.DisplayName,
                $"Trigger {commonEvent.Trigger} · Switch {commonEvent.SwitchId} · {commonEvent.Blocks.Count} block{(commonEvent.Blocks.Count == 1 ? string.Empty : "s")}",
                breadcrumb,
                StoryLocation.ForCommonEvent(commonEvent.Id),
                StoryLocationSourceKind.CommonEvent,
                [commonEvent.Id.ToString(), commonEvent.Id.ToString("000"), commonEvent.Name, commonEvent.DisplayName],
                [commonEvent.Name, commonEvent.DisplayName], [], [], [], string.Empty, false, false, false));
            entries.AddRange(commonEvent.Blocks.SelectMany(block => CreateBlockEntry(
                block,
                StoryLocation.ForCommonEvent(commonEvent.Id, block),
                StoryLocationSourceKind.CommonEvent,
                breadcrumb,
                false)));
        }

        return new StorySearchIndex(entries);
    }

    public StorySearchQueryResult Search(StorySearchQuery? query = null)
    {
        query ??= new StorySearchQuery();
        var text = query.Text?.Trim() ?? string.Empty;
        var filters = query.Filters ?? new StorySearchFilters();
        var matches = new List<StorySearchResult>();

        foreach (var entry in _entries)
        {
            if (!MatchesFilters(entry, filters))
            {
                continue;
            }
            var rank = string.IsNullOrEmpty(text) ? 1000 : entry.MatchRank(text, filters.IncludeTechnicalRawData);
            if (rank < 0)
            {
                continue;
            }
            matches.Add(new StorySearchResult
            {
                Kind = entry.Kind,
                Title = entry.Title,
                Snippet = entry.Snippet,
                Breadcrumb = entry.Breadcrumb,
                Location = entry.Location,
                Rank = rank,
            });
        }

        var ordered = matches
            .OrderBy(result => result.Rank)
            .ThenBy(result => ResultKindPriority(result.Kind))
            .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Breadcrumb, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Location.MapId)
            .ThenBy(result => result.Location.EventId)
            .ThenBy(result => result.Location.PageNumber)
            .ThenBy(result => result.Location.CommandStartIndex)
            .ToArray();
        var maximum = Math.Clamp(query.MaximumResults, 1, 500);
        return new StorySearchQueryResult
        {
            Results = ordered.Take(maximum).ToArray(),
            TotalResults = ordered.Length,
        };
    }

    private static IEnumerable<Entry> CreateBlockEntry(
        StoryBlock block,
        StoryLocation location,
        StoryLocationSourceKind sourceKind,
        string breadcrumb,
        bool hasConditions)
    {
        var originValues = block.LocalizationOrigins
            .SelectMany(origin => new[] {origin.Path, origin.SourceFile})
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var speaker = block.Kind == StoryBlockKind.Dialogue ? block.Title : null;
        yield return new Entry(
            ToResultKind(block.Kind),
            block.Title,
            string.IsNullOrWhiteSpace(block.DisplayText) ? block.Summary : block.DisplayText,
            breadcrumb,
            location,
            sourceKind,
            [],
            [block.Title],
            speaker is null ? [] : [speaker],
            [block.DisplayText, block.Summary, block.RawText],
            originValues,
            block.RawParameters,
            hasConditions,
            block.LocalizationOrigins.Count > 0,
            IsEditableMaster(block));
    }

    private static bool MatchesFilters(Entry entry, StorySearchFilters filters)
    {
        if (filters.Source == StorySearchSourceFilter.Maps && entry.SourceKind != StoryLocationSourceKind.Map ||
            filters.Source == StorySearchSourceFilter.CommonEvents && entry.SourceKind != StoryLocationSourceKind.CommonEvent)
        {
            return false;
        }
        if (filters.ContentTypes.Count > 0 && (entry.ContentType is null || !filters.ContentTypes.Contains(entry.ContentType.Value)))
        {
            return false;
        }
        if (filters.LocalizedOnly && !entry.IsLocalized ||
            filters.RawLiteralOnly && (entry.IsLocalized || entry.ContentType is null) ||
            filters.EditableMasterOnly && !entry.IsEditableMaster ||
            filters.WithConditionsOnly && !entry.HasConditions ||
            filters.WithoutConditionsOnly && entry.HasConditions)
        {
            return false;
        }
        return true;
    }

    private static StorySearchResultKind ToResultKind(StoryBlockKind kind) => kind switch
    {
        StoryBlockKind.Dialogue => StorySearchResultKind.Dialogue,
        StoryBlockKind.Choices => StorySearchResultKind.Choices,
        StoryBlockKind.MovementRoute => StorySearchResultKind.Movement,
        StoryBlockKind.Animation => StorySearchResultKind.Animation,
        StoryBlockKind.Audio => StorySearchResultKind.Audio,
        StoryBlockKind.Wait or StoryBlockKind.SwitchVariable or StoryBlockKind.ControlFlow or
        StoryBlockKind.Screen or StoryBlockKind.Picture or StoryBlockKind.System or
        StoryBlockKind.Actor or StoryBlockKind.Battle => StorySearchResultKind.Logic,
        StoryBlockKind.Transfer => StorySearchResultKind.Transfer,
        StoryBlockKind.PluginCommand => StorySearchResultKind.Plugin,
        StoryBlockKind.Script => StorySearchResultKind.Script,
        StoryBlockKind.Comment => StorySearchResultKind.Comment,
        _ => StorySearchResultKind.Raw,
    };

    private static int ResultKindPriority(StorySearchResultKind kind) => kind switch
    {
        StorySearchResultKind.Map => 0,
        StorySearchResultKind.Event => 1,
        StorySearchResultKind.Page => 2,
        _ => 3,
    };

    private static StorySearchContentFilter ToContentFilter(StorySearchResultKind kind) => kind switch
    {
        StorySearchResultKind.Dialogue => StorySearchContentFilter.Dialogue,
        StorySearchResultKind.Choices => StorySearchContentFilter.Choices,
        StorySearchResultKind.Movement => StorySearchContentFilter.Movement,
        StorySearchResultKind.Animation => StorySearchContentFilter.Animation,
        StorySearchResultKind.Audio => StorySearchContentFilter.Audio,
        StorySearchResultKind.Logic => StorySearchContentFilter.Logic,
        StorySearchResultKind.Transfer => StorySearchContentFilter.Transfer,
        StorySearchResultKind.Plugin => StorySearchContentFilter.Plugin,
        StorySearchResultKind.Script => StorySearchContentFilter.Script,
        StorySearchResultKind.Comment => StorySearchContentFilter.Comment,
        _ => StorySearchContentFilter.Raw,
    };

    private static bool IsEditableMaster(StoryBlock block) =>
        block.Kind is StoryBlockKind.Dialogue or StoryBlockKind.Choices &&
        block.LocalizationOrigins.Any(origin =>
            origin.Locale.Equals("it", StringComparison.OrdinalIgnoreCase) && origin.Segments.Count > 0);

    private static IReadOnlyDictionary<int, string> BuildMapBreadcrumbs(IReadOnlyList<StoryMap> maps)
    {
        var values = new Dictionary<int, string>();
        void Add(StoryMapHierarchyNode node, string? parent)
        {
            var current = string.IsNullOrWhiteSpace(parent) ? node.Map.DisplayName : $"{parent} › {node.Map.DisplayName}";
            values[node.Map.Id] = current;
            foreach (var child in node.Children)
            {
                Add(child, current);
            }
        }
        foreach (var root in StoryMapHierarchy.Build(maps).Roots)
        {
            Add(root, null);
        }
        return values;
    }

    private sealed record Entry(
        StorySearchResultKind Kind,
        string Title,
        string Snippet,
        string Breadcrumb,
        StoryLocation Location,
        StoryLocationSourceKind SourceKind,
        IReadOnlyList<string> IdentifierValues,
        IReadOnlyList<string> TitleValues,
        IReadOnlyList<string> SpeakerValues,
        IReadOnlyList<string> DisplayValues,
        IReadOnlyList<string> LocalizationValues,
        string RawParameters,
        bool HasConditions,
        bool IsLocalized,
        bool IsEditableMaster)
    {
        public StorySearchContentFilter? ContentType => Kind is StorySearchResultKind.Map or StorySearchResultKind.Event or StorySearchResultKind.Page
            ? null
            : ToContentFilter(Kind);

        public int MatchRank(string query, bool includeTechnicalRawData)
        {
            if (IdentifierValues.Any(value => value.Equals(query, StringComparison.OrdinalIgnoreCase))) return 0;
            if (TitleValues.Any(value => value.Equals(query, StringComparison.OrdinalIgnoreCase))) return 10;
            if (TitleValues.Any(value => value.StartsWith(query, StringComparison.OrdinalIgnoreCase))) return 20;
            if (TitleValues.Any(value => Contains(value, query))) return 30;
            if (SpeakerValues.Any(value => Contains(value, query))) return 40;
            if (DisplayValues.Any(value => Contains(value, query))) return 50;
            if (LocalizationValues.Any(value => Contains(value, query))) return 70;
            if (includeTechnicalRawData && Contains(RawParameters, query)) return 80;
            return -1;
        }

        private static bool Contains(string value, string query) =>
            !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
