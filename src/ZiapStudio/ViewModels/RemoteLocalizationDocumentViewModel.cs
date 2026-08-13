using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZiapStudio.Core.Localization;

namespace ZiapStudio.ViewModels;

public sealed class RemoteLocalizationDocumentViewModel : INotifyPropertyChanged
{
    private readonly List<RemoteLocalizationFileViewModel> _allFiles = [];
    private string _searchText = string.Empty;
    private RemoteLocalizationFilter _filter;
    private DateTimeOffset? _lastCheckedAt;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RemoteLocalizationFileViewModel> VisibleFiles { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public RemoteLocalizationFilter Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                OnPropertyChanged(nameof(FilterIndex));
                ApplyFilter();
            }
        }
    }

    public int FilterIndex
    {
        get => (int)Filter;
        set
        {
            if (Enum.IsDefined(typeof(RemoteLocalizationFilter), value))
            {
                Filter = (RemoteLocalizationFilter)value;
            }
        }
    }

    public int AlignedCount => Count(RemoteLocalizationAlignment.Aligned);

    public int DifferentCount => Count(RemoteLocalizationAlignment.Different);

    public int OnlyLocalCount => Count(RemoteLocalizationAlignment.MissingRemote);

    public int OnlyPublishedCount => Count(RemoteLocalizationAlignment.MissingLocal);

    public int ProblemCount => _allFiles.Count(file =>
        file.Status.Alignment != RemoteLocalizationAlignment.Aligned);

    public string VisibleCountText =>
        $"{VisibleFiles.Count} di {_allFiles.Count} file";

    public string LastCheckedText => _lastCheckedAt is null
        ? "Non ancora verificato"
        : $"Ultimo controllo: {_lastCheckedAt.Value.ToLocalTime():g}";

    public void Update(
        IEnumerable<RemoteLocalizationFileViewModel> files,
        DateTimeOffset checkedAt)
    {
        _allFiles.Clear();
        _allFiles.AddRange(files.OrderBy(file => file.Locale)
            .ThenBy(file => file.File, StringComparer.OrdinalIgnoreCase));
        _lastCheckedAt = checkedAt;
        NotifySummaryChanged();
        ApplyFilter();
    }

    public void Reset()
    {
        _allFiles.Clear();
        _lastCheckedAt = null;
        SearchText = string.Empty;
        Filter = RemoteLocalizationFilter.All;
        NotifySummaryChanged();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        var filtered = _allFiles.Where(file =>
            MatchesFilter(file) &&
            (search.Length == 0 ||
             file.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             file.StatusText.Contains(search, StringComparison.OrdinalIgnoreCase)));

        VisibleFiles.Clear();
        foreach (var file in filtered)
        {
            VisibleFiles.Add(file);
        }
        OnPropertyChanged(nameof(VisibleCountText));
    }

    private bool MatchesFilter(RemoteLocalizationFileViewModel file) => Filter switch
    {
        RemoteLocalizationFilter.Aligned =>
            file.Status.Alignment == RemoteLocalizationAlignment.Aligned,
        RemoteLocalizationFilter.Different =>
            file.Status.Alignment == RemoteLocalizationAlignment.Different,
        RemoteLocalizationFilter.OnlyLocal =>
            file.Status.Alignment == RemoteLocalizationAlignment.MissingRemote,
        RemoteLocalizationFilter.OnlyPublished =>
            file.Status.Alignment == RemoteLocalizationAlignment.MissingLocal,
        RemoteLocalizationFilter.Problems =>
            file.Status.Alignment != RemoteLocalizationAlignment.Aligned,
        _ => true,
    };

    private int Count(RemoteLocalizationAlignment alignment) =>
        _allFiles.Count(file => file.Status.Alignment == alignment);

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(AlignedCount));
        OnPropertyChanged(nameof(DifferentCount));
        OnPropertyChanged(nameof(OnlyLocalCount));
        OnPropertyChanged(nameof(OnlyPublishedCount));
        OnPropertyChanged(nameof(ProblemCount));
        OnPropertyChanged(nameof(LastCheckedText));
    }

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
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

public enum RemoteLocalizationFilter
{
    All,
    Problems,
    Aligned,
    Different,
    OnlyLocal,
    OnlyPublished,
}
