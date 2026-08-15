using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZiapStudio.Core.Preflight;

namespace ZiapStudio.ViewModels;

public sealed class PreflightDocumentViewModel : INotifyPropertyChanged
{
    private readonly List<PreflightIssueViewModel> _allItems = [];
    private string _searchText = string.Empty;
    private int _filterIndex;
    private PreflightScanResult _result = PreflightScanResult.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PreflightIssueViewModel> VisibleItems { get; } = [];

    public ObservableCollection<PreflightIssueViewModel> RecentIssues { get; } = [];

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

    public int FilterIndex
    {
        get => _filterIndex;
        set
        {
            if (SetProperty(ref _filterIndex, value))
            {
                ApplyFilter();
            }
        }
    }

    public int ErrorCount => _result.ErrorCount;

    public int WarningCount => _result.WarningCount;

    public int IgnoredCount => _result.IgnoredCount;

    public int ObsoleteCount => _result.ObsoleteSuppressions.Count;

    public bool HasIssues => _result.ActiveIssues.Count > 0;

    public bool HasRecentIssues => RecentIssues.Count > 0;

    public bool HasObsoleteSuppressions => ObsoleteCount > 0;

    public string VisibleCountText => VisibleItems.Count == 1
        ? "1 risultato"
        : $"{VisibleItems.Count} risultati";

    public string LastScanText => _result.ScannedAt is DateTimeOffset scannedAt
        ? $"Ultima analisi: {scannedAt:HH:mm}"
        : "Analisi non ancora eseguita";

    public string SummaryText => _result.ScannedAt is null
        ? "Analisi in attesa."
        : ErrorCount == 0 && WarningCount == 0
            ? IgnoredCount == 0 ? "Nessun problema attivo." : "Nessun problema attivo; sono presenti eccezioni ignorate."
            : $"{CountLabel(ErrorCount, "errore", "errori")} · " +
                $"{CountLabel(WarningCount, "avviso", "avvisi")} · " +
                $"{CountLabel(IgnoredCount, "ignorato", "ignorati")}";

    public void Update(PreflightScanResult result)
    {
        _result = result;
        _allItems.Clear();
        _allItems.AddRange(result.ActiveIssues.Select(issue =>
            PreflightIssueViewModel.FromActive(issue)));
        _allItems.AddRange(result.IgnoredIssues.Select(ignored =>
            PreflightIssueViewModel.FromIgnored(ignored)));
        _allItems.AddRange(result.ObsoleteSuppressions.Select(
            PreflightIssueViewModel.FromObsolete));

        RecentIssues.Clear();
        foreach (var item in _allItems.Where(item => !item.IsIgnored && !item.IsObsolete).Take(3))
        {
            RecentIssues.Add(item);
        }

        ApplyFilter();
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(IgnoredCount));
        OnPropertyChanged(nameof(ObsoleteCount));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(HasRecentIssues));
        OnPropertyChanged(nameof(HasObsoleteSuppressions));
        OnPropertyChanged(nameof(LastScanText));
        OnPropertyChanged(nameof(SummaryText));
    }

    public void Reset()
    {
        SearchText = string.Empty;
        FilterIndex = 0;
        Update(PreflightScanResult.Empty);
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        var items = _allItems.Where(item => FilterIndex switch
        {
            1 => !item.IsIgnored && !item.IsObsolete && item.Severity == PreflightSeverity.Error,
            2 => !item.IsIgnored && !item.IsObsolete && item.Severity == PreflightSeverity.Warning,
            3 => item.IsIgnored,
            4 => item.IsObsolete,
            _ => !item.IsIgnored && !item.IsObsolete,
        });
        if (search.Length > 0)
        {
            items = items.Where(item =>
                item.RecordName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.RuleId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.RecordId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        VisibleItems.Clear();
        foreach (var item in items)
        {
            VisibleItems.Add(item);
        }

        OnPropertyChanged(nameof(VisibleCountText));
    }

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

    private static string CountLabel(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record PreflightIssueViewModel
{
    public required string RuleId { get; init; }
    public required string Scope { get; init; }
    public required int RecordId { get; init; }
    public required string RecordName { get; init; }
    public required PreflightSeverity Severity { get; init; }
    public required string Message { get; init; }
    public required PreflightIssueIdentity Identity { get; init; }
    public Uri? NavigationTarget { get; init; }
    public bool IsIgnored { get; init; }
    public bool IsObsolete { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset? IgnoredAt { get; init; }

    public string SeverityText => IsObsolete
        ? "Obsoleta"
        : Severity == PreflightSeverity.Error ? "Errore" : "Avviso";

    public string Glyph => IsObsolete
        ? "○"
        : Severity == PreflightSeverity.Error ? "●" : "▲";

    public string RecordText => $"{Scope} #{RecordId} · {RecordName}";

    public string SuppressionText => IsObsolete
        ? $"Eccezione senza problema corrispondente · {RuleId}"
        : IsIgnored
            ? string.IsNullOrWhiteSpace(Reason)
                ? $"Ignorato {IgnoredAt:dd/MM/yyyy HH:mm}"
                : $"Ignorato: {Reason}"
            : RuleId;

    public bool CanOpen => NavigationTarget is not null && !IsObsolete;
    public bool CanIgnore => !IsIgnored && !IsObsolete;
    public bool CanRestore => IsIgnored || IsObsolete;

    public static PreflightIssueViewModel FromActive(PreflightIssue issue) => FromIssue(issue);

    public static PreflightIssueViewModel FromIgnored(SuppressedPreflightIssue ignored) =>
        FromIssue(ignored.Issue) with
        {
            IsIgnored = true,
            Reason = ignored.Suppression.Reason,
            IgnoredAt = ignored.Suppression.IgnoredAt,
        };

    public static PreflightIssueViewModel FromObsolete(PreflightSuppression suppression) => new()
    {
        RuleId = suppression.RuleId,
        Scope = suppression.Scope,
        RecordId = suppression.RecordId,
        RecordName = "Record non più segnalato",
        Severity = PreflightSeverity.Warning,
        Message = "Questa eccezione non corrisponde più a un problema rilevato.",
        Identity = suppression.Identity,
        IsObsolete = true,
        Reason = suppression.Reason,
        IgnoredAt = suppression.IgnoredAt,
    };

    private static PreflightIssueViewModel FromIssue(PreflightIssue issue) => new()
    {
        RuleId = issue.RuleId,
        Scope = issue.Scope,
        RecordId = issue.RecordId,
        RecordName = issue.RecordName,
        Severity = issue.Severity,
        Message = issue.Message,
        Identity = issue.Identity,
        NavigationTarget = issue.NavigationTarget,
    };
}
