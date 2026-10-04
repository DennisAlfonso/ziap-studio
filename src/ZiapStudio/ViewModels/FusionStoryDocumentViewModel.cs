using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Story;

namespace ZiapStudio.ViewModels;

public sealed class FusionStoryDocumentViewModel : INotifyPropertyChanged
{
    private string _searchText = string.Empty;
    private StoryNavigationItem? _selectedSource;
    private StoryBlock? _selectedBlock;

    public FusionStoryDocumentViewModel(FusionStoryWorkspaceDocument document)
    {
        Document = document;
        RebuildNavigation();
        SelectedSource = NavigationItems.FirstOrDefault(item => item.IsSelectable);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FusionStoryWorkspaceDocument Document { get; }

    public IReadOnlyList<StoryNavigationItem> NavigationItems { get; private set; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RebuildNavigation();
            }
        }
    }

    public StoryNavigationItem? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (value is null || !value.IsSelectable || ReferenceEquals(_selectedSource, value))
            {
                return;
            }
            _selectedSource = value;
            SelectedBlock = value.Blocks.FirstOrDefault();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Blocks));
            OnPropertyChanged(nameof(SelectedSourceTitle));
            OnPropertyChanged(nameof(SelectedSourceDetail));
        }
    }

    public IReadOnlyList<StoryBlock> Blocks => SelectedSource?.Blocks ?? [];

    public StoryBlock? SelectedBlock
    {
        get => _selectedBlock;
        set
        {
            if (ReferenceEquals(_selectedBlock, value))
            {
                return;
            }
            _selectedBlock = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedBlockDetails));
            OnPropertyChanged(nameof(SelectedBlockRawCommands));
        }
    }

    public IReadOnlyList<string> SelectedBlockDetails => SelectedBlock?.Details ?? [];

    public IReadOnlyList<StoryRawCommand> SelectedBlockRawCommands => SelectedBlock?.SourceCommands ?? [];

    public string SelectedSourceTitle => SelectedSource?.Title ?? "Seleziona una page o un Common Event";

    public string SelectedSourceDetail => SelectedSource?.Context ?? "La timeline è read-only.";

    public string WorkspaceStatusText => Document.ErrorCount > 0
        ? $"{Document.ErrorCount} errori · {Document.WarningCount} avvisi"
        : $"{Document.PageCount} page · {Document.BlockCount} blocchi · read-only";

    public string SearchStatusText => string.IsNullOrWhiteSpace(SearchText)
        ? "Cerca mappe, eventi, speaker, testo risolto o chiavi raw."
        : $"{NavigationItems.Count(item => item.IsSelectable)} risultati navigabili";

    private void RebuildNavigation()
    {
        var filter = SearchText.Trim();
        var items = new List<StoryNavigationItem>();
        if (Document.Workspace.Maps.Count > 0)
        {
            items.Add(StoryNavigationItem.Group("Maps", 0));
        }
        foreach (var map in Document.Workspace.Maps)
        {
            var matchingEvents = map.Events
                .Select(@event => new { Event = @event, Pages = @event.Pages.Where(page =>
                    string.IsNullOrWhiteSpace(filter) || Matches(map, @event, page, filter)).ToArray() })
                .Where(item => item.Pages.Length > 0 ||
                    (!string.IsNullOrWhiteSpace(filter) && Contains(map.DisplayName, filter) || Contains(item.Event.DisplayName, filter)))
                .ToArray();
            if (!string.IsNullOrWhiteSpace(filter) && matchingEvents.Length == 0 && !Contains(map.DisplayName, filter))
            {
                continue;
            }
            items.Add(StoryNavigationItem.Group(map.DisplayName, 1, map.SourcePath));
            foreach (var match in matchingEvents)
            {
                items.Add(StoryNavigationItem.Group(match.Event.DisplayName, 2,
                    $"x {match.Event.X} · y {match.Event.Y}"));
                var pages = string.IsNullOrWhiteSpace(filter) || Contains(map.DisplayName, filter) || Contains(match.Event.DisplayName, filter)
                    ? match.Event.Pages
                    : match.Pages;
                foreach (var page in pages)
                {
                    items.Add(StoryNavigationItem.Source(
                        page.DisplayName,
                        3,
                        $"{map.DisplayName} · {match.Event.DisplayName} · {page.ConditionsSummary}",
                        page.Blocks));
                }
            }
        }

        var commonEvents = Document.Workspace.CommonEvents
            .Where(@event => string.IsNullOrWhiteSpace(filter) || Matches(@event, filter))
            .ToArray();
        if (commonEvents.Length > 0 || string.IsNullOrWhiteSpace(filter) && Document.Workspace.CommonEvents.Count > 0)
        {
            items.Add(StoryNavigationItem.Group("Common Events", 0));
            foreach (var commonEvent in commonEvents)
            {
                items.Add(StoryNavigationItem.Source(
                    commonEvent.DisplayName,
                    1,
                    $"Trigger {commonEvent.Trigger} · Switch {commonEvent.SwitchId}",
                    commonEvent.Blocks));
            }
        }

        NavigationItems = items;
        OnPropertyChanged(nameof(NavigationItems));
        OnPropertyChanged(nameof(SearchStatusText));
        if (_selectedSource is not null && !NavigationItems.Contains(_selectedSource))
        {
            _selectedSource = null;
            SelectedSource = NavigationItems.FirstOrDefault(item => item.IsSelectable);
        }
    }

    private static bool Matches(StoryMap map, StoryEvent @event, StoryPage page, string filter) =>
        Contains(map.DisplayName, filter) || Contains(@event.DisplayName, filter) ||
        page.Blocks.Any(block => Matches(block, filter));

    private static bool Matches(StoryCommonEvent @event, string filter) =>
        Contains(@event.DisplayName, filter) || @event.Blocks.Any(block => Matches(block, filter));

    private static bool Matches(StoryBlock block, string filter) =>
        Contains(block.Title, filter) || Contains(block.Summary, filter) ||
        Contains(block.DisplayText, filter) || Contains(block.RawText, filter) ||
        Contains(block.RawParameters, filter) || block.LocalizationOrigins.Any(origin =>
            Contains(origin.Path, filter) || Contains(origin.SourceFile, filter));

    private static bool Contains(string value, string filter) =>
        value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record StoryNavigationItem
{
    public required string Title { get; init; }
    public required int Depth { get; init; }
    public string Context { get; init; } = string.Empty;
    public IReadOnlyList<StoryBlock> Blocks { get; init; } = [];
    public bool IsSelectable { get; init; }

    public string DisplayTitle => string.Concat(Enumerable.Repeat("   ", Depth)) + Title;
    public string KindText => IsSelectable ? "Timeline" : "";

    public static StoryNavigationItem Group(string title, int depth, string? context = null) => new()
    {
        Title = title,
        Depth = depth,
        Context = context ?? string.Empty,
    };

    public static StoryNavigationItem Source(
        string title,
        int depth,
        string context,
        IReadOnlyList<StoryBlock> blocks) => new()
    {
        Title = title,
        Depth = depth,
        Context = context,
        Blocks = blocks,
        IsSelectable = true,
    };
}
