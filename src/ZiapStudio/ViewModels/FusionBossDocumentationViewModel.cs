using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZiapStudio.Core.Fusion.Bosses;

namespace ZiapStudio.ViewModels;

public sealed class FusionBossDocumentationViewModel : INotifyPropertyChanged
{
    private string _searchText = string.Empty;
    private string _selectedCategory = AllCategories;
    private const string AllCategories = "Tutte le categorie";

    public FusionBossDocumentationViewModel(FusionBossDocumentation documentation)
    {
        Documentation = documentation;
        Categories = new[] { AllCategories }
            .Concat(documentation.Actions
                .Select(action => action.Category)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
        ApplyFilter();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FusionBossDocumentation Documentation { get; }

    public IReadOnlyList<string> Categories { get; }

    public ObservableCollection<FusionBossActionApiEntry> VisibleActions { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplyFilter();
            }
        }
    }

    public string ActionCountText => Documentation.Actions.Count == 1
        ? "1 azione esposta dal runtime"
        : $"{Documentation.Actions.Count} azioni esposte dal runtime";

    public string VisibleActionCountText => VisibleActions.Count == Documentation.Actions.Count
        ? ActionCountText
        : $"{VisibleActions.Count} di {Documentation.Actions.Count} azioni";

    public string ProviderCountText
    {
        get
        {
            var count = Documentation.Actions
                .Select(action => action.Provider)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return count == 1 ? "1 provider attivo" : $"{count} provider attivi";
        }
    }

    public string DiagnosticText => Documentation.Diagnostics.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, Documentation.Diagnostics);

    public string EmptyText => VisibleActions.Count == 0
        ? "Nessuna azione corrisponde ai filtri correnti."
        : string.Empty;

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        var actions = Documentation.Actions.Where(action =>
            (SelectedCategory == AllCategories || action.Category.Equals(
                SelectedCategory,
                StringComparison.CurrentCultureIgnoreCase)) &&
            (search.Length == 0 ||
                action.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                action.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                action.Provider.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                action.Signature.Contains(search, StringComparison.OrdinalIgnoreCase)));
        VisibleActions.Clear();
        foreach (var action in actions)
        {
            VisibleActions.Add(action);
        }
        OnPropertyChanged(nameof(VisibleActionCountText));
        OnPropertyChanged(nameof(EmptyText));
    }

    private bool SetProperty<T>(
        ref T storage,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
