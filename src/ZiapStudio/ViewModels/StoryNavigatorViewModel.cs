using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZiapStudio.Core.Fusion.Story;

namespace ZiapStudio.ViewModels;

public enum StoryNavigatorSourceMode
{
    Maps,
    CommonEvents,
}

/// <summary>Stateful map node used only by the WinUI Story navigator.</summary>
public sealed class StoryMapTreeItemViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    internal StoryMapTreeItemViewModel(StoryMap map, IEnumerable<StoryMapTreeItemViewModel> children, bool isExpanded)
    {
        Map = map;
        Children = new ObservableCollection<StoryMapTreeItemViewModel>(children);
        _isExpanded = isExpanded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public StoryMap Map { get; }
    public ObservableCollection<StoryMapTreeItemViewModel> Children { get; }
    public string IdText => Map.Id.ToString("000");
    public string Name => Map.Name;
    public string DisplayName => Map.DisplayName;
    public string CountText => $"{Map.Events.Count} event{(Map.Events.Count == 1 ? string.Empty : "i")}";
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public sealed class StoryEventNavigatorItemViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    internal StoryEventNavigatorItemViewModel(int mapId, StoryEvent @event, bool isExpanded)
    {
        MapId = mapId;
        Event = @event;
        _isExpanded = isExpanded;
        Pages = new ObservableCollection<StoryPageNavigatorItemViewModel>(
            @event.Pages.OrderBy(page => page.Number)
                .Select(page => new StoryPageNavigatorItemViewModel(mapId, @event.Id, page)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public int MapId { get; }
    public StoryEvent Event { get; }
    public ObservableCollection<StoryPageNavigatorItemViewModel> Pages { get; }
    public string IdText => Event.Id.ToString();
    public string Name => string.IsNullOrWhiteSpace(Event.Name) ? $"Event {Event.Id}" : Event.Name;
    public string CoordinatesAndPages => $"x{Event.X} · y{Event.Y} · {Event.Pages.Count} page{(Event.Pages.Count == 1 ? string.Empty : "s")}";
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public sealed class StoryPageNavigatorItemViewModel
{
    internal StoryPageNavigatorItemViewModel(int mapId, int eventId, StoryPage page)
    {
        MapId = mapId;
        EventId = eventId;
        Page = page;
    }

    public int MapId { get; }
    public int EventId { get; }
    public StoryPage Page { get; }
    public string Title => Page.DisplayName;
    public string Detail => Page.Blocks.Count == 0
        ? "Empty"
        : $"{Page.ConditionsSummary} · {Page.Blocks.Count} block{(Page.Blocks.Count == 1 ? string.Empty : "s")}";
    public StoryLocation Location => StoryLocation.ForPage(MapId, EventId, Page.Number);
}

public sealed class StoryCommonEventNavigatorItemViewModel
{
    internal StoryCommonEventNavigatorItemViewModel(StoryCommonEvent commonEvent) => CommonEvent = commonEvent;

    public StoryCommonEvent CommonEvent { get; }
    public string IdText => CommonEvent.Id.ToString("000");
    public string Title => string.IsNullOrWhiteSpace(CommonEvent.Name) ? $"Common Event {CommonEvent.Id}" : CommonEvent.Name;
    public string Detail => $"Trigger {CommonEvent.Trigger} · Switch {CommonEvent.SwitchId} · {CommonEvent.Blocks.Count} block{(CommonEvent.Blocks.Count == 1 ? string.Empty : "s")}";
    public StoryLocation Location => StoryLocation.ForCommonEvent(CommonEvent.Id);
}

/// <summary>Compact filter state which deliberately stays separate from search indexing.</summary>
public sealed class StorySearchFiltersViewModel : INotifyPropertyChanged
{
    private StorySearchSourceFilter _source;
    private bool _dialogue, _choices, _movement, _animation, _audio, _logic, _transfer, _plugin, _script, _comment, _raw;
    private bool _localized, _literal, _editableMaster, _withConditions, _withoutConditions, _technical;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;

    public StorySearchSourceFilter Source { get => _source; set => Set(ref _source, value); }
    public bool Dialogue { get => _dialogue; set => Set(ref _dialogue, value); }
    public bool Choices { get => _choices; set => Set(ref _choices, value); }
    public bool Movement { get => _movement; set => Set(ref _movement, value); }
    public bool Animation { get => _animation; set => Set(ref _animation, value); }
    public bool Audio { get => _audio; set => Set(ref _audio, value); }
    public bool Logic { get => _logic; set => Set(ref _logic, value); }
    public bool Transfer { get => _transfer; set => Set(ref _transfer, value); }
    public bool Plugin { get => _plugin; set => Set(ref _plugin, value); }
    public bool Script { get => _script; set => Set(ref _script, value); }
    public bool Comment { get => _comment; set => Set(ref _comment, value); }
    public bool Raw { get => _raw; set => Set(ref _raw, value); }
    public bool Localized { get => _localized; set => Set(ref _localized, value); }
    public bool Literal { get => _literal; set => Set(ref _literal, value); }
    public bool EditableMaster { get => _editableMaster; set => Set(ref _editableMaster, value); }
    public bool WithConditions { get => _withConditions; set => Set(ref _withConditions, value); }
    public bool WithoutConditions { get => _withoutConditions; set => Set(ref _withoutConditions, value); }
    public bool Technical { get => _technical; set => Set(ref _technical, value); }

    public int ActiveCount => ToFilters().HasActiveFilters
        ? (Source == StorySearchSourceFilter.All ? 0 : 1) + ContentTypes().Count +
          new[] {Localized, Literal, EditableMaster, WithConditions, WithoutConditions, Technical}.Count(value => value)
        : 0;

    public StorySearchFilters ToFilters() => new()
    {
        Source = Source,
        ContentTypes = ContentTypes(),
        LocalizedOnly = Localized,
        RawLiteralOnly = Literal,
        EditableMasterOnly = EditableMaster,
        WithConditionsOnly = WithConditions,
        WithoutConditionsOnly = WithoutConditions,
        IncludeTechnicalRawData = Technical,
    };

    public void Clear()
    {
        _source = StorySearchSourceFilter.All;
        _dialogue = _choices = _movement = _animation = _audio = _logic = _transfer = _plugin = _script = _comment = _raw = false;
        _localized = _literal = _editableMaster = _withConditions = _withoutConditions = _technical = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlySet<StorySearchContentFilter> ContentTypes()
    {
        var result = new HashSet<StorySearchContentFilter>();
        if (Dialogue) result.Add(StorySearchContentFilter.Dialogue);
        if (Choices) result.Add(StorySearchContentFilter.Choices);
        if (Movement) result.Add(StorySearchContentFilter.Movement);
        if (Animation) result.Add(StorySearchContentFilter.Animation);
        if (Audio) result.Add(StorySearchContentFilter.Audio);
        if (Logic) result.Add(StorySearchContentFilter.Logic);
        if (Transfer) result.Add(StorySearchContentFilter.Transfer);
        if (Plugin) result.Add(StorySearchContentFilter.Plugin);
        if (Script) result.Add(StorySearchContentFilter.Script);
        if (Comment) result.Add(StorySearchContentFilter.Comment);
        if (Raw) result.Add(StorySearchContentFilter.Raw);
        return result;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveCount)));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Hierarchical navigation/search state. It owns no authoring or file mutation
/// behavior; callers receive semantic <see cref="StoryLocation"/> notifications.
/// </summary>
public sealed class StoryNavigatorViewModel : INotifyPropertyChanged, IDisposable
{
    private StoryWorkspace _workspace;
    private StorySearchIndex _searchIndex;
    private CancellationTokenSource? _searchCancellation;
    private string _searchText = string.Empty;
    private StoryNavigatorSourceMode _sourceMode;
    private StoryMapTreeItemViewModel? _selectedMap;
    private StoryEventNavigatorItemViewModel? _selectedEvent;
    private StoryPageNavigatorItemViewModel? _selectedPage;
    private StoryCommonEventNavigatorItemViewModel? _selectedCommonEvent;
    private IReadOnlyList<StorySearchResult> _searchResults = [];
    private int _searchTotal;
    private readonly Dictionary<(int MapId, int EventId), bool> _eventExpansion = [];

    public StoryNavigatorViewModel(StoryWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _searchIndex = StorySearchIndex.Build(workspace);
        Filters = new StorySearchFiltersViewModel();
        Filters.Changed += (_, _) => RefreshSearch();
        RebuildTree(null, selectDefault: true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<StoryLocation>? LocationSelected;

    public ObservableCollection<StoryMapTreeItemViewModel> MapRoots { get; } = [];
    public ObservableCollection<StoryEventNavigatorItemViewModel> Events { get; } = [];
    public ObservableCollection<StoryCommonEventNavigatorItemViewModel> CommonEvents { get; } = [];
    public StorySearchFiltersViewModel Filters { get; }

    public StoryNavigatorSourceMode SourceMode
    {
        get => _sourceMode;
        private set
        {
            if (Set(ref _sourceMode, value)) NotifyModeChanged();
        }
    }
    public StoryMapTreeItemViewModel? SelectedMap { get => _selectedMap; private set => Set(ref _selectedMap, value); }
    public StoryEventNavigatorItemViewModel? SelectedEvent { get => _selectedEvent; private set => Set(ref _selectedEvent, value); }
    public StoryPageNavigatorItemViewModel? SelectedPage { get => _selectedPage; private set => Set(ref _selectedPage, value); }
    public StoryCommonEventNavigatorItemViewModel? SelectedCommonEvent { get => _selectedCommonEvent; private set => Set(ref _selectedCommonEvent, value); }
    public IReadOnlyList<StorySearchResult> SearchResults { get => _searchResults; private set => Set(ref _searchResults, value); }
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? string.Empty))
            {
                RefreshSearch();
            }
        }
    }
    public bool IsSearchMode => !string.IsNullOrWhiteSpace(SearchText) || Filters.ToFilters().HasActiveFilters;
    public bool IsMapsMode => !IsSearchMode && SourceMode == StoryNavigatorSourceMode.Maps;
    public bool IsCommonEventsMode => !IsSearchMode && SourceMode == StoryNavigatorSourceMode.CommonEvents;
    public int SearchTotal => _searchTotal;
    public string FilterLabel => Filters.ActiveCount == 0 ? "Filters" : $"Filters · {Filters.ActiveCount}";
    public string SearchStatusText => !IsSearchMode
        ? "Scegli una mappa, poi un evento e una page."
        : _searchTotal == 0 ? "Nessun risultato."
        : _searchTotal > SearchResults.Count ? $"{_searchTotal}+ risultati — affina la ricerca." : $"{_searchTotal} risultati";

    public void ShowMaps()
    {
        SourceMode = StoryNavigatorSourceMode.Maps;
    }

    public void ShowCommonEvents()
    {
        SourceMode = StoryNavigatorSourceMode.CommonEvents;
    }

    public void ClearSearch()
    {
        SearchText = string.Empty;
        Filters.Clear();
    }

    public void SelectMap(StoryMapTreeItemViewModel map)
    {
        ArgumentNullException.ThrowIfNull(map);
        SourceMode = StoryNavigatorSourceMode.Maps;
        var wasSelected = ReferenceEquals(SelectedMap, map) && Events.Count > 0;
        SetSelectedMap(map);
        map.IsExpanded = true;
        if (!wasSelected)
        {
            BuildEvents(map.Map.Id);
        }
    }

    public void SelectEvent(StoryEventNavigatorItemViewModel @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        SelectMapById(@event.MapId);
        if (SelectedEvent is not null) SelectedEvent.IsSelected = false;
        SelectedEvent = @event;
        @event.IsSelected = true;
        @event.IsExpanded = true;
    }

    public void SelectPage(StoryPageNavigatorItemViewModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        SelectMapById(page.MapId);
        var @event = Events.FirstOrDefault(candidate => candidate.Event.Id == page.EventId);
        if (@event is not null) SelectEvent(@event);
        SelectedPage = page;
        LocationSelected?.Invoke(this, page.Location);
    }

    public void SelectCommonEvent(StoryCommonEventNavigatorItemViewModel commonEvent)
    {
        ArgumentNullException.ThrowIfNull(commonEvent);
        SourceMode = StoryNavigatorSourceMode.CommonEvents;
        SelectedCommonEvent = commonEvent;
        LocationSelected?.Invoke(this, commonEvent.Location);
    }

    public void Navigate(StoryLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (location.SourceKind == StoryLocationSourceKind.CommonEvent)
        {
            var common = CommonEvents.FirstOrDefault(item => item.CommonEvent.Id == location.EventId);
            if (common is not null)
            {
                SourceMode = StoryNavigatorSourceMode.CommonEvents;
                SelectedCommonEvent = common;
                LocationSelected?.Invoke(this, location);
            }
            return;
        }
        if (location.MapId is not { } mapId) return;
        SelectMapById(mapId);
        if (location.EventId is not { } eventId) return;
        var @event = Events.FirstOrDefault(item => item.Event.Id == eventId);
        if (@event is null) return;
        SelectEvent(@event);
        if (location.PageNumber is not { } pageNumber) return;
        var page = @event.Pages.FirstOrDefault(item => item.Page.Number == pageNumber);
        if (page is null) return;
        SelectedPage = page;
        LocationSelected?.Invoke(this, location);
    }

    public void Navigate(StorySearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Navigate(result.Location);
    }

    /// <summary>Retains expansion and restores a semantic selection after a reload.</summary>
    public void Reload(StoryWorkspace workspace, StoryLocation? preferredLocation)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var mapExpansion = FlattenMaps(MapRoots).ToDictionary(item => item.Map.Id, item => item.IsExpanded);
        var eventExpansion = Events.ToDictionary(item => (item.MapId, item.Event.Id), item => item.IsExpanded);
        _workspace = workspace;
        _searchIndex = StorySearchIndex.Build(workspace);
        RebuildTree(preferredLocation, selectDefault: preferredLocation is null, mapExpansion, eventExpansion);
        RefreshSearch(immediate: true);
    }

    /// <summary>Debounced rebuild used after a local authoring overlay changes visible strings.</summary>
    public void RefreshSearchIndex(Func<StoryBlock, StoryBlock> projectBlock)
    {
        ArgumentNullException.ThrowIfNull(projectBlock);
        _searchCancellation?.Cancel();
        var tokenSource = _searchCancellation = new CancellationTokenSource();
        _ = RefreshSearchIndexAsync(projectBlock, tokenSource.Token);
    }

    private async Task RefreshSearchIndexAsync(Func<StoryBlock, StoryBlock> projectBlock, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(200, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _searchIndex = StorySearchIndex.Build(ProjectWorkspace(projectBlock));
            RefreshSearch(immediate: true);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private StoryWorkspace ProjectWorkspace(Func<StoryBlock, StoryBlock> projectBlock) => _workspace with
    {
        Maps = _workspace.Maps.Select(map => map with
        {
            Events = map.Events.Select(@event => @event with
            {
                Pages = @event.Pages.Select(page => page with {Blocks = page.Blocks.Select(projectBlock).ToArray()}).ToArray(),
            }).ToArray(),
        }).ToArray(),
        CommonEvents = _workspace.CommonEvents.Select(@event => @event with
        {
            Blocks = @event.Blocks.Select(projectBlock).ToArray(),
        }).ToArray(),
    };

    private void RefreshSearch(bool immediate = false)
    {
        OnPropertyChanged(nameof(IsSearchMode));
        OnPropertyChanged(nameof(IsMapsMode));
        OnPropertyChanged(nameof(IsCommonEventsMode));
        OnPropertyChanged(nameof(FilterLabel));
        _searchCancellation?.Cancel();
        var tokenSource = _searchCancellation = new CancellationTokenSource();
        _ = RefreshSearchAsync(immediate ? TimeSpan.Zero : TimeSpan.FromMilliseconds(200), tokenSource.Token);
    }

    private async Task RefreshSearchAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSearchMode)
            {
                SearchResults = [];
                _searchTotal = 0;
            }
            else
            {
                var result = _searchIndex.Search(new StorySearchQuery {Text = SearchText, Filters = Filters.ToFilters(), MaximumResults = 200});
                SearchResults = result.Results;
                _searchTotal = result.TotalResults;
            }
            OnPropertyChanged(nameof(SearchTotal));
            OnPropertyChanged(nameof(SearchStatusText));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RebuildTree(
        StoryLocation? preferredLocation,
        bool selectDefault,
        IReadOnlyDictionary<int, bool>? mapExpansion = null,
        IReadOnlyDictionary<(int MapId, int EventId), bool>? eventExpansion = null)
    {
        SelectedMap = null;
        SelectedEvent = null;
        SelectedPage = null;
        SelectedCommonEvent = null;
        MapRoots.Clear();
        StoryMapTreeItemViewModel CreateNode(StoryMapHierarchyNode node) => new(
            node.Map,
            node.Children.Select(CreateNode),
            mapExpansion?.TryGetValue(node.Map.Id, out var isExpanded) == true ? isExpanded : node.Map.RpgMakerExpanded);
        foreach (var root in StoryMapHierarchy.Build(_workspace.Maps).Roots)
        {
            MapRoots.Add(CreateNode(root));
        }
        CommonEvents.Clear();
        foreach (var common in _workspace.CommonEvents.OrderBy(common => common.Id))
        {
            CommonEvents.Add(new StoryCommonEventNavigatorItemViewModel(common));
        }

        var mapId = preferredLocation?.MapId ?? SelectedMap?.Map.Id ?? MapRoots.FirstOrDefault()?.Map.Id;
        if (mapId is not null) SelectMapById(mapId.Value, eventExpansion);
        if (preferredLocation is not null)
        {
            Navigate(preferredLocation);
        }
        else if (selectDefault)
        {
            var firstPage = Events.SelectMany(@event => @event.Pages).FirstOrDefault();
            if (firstPage is not null) SelectPage(firstPage);
        }
        OnPropertyChanged(nameof(MapRoots));
        OnPropertyChanged(nameof(CommonEvents));
    }

    private void SelectMapById(int mapId, IReadOnlyDictionary<(int MapId, int EventId), bool>? eventExpansion = null)
    {
        var map = FlattenMaps(MapRoots).FirstOrDefault(candidate => candidate.Map.Id == mapId);
        if (map is null) return;
        var isAlreadyMaterialized = ReferenceEquals(SelectedMap, map) && Events.Count > 0;
        SetSelectedMap(map);
        if (!isAlreadyMaterialized)
        {
            BuildEvents(mapId, eventExpansion);
        }
    }

    private void SetSelectedMap(StoryMapTreeItemViewModel map)
    {
        if (SelectedMap is not null) SelectedMap.IsSelected = false;
        SelectedMap = map;
        map.IsSelected = true;
    }

    private void BuildEvents(int mapId, IReadOnlyDictionary<(int MapId, int EventId), bool>? eventExpansion = null)
    {
        foreach (var existing in Events)
        {
            _eventExpansion[(existing.MapId, existing.Event.Id)] = existing.IsExpanded;
        }
        var map = _workspace.Maps.FirstOrDefault(candidate => candidate.Id == mapId);
        if (map is null) return;
        Events.Clear();
        foreach (var @event in map.Events.OrderBy(@event => @event.Id))
        {
            var key = (mapId, @event.Id);
            var isExpanded = eventExpansion?.TryGetValue(key, out var restoredExpansion) == true
                ? restoredExpansion
                : _eventExpansion.TryGetValue(key, out var rememberedExpansion) && rememberedExpansion;
            var item = new StoryEventNavigatorItemViewModel(mapId, @event, isExpanded);
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(StoryEventNavigatorItemViewModel.IsExpanded))
                {
                    _eventExpansion[key] = item.IsExpanded;
                }
            };
            Events.Add(item);
        }
        SelectedEvent = null;
        SelectedPage = null;
        OnPropertyChanged(nameof(Events));
    }

    private static IEnumerable<StoryMapTreeItemViewModel> FlattenMaps(IEnumerable<StoryMapTreeItemViewModel> maps) =>
        maps.SelectMany(map => new[] {map}.Concat(FlattenMaps(map.Children)));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(property);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(IsMapsMode));
        OnPropertyChanged(nameof(IsCommonEventsMode));
    }

    public void Dispose()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        Filters.Changed -= (_, _) => RefreshSearch();
    }
}
