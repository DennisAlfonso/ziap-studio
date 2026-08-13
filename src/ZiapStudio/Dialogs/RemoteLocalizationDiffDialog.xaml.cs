using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ZiapStudio.Core.Localization;

namespace ZiapStudio.Dialogs;

public sealed partial class RemoteLocalizationDiffDialog : ContentDialog
{
    private const int MaximumDisplayedDifferences = 500;
    private readonly ObservableCollection<LocalizationJsonDifference> _displayedDifferences = [];

    public RemoteLocalizationDiffDialog(
        PublishedLocalizationComparison comparison,
        bool allowSynchronization)
    {
        Comparison = comparison;
        ApplyDifferenceFilter(kind: null);
        Title = $"Confronto {comparison.Locale}/{comparison.File}";
        CloseButtonText = allowSynchronization ? "Annulla" : "Chiudi";
        if (allowSynchronization)
        {
            PrimaryButtonText = comparison.LocalFileExists
                ? "Aggiorna dal pubblicato"
                : "Scarica";
        }
        InitializeComponent();
    }

    private PublishedLocalizationComparison Comparison { get; }

    public IReadOnlyList<LocalizationJsonDifference> DisplayedDifferences =>
        _displayedDifferences;

    public string AllDifferencesLabel => $"Tutte  {Comparison.Differences.Count}";

    public string ModifiedDifferencesLabel => $"Modificati  {Comparison.ModifiedCount}";

    public string OnlyLocalDifferencesLabel => $"Solo locali  {Comparison.OnlyLocalCount}";

    public string OnlyPublishedDifferencesLabel =>
        $"Solo remoti  {Comparison.OnlyPublishedCount}";

    public string VisibleDifferencesText =>
        $"{DisplayedDifferences.Count} visualizzate";

    public string SummaryText =>
        $"Versione pubblicata {Comparison.VersionId} · " +
        $"{Comparison.ModifiedCount} modificati · " +
        $"{Comparison.OnlyLocalCount} solo locali · " +
        $"{Comparison.OnlyPublishedCount} solo remoti" +
        (Comparison.Differences.Count > MaximumDisplayedDifferences
            ? $" · visualizzati i primi {MaximumDisplayedDifferences}"
            : string.Empty);

    public string DestinationText => $"Destinazione locale: {Comparison.DestinationPath}";

    public string SafetyText => Comparison.LocalFileExists
        ? "Il file locale verrà sostituito solo dopo una nuova verifica di versione, " +
          "checksum, JSON e modifiche intervenute dal confronto."
        : "Il file verrà creato solo dopo una nuova verifica di versione, checksum e JSON.";

    private void DifferenceFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected || selected.Tag is not string filter)
        {
            return;
        }

        if (selected.IsChecked != true)
        {
            AllDifferencesFilter.IsChecked = true;
            ApplyDifferenceFilter(kind: null);
            return;
        }

        AllDifferencesFilter.IsChecked = ReferenceEquals(selected, AllDifferencesFilter);
        ModifiedDifferencesFilter.IsChecked = ReferenceEquals(selected, ModifiedDifferencesFilter);
        OnlyLocalDifferencesFilter.IsChecked = ReferenceEquals(selected, OnlyLocalDifferencesFilter);
        OnlyPublishedDifferencesFilter.IsChecked =
            ReferenceEquals(selected, OnlyPublishedDifferencesFilter);
        var kind = filter switch
        {
            "Modified" => LocalizationDifferenceKind.Modified,
            "OnlyLocal" => LocalizationDifferenceKind.OnlyLocal,
            "OnlyPublished" => LocalizationDifferenceKind.OnlyPublished,
            _ => (LocalizationDifferenceKind?)null,
        };
        ApplyDifferenceFilter(kind);
    }

    private void ApplyDifferenceFilter(LocalizationDifferenceKind? kind)
    {
        var differences = Comparison.Differences
            .Where(difference => kind is null || difference.Kind == kind)
            .Take(MaximumDisplayedDifferences);
        _displayedDifferences.Clear();
        foreach (var difference in differences)
        {
            _displayedDifferences.Add(difference);
        }

        if (VisibleDifferencesCountText is not null)
        {
            VisibleDifferencesCountText.Text = VisibleDifferencesText;
        }
    }
}
