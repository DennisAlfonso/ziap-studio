using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ZiapStudio.Core.Assets;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Weapons;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Preflight;

namespace ZiapStudio.ViewModels;

public sealed class RpgMakerDatabaseDocumentViewModel : INotifyPropertyChanged
{
    private readonly RpgMakerDatabaseDefinition _definition;
    private readonly IReadOnlyList<RpgMakerDatabaseRowViewModel> _allEntries;
    private readonly IReadOnlyDictionary<AssetPreviewKey, AssetPreviewResult> _assetPreviews;
    private readonly DocumentEditSession _editSession;
    private readonly WeaponNotetagCatalog? _documentWeaponCatalog;
    private string _searchText = string.Empty;
    private RpgMakerDatabaseRowViewModel? _selectedEntry;
    private IReadOnlyList<PreflightIssue> _preflightIssues = [];

    public RpgMakerDatabaseDocumentViewModel(
        RpgMakerDatabaseDocument document,
        IReadOnlyDictionary<AssetPreviewKey, AssetPreviewResult>? assetPreviews,
        DocumentEditSession editSession)
    {
        _definition = document.Definition;
        _assetPreviews = assetPreviews ??
            new Dictionary<AssetPreviewKey, AssetPreviewResult>();
        _editSession = editSession;
        _documentWeaponCatalog = document.WeaponNotetagCatalog;
        _editSession.PropertyChanged += EditSession_PropertyChanged;
        DisplayName = document.Descriptor.DisplayName;
        SourcePath = document.SourcePath;
        Columns = document.Definition.Columns;
        _allEntries = document.Entries
            .Select(entry => new RpgMakerDatabaseRowViewModel(entry, Columns))
            .ToArray();
        ApplyFilter();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; }

    public string SourcePath { get; }

    public IReadOnlyList<RpgMakerDatabaseColumnDefinition> Columns { get; }

    public ObservableCollection<RpgMakerDatabaseRowViewModel> FilteredEntries { get; } = [];

    public ObservableCollection<RpgMakerDatabaseInspectorSectionViewModel> SelectedSections { get; } = [];

    public WeaponAdvancedEditorViewModel? AdvancedEditor { get; private set; }

    public bool HasAdvancedEditor => AdvancedEditor is not null;

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

    public RpgMakerDatabaseRowViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                RebuildInspector();
                OnPropertyChanged(nameof(SelectedEntryTitle));
            }
        }
    }

    public string ItemCountText => _allEntries.Count == 1
        ? "1 elemento"
        : $"{_allEntries.Count} elementi";

    public string FilteredCountText => FilteredEntries.Count == _allEntries.Count
        ? ItemCountText
        : $"{FilteredEntries.Count} di {_allEntries.Count} elementi";

    public string ChangeCountText => _editSession.ChangeSet.Count switch
    {
        0 => string.Empty,
        1 => "1 modifica non salvata",
        var count => $"{count} modifiche non salvate",
    };

    public string SelectedEntryTitle
    {
        get
        {
            if (SelectedEntry is null)
            {
                return "Nessun elemento selezionato";
            }

            var displayName = SelectedEntry.Entry.GetValue("name").DisplayValue;
            var nameSuffix = string.IsNullOrWhiteSpace(displayName)
                ? string.Empty
                : $" — {displayName}";
            return $"{_definition.ItemDisplayName} #{SelectedEntry.Entry.Id}{nameSuffix}";
        }
    }

    public void SelectEntryById(int id)
    {
        SearchText = string.Empty;
        var entry = _allEntries.FirstOrDefault(candidate => candidate.Entry.Id == id);
        if (entry is not null)
        {
            SelectedEntry = entry;
        }
    }

    public void ApplyPreflightIssues(IEnumerable<PreflightIssue> issues)
    {
        _preflightIssues = _definition.ResourceName.Equals("weapons", StringComparison.OrdinalIgnoreCase)
            ? issues.Where(issue => issue.Scope.Equals("Weapons", StringComparison.OrdinalIgnoreCase)).ToArray()
            : [];
        foreach (var row in _allEntries)
        {
            row.ApplyPreflightIssues(_preflightIssues.Where(issue => issue.RecordId == row.Entry.Id));
        }

        AdvancedEditor?.ApplyPreflightIssues(_preflightIssues.Where(issue =>
            issue.RecordId == SelectedEntry?.Entry.Id));
    }

    private void ApplyFilter()
    {
        var previousSelection = SelectedEntry;
        var search = SearchText.Trim();
        var entries = string.IsNullOrEmpty(search)
            ? _allEntries
            : _allEntries
                .Where(entry =>
                    entry.Entry.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    entry.Entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    entry.Entry.GetValue("name").DisplayValue.Contains(
                        search,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        FilteredEntries.Clear();
        foreach (var entry in entries)
        {
            FilteredEntries.Add(entry);
        }

        SelectedEntry = previousSelection is not null && FilteredEntries.Contains(previousSelection)
            ? previousSelection
            : FilteredEntries.FirstOrDefault();
        OnPropertyChanged(nameof(FilteredCountText));
    }

    private void RebuildInspector()
    {
        SelectedSections.Clear();
        AdvancedEditor = null;
        if (SelectedEntry is null)
        {
            OnPropertyChanged(nameof(AdvancedEditor));
            OnPropertyChanged(nameof(HasAdvancedEditor));
            return;
        }

        foreach (var section in _definition.Sections)
        {
            if (_definition.ResourceName.Equals("weapons", StringComparison.OrdinalIgnoreCase) &&
                section.DisplayName.Equals("Note", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SelectedSections.Add(new RpgMakerDatabaseInspectorSectionViewModel(
                section.DisplayName,
                section.Fields
                    .Select(field => new RpgMakerDatabaseInspectorFieldViewModel(
                        field,
                        SelectedEntry.Entry.Id,
                        SelectedEntry.Entry.GetValue(field.Key),
                        _editSession,
                        _assetPreviews.GetValueOrDefault(
                            new AssetPreviewKey(SelectedEntry.Entry.Id, field.Key))))
                    .ToArray()));
        }

        if (_definition.ResourceName.Equals("weapons", StringComparison.OrdinalIgnoreCase))
        {
            AdvancedEditor = new WeaponAdvancedEditorViewModel(
                SelectedEntry.Entry.Id,
                _editSession,
                _documentWeaponCatalog);
            AdvancedEditor.ApplyPreflightIssues(_preflightIssues.Where(issue =>
                issue.RecordId == SelectedEntry.Entry.Id));
        }

        OnPropertyChanged(nameof(AdvancedEditor));
        OnPropertyChanged(nameof(HasAdvancedEditor));
    }

    private void EditSession_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(DocumentEditSession.ChangeSet))
        {
            return;
        }

        foreach (var row in _allEntries)
        {
            row.RefreshFromSession(_editSession, Columns);
        }

        foreach (var field in SelectedSections.SelectMany(section => section.Fields))
        {
            field.RefreshFromSession();
        }

        AdvancedEditor?.RefreshFromSession();

        OnPropertyChanged(nameof(ChangeCountText));
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

}

public sealed class RpgMakerDatabaseRowViewModel : INotifyPropertyChanged
{
    private int _preflightIssueCount;
    private bool _hasPreflightErrors;

    public RpgMakerDatabaseRowViewModel(
        RpgMakerDatabaseEntry entry,
        IReadOnlyList<RpgMakerDatabaseColumnDefinition> columns)
    {
        Entry = entry;
        Cells = columns
            .Select(column => new RpgMakerDatabaseCellViewModel(
                ValueOrDash(entry.GetValue(column.Key).DisplayValue),
                column.Width))
            .ToArray();
    }

    public RpgMakerDatabaseEntry Entry { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<RpgMakerDatabaseCellViewModel> Cells { get; }

    public int PreflightIssueCount => _preflightIssueCount;

    public bool HasPreflightIssues => PreflightIssueCount > 0;

    public string PreflightMarker => !HasPreflightIssues
        ? string.Empty
        : _hasPreflightErrors ? $"● {PreflightIssueCount}" : $"▲ {PreflightIssueCount}";

    public string PreflightToolTip => !HasPreflightIssues
        ? string.Empty
        : $"{PreflightIssueCount} problemi Pre-Flight";

    public void ApplyPreflightIssues(IEnumerable<PreflightIssue> issues)
    {
        var current = issues.ToArray();
        _preflightIssueCount = current.Length;
        _hasPreflightErrors = current.Any(issue => issue.Severity == PreflightSeverity.Error);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreflightIssueCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreflightIssues)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreflightMarker)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreflightToolTip)));
    }

    public void RefreshFromSession(
        DocumentEditSession session,
        IReadOnlyList<RpgMakerDatabaseColumnDefinition> columns)
    {
        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            if (!session.TryGetValue(Entry.Id, column.Key, out var node))
            {
                continue;
            }
            var rawValue = node?.ToString() ?? string.Empty;
            var semanticValue = Entry.GetValue(column.Key);
            if (string.Equals(rawValue, semanticValue.RawValue, StringComparison.Ordinal))
            {
                Cells[index].Value = ValueOrDash(semanticValue.DisplayValue);
                continue;
            }

            var option = semanticValue.EditorOptions.FirstOrDefault(candidate =>
                int.TryParse(rawValue, out var id) && candidate.Value == id);
            Cells[index].Value = ValueOrDash(option?.DisplayValue ?? rawValue);
        }
    }

    private static string ValueOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}

public sealed class RpgMakerDatabaseCellViewModel : INotifyPropertyChanged
{
    private string _value;

    public RpgMakerDatabaseCellViewModel(string value, double width)
    {
        _value = value;
        Width = width;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Value
    {
        get => _value;
        set
        {
            if (!string.Equals(_value, value, StringComparison.Ordinal))
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }

    public double Width { get; }
}

public sealed record RpgMakerDatabaseInspectorSectionViewModel(
    string DisplayName,
    IReadOnlyList<RpgMakerDatabaseInspectorFieldViewModel> Fields);

public sealed class RpgMakerDatabaseInspectorFieldViewModel : INotifyPropertyChanged
{
    private readonly RpgMakerDatabaseFieldDefinition _definition;
    private readonly int _entryId;
    private readonly DocumentEditSession _editSession;
    private readonly AssetPreviewResult? _assetResult;
    private double _numericValue;
    private RpgMakerReferenceOption? _selectedReferenceOption;
    private AssetPreviewViewModel? _assetPreview;
    private int? _currentPreviewIconIndex;

    public RpgMakerDatabaseInspectorFieldViewModel(
        RpgMakerDatabaseFieldDefinition definition,
        int entryId,
        RpgMakerResolvedValue value,
        DocumentEditSession editSession,
        AssetPreviewResult? assetResult = null)
    {
        _definition = definition;
        _entryId = entryId;
        _editSession = editSession;
        _assetResult = assetResult;
        _currentPreviewIconIndex = assetResult?.Reference.Index;
        DisplayName = definition.DisplayName;
        DisplayValue = ValueOrDash(value.DisplayValue);
        SecondaryText = definition.EditorKind == RpgMakerEditorKind.ReferenceComboBox
            ? string.Empty
            : CreateSecondaryText(value, assetResult);
        NavigationTarget = value.Kind == RpgMakerResolvedValueKind.DatabaseReference &&
            value.Status == RpgMakerResolutionStatus.Resolved
                ? value.Target
                : null;
        LocalizationOrigin = value.LocalizationOrigin;
        _assetPreview = assetResult?.Preview is not null
            ? new AssetPreviewViewModel(assetResult)
            : null;
        AssetDiagnostic = AssetPreview?.LoadDiagnostic ?? CreateAssetDiagnostic(assetResult);
        EditorOptions = value.EditorOptions;
        RefreshFromSession();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; }

    public string EditorAutomationName => $"Modifica {DisplayName}";

    public string DisplayValue { get; }

    public string SecondaryText { get; }

    public bool HasSecondaryText => !string.IsNullOrEmpty(SecondaryText);

    public string? NavigationTarget { get; }

    public LocalizationReferenceOrigin? LocalizationOrigin { get; }

    public bool IsConsoleManaged => LocalizationOrigin is not null;

    public string ConsoleNavigationAutomationName =>
        $"Apri {DisplayName} in ZIAP Console";

    public string ConsoleNavigationToolTip => LocalizationOrigin is null
        ? string.Empty
        : $"{LocalizationOrigin.Locale}/{LocalizationOrigin.SourceFile} · {LocalizationOrigin.Path}";

    public bool CanNavigate => IsReadOnly && !string.IsNullOrWhiteSpace(NavigationTarget);

    public bool CannotNavigate => IsReadOnly && !CanNavigate;

    public bool IsReadOnly => _definition.EditorKind == RpgMakerEditorKind.ReadOnly;

    public bool IsNumberEditor => _definition.EditorKind == RpgMakerEditorKind.Number;

    public bool IsReferenceEditor =>
        _definition.EditorKind == RpgMakerEditorKind.ReferenceComboBox;

    public double Minimum => _definition.Minimum ?? int.MinValue;

    public double Maximum
    {
        get
        {
            if (_assetResult?.Reference.AssetKind == RpgMakerAssetKind.Icon &&
                _assetResult.Preview is { } preview)
            {
                return preview.SourceWidth / 32 * (preview.SourceHeight / 32) - 1;
            }

            return _definition.Maximum ?? int.MaxValue;
        }
    }

    public double NumericValue
    {
        get => _numericValue;
        set
        {
            if (!IsNumberEditor || double.IsNaN(value) || double.IsInfinity(value) ||
                value < Minimum || value > Maximum || value != Math.Truncate(value))
            {
                OnPropertyChanged();
                return;
            }

            var integerValue = checked((int)value);
            if (_numericValue.Equals(value))
            {
                return;
            }

            _numericValue = value;
            _editSession.SetValue(_entryId, _definition.Key, JsonValue.Create(integerValue));
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<RpgMakerReferenceOption> EditorOptions { get; }

    public RpgMakerReferenceOption? SelectedReferenceOption
    {
        get => _selectedReferenceOption;
        set
        {
            if (!IsReferenceEditor || value is null ||
                _selectedReferenceOption?.Value == value.Value)
            {
                return;
            }

            _selectedReferenceOption = value;
            _editSession.SetValue(_entryId, _definition.Key, JsonValue.Create(value.Value));
            OnPropertyChanged();
        }
    }

    public AssetPreviewViewModel? AssetPreview
    {
        get => _assetPreview;
        private set
        {
            _assetPreview = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAssetPreview));
        }
    }

    public bool HasAssetPreview => AssetPreview?.ImageSource is not null;

    public string AssetDiagnostic { get; }

    public bool HasAssetDiagnostic => !string.IsNullOrWhiteSpace(AssetDiagnostic);

    public void RefreshFromSession()
    {
        if (!_editSession.TryGetValue(_entryId, _definition.Key, out var node))
        {
            return;
        }
        if (IsNumberEditor && TryGetInteger(node, out var numericValue))
        {
            _numericValue = numericValue;
            OnPropertyChanged(nameof(NumericValue));
            RefreshIconPreview(numericValue);
        }

        if (IsReferenceEditor && TryGetInteger(node, out var referenceValue))
        {
            _selectedReferenceOption = EditorOptions.FirstOrDefault(option =>
                option.Value == referenceValue);
            OnPropertyChanged(nameof(SelectedReferenceOption));
        }
    }

    private void RefreshIconPreview(int iconIndex)
    {
        if (_assetResult?.Reference.AssetKind != RpgMakerAssetKind.Icon ||
            _assetResult.Preview is not { } originalPreview)
        {
            return;
        }

        if (_currentPreviewIconIndex == iconIndex)
        {
            return;
        }

        const int iconSize = 32;
        var columns = originalPreview.SourceWidth / iconSize;
        var cropX = columns > 0 ? iconIndex % columns * iconSize : -1;
        var cropY = columns > 0 ? iconIndex / columns * iconSize : -1;
        if (cropX < 0 || cropY < 0 ||
            cropX + iconSize > originalPreview.SourceWidth ||
            cropY + iconSize > originalPreview.SourceHeight)
        {
            AssetPreview = null;
            _currentPreviewIconIndex = iconIndex;
            return;
        }

        AssetPreview = new AssetPreviewViewModel(new AssetPreviewResult
        {
            Reference = _assetResult.Reference with { Index = iconIndex },
            Preview = originalPreview with
            {
                CropX = cropX,
                CropY = cropY,
                CropWidth = iconSize,
                CropHeight = iconSize,
            },
        });
        _currentPreviewIconIndex = iconIndex;
    }

    private static bool TryGetInteger(JsonNode? node, out int value)
    {
        value = 0;
        return node is JsonValue jsonValue && jsonValue.TryGetValue(out value);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string CreateSecondaryText(
        RpgMakerResolvedValue value,
        AssetPreviewResult? assetResult)
    {
        if (value.Kind == RpgMakerResolvedValueKind.AssetReference &&
            assetResult?.Reference is { } assetReference)
        {
            if (assetReference.Status == AssetResolutionStatus.Empty)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(assetReference.RelativePath))
            {
                return $"Percorso: {assetReference.RelativePath}";
            }
        }

        if (value.Kind == RpgMakerResolvedValueKind.LocalizationReference)
        {
            return value.Status == RpgMakerResolutionStatus.Resolved
                ? $"Chiave: {ValueOrDash(value.RawValue)}"
                : $"Localizzazione mancante · chiave: {ValueOrDash(value.RawValue)}";
        }

        if (value.Status == RpgMakerResolutionStatus.MissingTarget)
        {
            return $"Destinazione mancante · valore raw: {ValueOrDash(value.RawValue)}";
        }

        if (value.IsResolved)
        {
            return $"Valore raw: {ValueOrDash(value.RawValue)}";
        }

        if (value.Kind == RpgMakerResolvedValueKind.AssetReference &&
            !string.IsNullOrWhiteSpace(value.RawValue))
        {
            return "Riferimento asset · valore raw preservato";
        }

        if (value.Kind is RpgMakerResolvedValueKind.DatabaseReference or
            RpgMakerResolvedValueKind.SystemReference &&
            !string.IsNullOrWhiteSpace(value.RawValue))
        {
            return "Riferimento non risolto · valore raw preservato";
        }

        if (value.Kind == RpgMakerResolvedValueKind.Text &&
            !string.Equals(value.RawValue, value.DisplayValue, StringComparison.Ordinal))
        {
            return $"Valore raw: {value.RawValue}";
        }

        return string.Empty;
    }

    private static string CreateAssetDiagnostic(AssetPreviewResult? assetResult)
    {
        if (assetResult is null || assetResult.Reference.Status == AssetResolutionStatus.Empty)
        {
            return string.Empty;
        }

        if (assetResult.Reference.Status is AssetResolutionStatus.Missing or
            AssetResolutionStatus.Invalid)
        {
            return assetResult.Reference.Diagnostic ?? "L'asset non può essere risolto.";
        }

        return assetResult.Preview is null
            ? "Il file esiste, ma l'anteprima PNG non è disponibile."
            : string.Empty;
    }

    private static string ValueOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
