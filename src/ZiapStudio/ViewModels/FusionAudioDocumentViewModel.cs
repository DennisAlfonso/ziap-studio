using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Audio;

namespace ZiapStudio.ViewModels;

public sealed class FusionAudioDocumentViewModel : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions SignatureOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, double> _categoryVolumes;
    private readonly Dictionary<string, Dictionary<string, JsonElement>?> _categoryExtensions;
    private readonly Dictionary<string, JsonElement>? _catalogExtensionData;
    private string _baselineSignature;
    private string _searchText = string.Empty;
    private FusionAudioEntryViewModel? _selectedEntry;
    private FusionAudioPlaybackPlan? _lastPlayback;
    private double _masterVolume;
    private IReadOnlyList<FusionAudioDiagnostic> _diagnostics;

    public FusionAudioDocumentViewModel(FusionAudioDocument document)
    {
        SourcePath = document.SourcePath;
        PluginIsActive = document.PluginIsActive;
        SourceSnapshot = document.SourceSnapshot;
        _masterVolume = document.Catalog.MasterVolume;
        _catalogExtensionData = document.Catalog.ExtensionData;
        _categoryVolumes = document.Catalog.Categories.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Volume,
            StringComparer.OrdinalIgnoreCase);
        _categoryExtensions = document.Catalog.Categories.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ExtensionData,
            StringComparer.OrdinalIgnoreCase);
        _diagnostics = document.Diagnostics;
        AssetCountText = $"{document.AssetCount} asset";
        AvailableAudioFiles = document.AvailableFiles
            .Select(option => new FusionAudioFileOptionViewModel(option))
            .OrderBy(option => option.CatalogPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var diagnostic in document.Diagnostics)
        {
            Diagnostics.Add(new FusionAudioDiagnosticViewModel(diagnostic));
        }
        foreach (var (eventId, entry) in document.Catalog.Sounds
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddEntry(new FusionAudioEntryViewModel(eventId, entry));
        }
        RebuildVisibleEntries();
        SelectedEntry = Entries.FirstOrDefault();
        _baselineSignature = CreateSignature();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourcePath { get; }

    public bool PluginIsActive { get; }

    public string PluginStatusText => PluginIsActive ? "● Plugin attivo" : "○ Plugin non attivo";

    public string CatalogStatusText => HasErrors ? "Catalogo non valido" : "Catalogo valido";

    public string EventCountText => $"{Entries.Count} eventi";

    public string AssetCountText { get; private set; } = "— asset";

    public ObservableCollection<FusionAudioEntryViewModel> Entries { get; } = [];

    public ObservableCollection<FusionAudioEntryViewModel> VisibleEntries { get; } = [];

    public ObservableCollection<FusionAudioDiagnosticViewModel> Diagnostics { get; } = [];

    public IReadOnlyList<FusionAudioFileOptionViewModel> AvailableAudioFiles { get; }

    public string AudioFileSelectorPlaceholder => AvailableAudioFiles.Count == 0
        ? "Nessun asset in audio/se/**"
        : $"Cerca tra {AvailableAudioFiles.Count} asset in audio/se/**…";

    public DocumentSourceSnapshot? SourceSnapshot { get; private set; }

    public double MasterVolume
    {
        get => _masterVolume;
        set
        {
            var normalized = Math.Clamp(value, 0, 1);
            if (Math.Abs(_masterVolume - normalized) < 0.0001) return;
            _masterVolume = normalized;
            OnPropertyChanged();
            NotifyDirtyChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty)) return;
            RebuildVisibleEntries();
        }
    }

    public FusionAudioEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value)) return;
            OnPropertyChanged(nameof(HasSelectedEntry));
            OnPropertyChanged(nameof(SelectedCategoryVolume));
            NotifyLastPlaybackChanged();
        }
    }

    public bool HasSelectedEntry => SelectedEntry is not null;

    public bool HasLastPlayback => _lastPlayback is not null &&
        _lastPlayback.EventId.Equals(SelectedEntry?.EventId, StringComparison.OrdinalIgnoreCase);

    public string LastPlaybackFileText => HasLastPlayback
        ? Path.GetFileName(_lastPlayback!.RelativePath)
        : string.Empty;

    public string LastPlaybackSettingsText => HasLastPlayback
        ? $"Volume effettivo {_lastPlayback!.Volume:P0}  ·  " +
          $"Pitch {_lastPlayback.Pitch:0.#}  ·  Pan {_lastPlayback.Pan * 100:0}"
        : string.Empty;

    public double SelectedCategoryVolume
    {
        get
        {
            var category = SelectedEntry?.Category;
            return category is not null && _categoryVolumes.TryGetValue(category, out var volume)
                ? volume
                : 1;
        }
        set
        {
            var category = SelectedEntry?.Category?.Trim();
            if (string.IsNullOrWhiteSpace(category)) return;
            var normalized = Math.Clamp(value, 0, 1);
            if (_categoryVolumes.TryGetValue(category, out var current) &&
                Math.Abs(current - normalized) < 0.0001)
            {
                return;
            }
            _categoryVolumes[category] = normalized;
            OnPropertyChanged();
            NotifyDirtyChanged();
        }
    }

    public bool IsDirty => !string.Equals(
        _baselineSignature,
        CreateSignature(),
        StringComparison.Ordinal);

    public bool HasErrors => _diagnostics.Any(diagnostic =>
        diagnostic.Severity == FusionAudioDiagnosticSeverity.Error);

    public string? IdentityError
    {
        get
        {
            if (Entries.Any(entry => string.IsNullOrWhiteSpace(entry.EventId)))
            {
                return "Ogni evento deve avere un identificatore.";
            }
            var duplicate = Entries.GroupBy(
                    entry => entry.EventId.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            return duplicate is null
                ? null
                : $"L'identificatore '{duplicate.Key}' è duplicato.";
        }
    }

    public FusionAudioCatalog BuildCatalog()
    {
        var categories = new Dictionary<string, FusionAudioCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, volume) in _categoryVolumes)
        {
            categories[category] = new FusionAudioCategory
            {
                Volume = volume,
                ExtensionData = _categoryExtensions.GetValueOrDefault(category),
            };
        }
        foreach (var category in Entries.Select(entry => entry.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            categories.TryAdd(category, new FusionAudioCategory());
        }

        var sounds = new Dictionary<string, FusionAudioEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.EventId))
            {
                sounds[entry.EventId.Trim()] = entry.ToModel();
            }
        }
        return new FusionAudioCatalog
        {
            SchemaVersion = 1,
            MasterVolume = MasterVolume,
            Categories = categories
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            Sounds = sounds
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            ExtensionData = _catalogExtensionData,
        };
    }

    public FusionAudioEntryViewModel AddEvent()
    {
        var suffix = 1;
        var eventId = "new.event";
        while (Entries.Any(entry => entry.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase)))
        {
            eventId = $"new.event{++suffix}";
        }
        var entry = new FusionAudioEntryViewModel(eventId, new FusionAudioEntry
        {
            Category = "new",
            Source = new FusionAudioSource { Kind = FusionAudioSourceKinds.Files, Files = [""] },
            Volume = 90,
        });
        AddEntry(entry);
        RebuildVisibleEntries();
        SelectedEntry = entry;
        NotifyCollectionChanged();
        return entry;
    }

    public bool RemoveSelectedEvent()
    {
        var entry = SelectedEntry;
        if (entry is null) return false;
        entry.PropertyChanged -= Entry_PropertyChanged;
        var index = Entries.IndexOf(entry);
        Entries.Remove(entry);
        RebuildVisibleEntries();
        SelectedEntry = Entries.Count == 0
            ? null
            : Entries[Math.Clamp(index, 0, Entries.Count - 1)];
        NotifyCollectionChanged();
        return true;
    }

    public void UpdateAnalysis(
        IReadOnlyList<FusionAudioResolvedAsset> assets,
        IReadOnlyList<FusionAudioDiagnostic> diagnostics)
    {
        _diagnostics = diagnostics;
        Diagnostics.Clear();
        foreach (var diagnostic in diagnostics)
        {
            Diagnostics.Add(new FusionAudioDiagnosticViewModel(diagnostic));
        }
        AssetCountText = $"{assets.Where(asset => asset.Exists).Select(asset => asset.ResolvedPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count()} asset";
        OnPropertyChanged(nameof(AssetCountText));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(CatalogStatusText));
    }

    public void AcceptSaved(DocumentSourceSnapshot snapshot)
    {
        SourceSnapshot = snapshot;
        _baselineSignature = CreateSignature();
        OnPropertyChanged(nameof(SourceSnapshot));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void SelectEvent(string eventId)
    {
        var match = Entries.FirstOrDefault(entry =>
            entry.EventId.Equals(eventId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            SelectedEntry = match;
        }
    }

    public void RefreshVisibleEntries() => RebuildVisibleEntries();

    public void RecordPlayback(FusionAudioPlaybackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _lastPlayback = plan;
        NotifyLastPlaybackChanged();
    }

    public IReadOnlyList<FusionAudioFileOptionViewModel> SearchAudioFiles(
        string? query,
        int maximumResults = int.MaxValue)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        var terms = normalizedQuery.Split(
            [' ', '\\', '/'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return AvailableAudioFiles
            .Where(option => terms.All(term => option.SearchText.Contains(
                term,
                StringComparison.OrdinalIgnoreCase)))
            .OrderBy(option => option.GetSearchRank(normalizedQuery))
            .ThenBy(option => option.CatalogPath, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maximumResults))
            .ToArray();
    }

    public FusionAudioFileOptionViewModel? FindAudioFile(string? catalogPath)
    {
        var normalizedPath = catalogPath?.Trim().Replace('\\', '/');
        var extension = Path.GetExtension(normalizedPath);
        if (extension is not null &&
            (extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)))
        {
            normalizedPath = Path.ChangeExtension(normalizedPath, null)?.Replace('\\', '/');
        }
        return string.IsNullOrWhiteSpace(normalizedPath)
            ? null
            : AvailableAudioFiles.FirstOrDefault(option => option.CatalogPath.Equals(
                normalizedPath,
                StringComparison.OrdinalIgnoreCase));
    }

    private void AddEntry(FusionAudioEntryViewModel entry)
    {
        entry.PropertyChanged += Entry_PropertyChanged;
        Entries.Add(entry);
    }

    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(FusionAudioEntryViewModel.EventId) or
            nameof(FusionAudioEntryViewModel.Category))
        {
            OnPropertyChanged(nameof(SelectedCategoryVolume));
            NotifyLastPlaybackChanged();
        }
        NotifyDirtyChanged();
    }

    private void RebuildVisibleEntries()
    {
        var visibleEntries = Entries
            .Where(entry => string.IsNullOrWhiteSpace(SearchText) ||
                entry.EventId.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                entry.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.EventId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var targetIndex = 0; targetIndex < visibleEntries.Length; targetIndex++)
        {
            var entry = visibleEntries[targetIndex];
            if (targetIndex < VisibleEntries.Count &&
                ReferenceEquals(VisibleEntries[targetIndex], entry))
            {
                continue;
            }

            var currentIndex = VisibleEntries.IndexOf(entry);
            if (currentIndex >= 0)
            {
                VisibleEntries.Move(currentIndex, targetIndex);
            }
            else
            {
                VisibleEntries.Insert(targetIndex, entry);
            }
        }

        while (VisibleEntries.Count > visibleEntries.Length)
        {
            VisibleEntries.RemoveAt(VisibleEntries.Count - 1);
        }
    }

    private string CreateSignature() => JsonSerializer.Serialize(BuildCatalog(), SignatureOptions);

    private void NotifyCollectionChanged()
    {
        OnPropertyChanged(nameof(EventCountText));
        NotifyDirtyChanged();
    }

    private void NotifyDirtyChanged() => OnPropertyChanged(nameof(IsDirty));

    private void NotifyLastPlaybackChanged()
    {
        OnPropertyChanged(nameof(HasLastPlayback));
        OnPropertyChanged(nameof(LastPlaybackFileText));
        OnPropertyChanged(nameof(LastPlaybackSettingsText));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class FusionAudioEntryViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<string, JsonElement>? _extensionData;
    private readonly Dictionary<string, JsonElement>? _sourceExtensionData;
    private readonly Dictionary<string, JsonElement>? _pitchExtensionData;
    private string _eventId;
    private string _category;
    private string _sourceKind;
    private string? _systemSlot;
    private double _volume;
    private double _pitchMin;
    private double _pitchMax;
    private double _pan;
    private double _cooldown;
    private bool _antiRepeat;

    public FusionAudioEntryViewModel(string eventId, FusionAudioEntry entry)
    {
        _extensionData = entry.ExtensionData;
        _sourceExtensionData = entry.Source.ExtensionData;
        _pitchExtensionData = entry.Pitch.ExtensionData;
        _eventId = eventId;
        _category = string.IsNullOrWhiteSpace(entry.Category)
            ? InferCategory(eventId)
            : entry.Category;
        _sourceKind = entry.Source.Kind;
        _systemSlot = entry.Source.Slot;
        _volume = entry.Volume;
        _pitchMin = entry.Pitch.Min;
        _pitchMax = entry.Pitch.Max;
        _pan = entry.Pan;
        _cooldown = entry.Cooldown;
        _antiRepeat = entry.AntiRepeat;
        foreach (var file in entry.Source.Files)
        {
            AddVariant(new FusionAudioVariantViewModel(file));
        }
        Variants.CollectionChanged += Variants_CollectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> SourceKindOptions { get; } =
        [FusionAudioSourceKinds.Files, FusionAudioSourceKinds.SystemSound];

    public IReadOnlyList<string> SystemSlotOptions => FusionAudioSystemSounds.Slots;

    public ObservableCollection<FusionAudioVariantViewModel> Variants { get; } = [];

    public string EventId { get => _eventId; set => Set(ref _eventId, value ?? string.Empty); }

    public string Category { get => _category; set => Set(ref _category, value ?? string.Empty); }

    public string SourceKind
    {
        get => _sourceKind;
        set
        {
            if (!Set(ref _sourceKind, value ?? FusionAudioSourceKinds.Files)) return;
            OnPropertyChanged(nameof(SourceKindIndex));
            OnPropertyChanged(nameof(IsFileSource));
            OnPropertyChanged(nameof(IsSystemSoundSource));
            OnPropertyChanged(nameof(SourceSummary));
        }
    }

    public int SourceKindIndex
    {
        get => IsSystemSoundSource ? 1 : 0;
        set
        {
            if (value < 0 || value >= SourceKindOptions.Count) return;
            SourceKind = SourceKindOptions[value];
        }
    }

    public string? SystemSlot
    {
        get => _systemSlot;
        set
        {
            if (Set(ref _systemSlot, value))
            {
                OnPropertyChanged(nameof(SystemSlotIndex));
                OnPropertyChanged(nameof(SourceSummary));
            }
        }
    }

    public int SystemSlotIndex
    {
        get
        {
            for (var index = 0; index < SystemSlotOptions.Count; index++)
            {
                if (SystemSlotOptions[index].Equals(SystemSlot, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
            return -1;
        }
        set
        {
            if (value < 0 || value >= SystemSlotOptions.Count) return;
            SystemSlot = SystemSlotOptions[value];
        }
    }

    public double Volume { get => _volume; set => Set(ref _volume, Math.Clamp(value, 0, 100)); }

    public double PitchMin { get => _pitchMin; set => Set(ref _pitchMin, value); }

    public double PitchMax { get => _pitchMax; set => Set(ref _pitchMax, value); }

    public double Pan { get => _pan; set => Set(ref _pan, Math.Clamp(value, -100, 100)); }

    public double Cooldown { get => _cooldown; set => Set(ref _cooldown, Math.Max(0, value)); }

    public bool AntiRepeat { get => _antiRepeat; set => Set(ref _antiRepeat, value); }

    public bool IsFileSource => SourceKind.Equals(
        FusionAudioSourceKinds.Files,
        StringComparison.OrdinalIgnoreCase);

    public bool IsSystemSoundSource => SourceKind.Equals(
        FusionAudioSourceKinds.SystemSound,
        StringComparison.OrdinalIgnoreCase);

    public string SourceSummary => IsSystemSoundSource
        ? $"System.json → {SystemSlot ?? "—"}"
        : Variants.FirstOrDefault()?.File ?? "Nessun asset";

    public FusionAudioEntry ToModel() => new()
    {
        Category = Category.Trim(),
        Source = IsSystemSoundSource
            ? new FusionAudioSource
            {
                Kind = FusionAudioSourceKinds.SystemSound,
                Slot = SystemSlot,
                ExtensionData = _sourceExtensionData,
            }
            : new FusionAudioSource
            {
                Kind = FusionAudioSourceKinds.Files,
                Files = Variants.Select(variant => variant.File.Trim()).ToArray(),
                ExtensionData = _sourceExtensionData,
            },
        Volume = Volume,
        Pitch = new FusionAudioPitch
        {
            Min = PitchMin,
            Max = PitchMax,
            ExtensionData = _pitchExtensionData,
        },
        Pan = Pan,
        Cooldown = (int)Math.Round(Cooldown),
        AntiRepeat = AntiRepeat,
        ExtensionData = _extensionData,
    };

    public FusionAudioVariantViewModel AddVariant(string file = "")
    {
        var variant = new FusionAudioVariantViewModel(file);
        AddVariant(variant);
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(Variants));
        return variant;
    }

    public bool RemoveVariant(FusionAudioVariantViewModel variant)
    {
        variant.PropertyChanged -= Variant_PropertyChanged;
        var removed = Variants.Remove(variant);
        if (removed)
        {
            OnPropertyChanged(nameof(SourceSummary));
            OnPropertyChanged(nameof(Variants));
        }
        return removed;
    }

    private void AddVariant(FusionAudioVariantViewModel variant)
    {
        variant.PropertyChanged += Variant_PropertyChanged;
        Variants.Add(variant);
    }

    private void Variant_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(Variants));
    }

    private void Variants_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        OnPropertyChanged(nameof(Variants));

    private static string InferCategory(string eventId)
    {
        var dot = eventId.IndexOf('.');
        return dot > 0 ? eventId[..dot] : "system";
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class FusionAudioVariantViewModel : INotifyPropertyChanged
{
    private string _file;

    public FusionAudioVariantViewModel(string file)
    {
        _file = file;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string File
    {
        get => _file;
        set
        {
            value ??= string.Empty;
            if (_file == value) return;
            _file = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(File)));
        }
    }
}

public sealed class FusionAudioFileOptionViewModel
{
    public FusionAudioFileOptionViewModel(FusionAudioFileOption option)
    {
        CatalogPath = option.CatalogPath;
        ResolvedPath = option.ResolvedPath;
        DisplayName = Path.GetFileName(option.CatalogPath);
        var folder = Path.GetDirectoryName(option.CatalogPath)?.Replace('\\', '/');
        LocationText = string.IsNullOrWhiteSpace(folder)
            ? "audio/se"
            : $"audio/se/{folder}";
        FormatText = string.Join(" · ", option.Formats);
        DetailText = string.IsNullOrWhiteSpace(FormatText)
            ? LocationText
            : $"{LocationText}  ·  {FormatText}";
        SearchText = $"{CatalogPath} {DisplayName} {LocationText} {FormatText}";
    }

    public string CatalogPath { get; }

    public string ResolvedPath { get; }

    public string DisplayName { get; }

    public string LocationText { get; }

    public string FormatText { get; }

    public string DetailText { get; }

    public string SearchText { get; }

    public int GetSearchRank(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 4;
        if (CatalogPath.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (DisplayName.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        if (CatalogPath.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 2;
        if (DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)) return 3;
        return 4;
    }

    public override string ToString() => CatalogPath;
}

public sealed record FusionAudioDiagnosticViewModel
{
    public FusionAudioDiagnosticViewModel(FusionAudioDiagnostic diagnostic)
    {
        Code = diagnostic.Code;
        Message = diagnostic.Message;
        EventId = diagnostic.EventId;
        Severity = diagnostic.Severity;
    }

    public string Code { get; }

    public string Message { get; }

    public string? EventId { get; }

    public FusionAudioDiagnosticSeverity Severity { get; }

    public string Glyph => Severity switch
    {
        FusionAudioDiagnosticSeverity.Error => "●",
        FusionAudioDiagnosticSeverity.Warning => "▲",
        _ => "ⓘ",
    };
}
