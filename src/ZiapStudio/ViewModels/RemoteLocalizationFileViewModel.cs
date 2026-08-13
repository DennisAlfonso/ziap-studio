using ZiapStudio.Core.Localization;
using ZiapStudio.Services.Integration.Remote;

namespace ZiapStudio.ViewModels;

public sealed class RemoteLocalizationFileViewModel
{
    public RemoteLocalizationFileViewModel(RemoteLocalizationFileStatus status)
    {
        Status = status;
    }

    public RemoteLocalizationFileStatus Status { get; }

    public string Locale => Status.Locale;

    public string File => Status.File;

    public string DisplayName => $"{Locale}/{File}";

    public string StatusGlyph => Status.Alignment switch
    {
        RemoteLocalizationAlignment.Aligned => "✓",
        RemoteLocalizationAlignment.Different => "⚠",
        RemoteLocalizationAlignment.MissingLocal => "↓",
        RemoteLocalizationAlignment.MissingRemote => "↑",
        RemoteLocalizationAlignment.Error => "×",
        _ => "?",
    };

    public string StatusText => Status.Alignment switch
    {
        RemoteLocalizationAlignment.Aligned => "Allineato",
        RemoteLocalizationAlignment.Different => "Differente dal pubblicato",
        RemoteLocalizationAlignment.MissingLocal => "Mancante localmente",
        RemoteLocalizationAlignment.MissingRemote => "Mancante nel remoto",
        RemoteLocalizationAlignment.Error => "Errore di lettura",
        _ => "Stato non determinabile",
    };

    public string ChecksumText => Status.Alignment switch
    {
        RemoteLocalizationAlignment.Aligned =>
            $"Locale e remoto: {ShortChecksum(Status.LocalChecksum)}",
        RemoteLocalizationAlignment.Different =>
            $"Locale {ShortChecksum(Status.LocalChecksum)} · Remoto {ShortChecksum(Status.Remote?.Checksum)}",
        RemoteLocalizationAlignment.MissingLocal =>
            $"Remoto {ShortChecksum(Status.Remote?.Checksum)}",
        RemoteLocalizationAlignment.MissingRemote =>
            $"Locale {ShortChecksum(Status.LocalChecksum)}",
        _ => Status.Diagnostic ?? "Checksum non disponibile",
    };

    public string VersionText
    {
        get
        {
            if (Status.Remote?.VersionId is not { Length: > 0 } versionId)
            {
                return "Nessuna versione pubblicata";
            }

            var publishedAt = Status.Remote.PublishedAt?.ToLocalTime().ToString("g");
            return string.IsNullOrWhiteSpace(publishedAt)
                ? $"Versione {versionId}"
                : $"Versione {versionId} · {publishedAt}";
        }
    }

    public string ConsoleNavigationToolTip =>
        $"Apri {Locale}/{File} in ZIAP Console";

    public bool CanCompare => Status.Alignment == RemoteLocalizationAlignment.Different;

    public bool CanSynchronize => Status.Alignment is
        RemoteLocalizationAlignment.Different or RemoteLocalizationAlignment.MissingLocal;

    public string SynchronizeLabel => Status.Alignment == RemoteLocalizationAlignment.MissingLocal
        ? "Scarica"
        : "Aggiorna dal pubblicato";

    private static string ShortChecksum(string? checksum)
    {
        var normalized = LocalizationChecksum.Normalize(checksum);
        if (normalized is null)
        {
            return "—";
        }

        return normalized.Length <= 12 ? normalized : $"{normalized[..12]}…";
    }
}
