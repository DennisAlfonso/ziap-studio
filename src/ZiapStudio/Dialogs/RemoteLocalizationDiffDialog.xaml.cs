using Microsoft.UI.Xaml.Controls;
using ZiapStudio.Core.Localization;

namespace ZiapStudio.Dialogs;

public sealed partial class RemoteLocalizationDiffDialog : ContentDialog
{
    private const int MaximumDisplayedDifferences = 500;

    public RemoteLocalizationDiffDialog(
        PublishedLocalizationComparison comparison,
        bool allowSynchronization)
    {
        Comparison = comparison;
        DisplayedDifferences = comparison.Differences
            .Take(MaximumDisplayedDifferences)
            .ToArray();
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

    public IReadOnlyList<LocalizationJsonDifference> DisplayedDifferences { get; }

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
}
