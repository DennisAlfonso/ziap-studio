using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Weapons;
using ZiapStudio.Core.Preflight;
using ZiapStudio.Services.Fusion.Weapons;

namespace ZiapStudio.ViewModels;

public sealed class WeaponAdvancedEditorViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<WeaponRarityOptionViewModel> RarityChoices =
    [
        new(null, "Non definita"),
        new(0, "0 — Comune"),
        new(1, "1 — Non Comune"),
        new(2, "2 — Rara"),
        new(3, "3 — Leggendaria"),
        new(4, "4 — Mitologica"),
        new(5, "5 — Definitiva"),
        new(6, "6 — Utopica"),
        new(7, "7 — Progetto"),
    ];

    private readonly int _entryId;
    private readonly DocumentEditSession _session;
    private readonly WeaponNotetagCatalog _catalog;
    private readonly WeaponAdvancedMetadataProvider _metadataProvider = new();
    private readonly WeaponAdvancedNoteEditor _editor = new();
    private readonly WeaponCreationService _creationService = new();
    private bool _isRefreshing;
    private bool _isApplying;
    private IReadOnlyList<WeaponStringOptionViewModel> _perkColumnOneOptions = [];
    private IReadOnlyList<WeaponStringOptionViewModel> _perkColumnTwoOptions = [];
    private IReadOnlyList<WeaponStringOptionViewModel> _perkColumnThreeOptions = [];
    private WeaponStringOptionViewModel? _selectedPerkColumnOne;
    private WeaponStringOptionViewModel? _selectedPerkColumnTwo;
    private WeaponStringOptionViewModel? _selectedPerkColumnThree;
    private WeaponRarityOptionViewModel? _selectedRarity;
    private double _requiredLevel = 1;
    private bool _hasMaximumLevelOverride;
    private double _maximumLevel = 1;
    private IReadOnlyList<WeaponLoreChoiceViewModel> _loreOptions = [];
    private WeaponLoreChoiceViewModel? _selectedLore;
    private string? _currentLoreChoiceKey;
    private bool _hideItemIcon;
    private string _automaticMaximumLevelText = "—";
    private string _maximumLevelDiagnostic = string.Empty;
    private string _loreStatusText = "Nessuna lore associata";
    private string _sourceSummary = string.Empty;
    private string _rawSource = string.Empty;
    private bool _isExpanded = true;
    private WeaponFamilyDefinition? _selectedWeaponFamily;
    private IReadOnlyList<WeaponSubtypeDefinition> _weaponSubtypeOptions = [];
    private WeaponSubtypeDefinition? _selectedWeaponSubtype;
    private double _handedness = 1;
    private bool _combatProfileEnabled;
    private double _damageRate = 1;
    private double _flatDamage;
    private double _defenseRate = 1;
    private double _attackInterval = 1.2;
    private double _attackRange = 1;
    private double _attackRadius = 0.35;
    private double _projectileSpeed;
    private double _projectileColliderRadius = 8;
    private double _magazineSize = 12;
    private double _reloadDuration = 1.4;
    private double _firearmAccuracy = 65;
    private double _firearmStability = 55;
    private double _firearmHandling = 80;
    private double _aimMinimumDistance = 4;
    private double _aimMaximumDistance = 7;
    private double _aimMovementMultiplier = 0.25;
    private double _attackSkillId = 1;
    private double _attackElementId = 1;
    private IReadOnlyList<WeaponAttackSkillOption> _attackSkillOptions = [];
    private WeaponAttackSkillOption? _selectedAttackSkill;
    private IReadOnlyList<WeaponDatabaseOption> _attackElementOptions = [];
    private WeaponDatabaseOption? _selectedAttackElement;
    private bool _hasDamageOverrides;
    private bool _hasAttackBehaviorOverrides;

    public WeaponAdvancedEditorViewModel(
        int entryId,
        DocumentEditSession session,
        WeaponNotetagCatalog? catalog)
    {
        _entryId = entryId;
        _session = session;
        _catalog = catalog ?? WeaponNotetagCatalog.Empty;
        RefreshFromSession();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PreflightIssueViewModel> PreflightIssues { get; } = [];

    public bool HasPreflightIssues => PreflightIssues.Count > 0;

    public string HeaderText => HasPreflightIssues
        ? PreflightIssues.Count == 1
            ? "Weapon Editor · 1 problema"
            : $"Weapon Editor · {PreflightIssues.Count} problemi"
        : "Weapon Editor";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public IReadOnlyList<WeaponRarityOptionViewModel> RarityOptions => RarityChoices;

    public IReadOnlyList<WeaponFamilyDefinition> WeaponFamilyOptions => WeaponAuthoringSchema.Families;

    public WeaponFamilyDefinition? SelectedWeaponFamily
    {
        get => _selectedWeaponFamily;
        set
        {
            if (value is null || !SetProperty(ref _selectedWeaponFamily, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsFirearm));
            if (!_isRefreshing)
            {
                Apply(note => _editor.ApplyFamilyClassification(note, value.Id));
            }
        }
    }

    public IReadOnlyList<WeaponSubtypeDefinition> WeaponSubtypeOptions
    {
        get => _weaponSubtypeOptions;
        private set => SetProperty(ref _weaponSubtypeOptions, value);
    }

    public WeaponSubtypeDefinition? SelectedWeaponSubtype
    {
        get => _selectedWeaponSubtype;
        set
        {
            if (value is not null && SetProperty(ref _selectedWeaponSubtype, value) && !_isRefreshing)
            {
                Apply(note => _editor.SetWeaponSubtype(note, value.Id));
            }
        }
    }

    public bool IsFirearm => SelectedWeaponFamily?.IsFirearm == true;

    public IReadOnlyList<WeaponAttackSkillOption> AttackSkillOptions
    {
        get => _attackSkillOptions;
        private set => SetProperty(ref _attackSkillOptions, value);
    }

    public WeaponAttackSkillOption? SelectedAttackSkill
    {
        get => _selectedAttackSkill;
        set
        {
            if (value is not null && SetProperty(ref _selectedAttackSkill, value) && !_isRefreshing)
            {
                AttackSkillId = value.Id;
            }
        }
    }

    public IReadOnlyList<WeaponDatabaseOption> AttackElementOptions
    {
        get => _attackElementOptions;
        private set => SetProperty(ref _attackElementOptions, value);
    }

    public WeaponDatabaseOption? SelectedAttackElement
    {
        get => _selectedAttackElement;
        set
        {
            if (value is not null && SetProperty(ref _selectedAttackElement, value) && !_isRefreshing)
            {
                AttackElementId = value.Id;
            }
        }
    }

    public bool HasDamageOverrides
    {
        get => _hasDamageOverrides;
        set
        {
            if (!SetProperty(ref _hasDamageOverrides, value) || _isRefreshing)
            {
                return;
            }

            Apply(note => value
                ? _editor.SetDefenseRate(
                    _editor.SetFlatDamage(
                        _editor.SetDamageRate(note, DamageRate),
                        FlatDamage),
                    DefenseRate)
                : _editor.ClearDamageOverrides(note));
        }
    }

    public bool HasAttackBehaviorOverrides
    {
        get => _hasAttackBehaviorOverrides;
        set
        {
            if (!SetProperty(ref _hasAttackBehaviorOverrides, value) || _isRefreshing)
            {
                return;
            }

            Apply(note => value
                ? SetAttackBehaviorOverrides(note)
                : _editor.ClearAttackBehaviorOverrides(note));
        }
    }

    public string AttackSkillOriginText => SelectedAttackSkill is null
        ? "Skill di attacco non risolta: i fallback ABS non sono disponibili."
        : HasAttackBehaviorOverrides
            ? $"Override dell'arma · base: {SelectedAttackSkill.DisplayName} (#{SelectedAttackSkill.Id})"
            : $"Ereditato da {SelectedAttackSkill.DisplayName} (#{SelectedAttackSkill.Id})";

    public string SkillAttackBehaviorText => SelectedAttackSkill is null
        ? "Intervallo — · Portata — · Raggio — · Velocità —"
        : $"Intervallo {FormatValue(SelectedAttackSkill.AttackInterval, "s")} · " +
          $"Portata {FormatValue(SelectedAttackSkill.AttackRange, "tile")} · " +
          $"Raggio {FormatValue(SelectedAttackSkill.AttackRadius, "tile")} · " +
          $"Velocità {FormatValue(SelectedAttackSkill.ProjectileSpeed, string.Empty)}";

    public double Handedness
    {
        get => _handedness;
        set => SetIntegerSchemaValue(ref _handedness, value, 1, 2,
            normalized => Apply(note => _editor.SetHandedness(note, normalized)));
    }

    public bool CombatProfileEnabled
    {
        get => _combatProfileEnabled;
        set
        {
            if (SetProperty(ref _combatProfileEnabled, value) && !_isRefreshing)
            {
                Apply(note => _editor.SetCombatProfileEnabled(note, value));
            }
        }
    }

    public double DamageRate
    {
        get => _damageRate;
        set => SetSchemaValue(ref _damageRate, value, 0, double.MaxValue,
            normalized => Apply(note => _editor.SetDamageRate(note, normalized)));
    }

    public double FlatDamage
    {
        get => _flatDamage;
        set => SetSchemaValue(ref _flatDamage, value, double.MinValue, double.MaxValue,
            normalized => Apply(note => _editor.SetFlatDamage(note, normalized)));
    }

    public double DefenseRate
    {
        get => _defenseRate;
        set => SetSchemaValue(ref _defenseRate, value, 0, double.MaxValue,
            normalized => Apply(note => _editor.SetDefenseRate(note, normalized)));
    }

    public double AttackInterval
    {
        get => _attackInterval;
        set => SetSchemaValue(ref _attackInterval, value, 0, double.MaxValue,
            normalized => Apply(note => _editor.SetAttackInterval(note, normalized)));
    }

    public double AttackRange
    {
        get => _attackRange;
        set => SetSchemaValue(ref _attackRange, value, 0.01, double.MaxValue,
            normalized => Apply(note => _editor.SetAttackRange(note, normalized)));
    }

    public double AttackRadius
    {
        get => _attackRadius;
        set => SetSchemaValue(ref _attackRadius, value, 0, double.MaxValue,
            normalized => Apply(note => _editor.SetAttackRadius(note, normalized)));
    }

    public double ProjectileSpeed
    {
        get => _projectileSpeed;
        set => SetSchemaValue(ref _projectileSpeed, value, 0, double.MaxValue,
            normalized => Apply(note => _editor.SetProjectileSpeed(note, normalized)));
    }

    public double ProjectileColliderRadius
    {
        get => _projectileColliderRadius;
        set => SetSchemaValue(ref _projectileColliderRadius, value, 0.01, double.MaxValue,
            normalized => Apply(note => _editor.SetProjectileColliderRadius(note, normalized)));
    }

    public double MagazineSize
    {
        get => _magazineSize;
        set => SetIntegerSchemaValue(ref _magazineSize, value, 1, int.MaxValue,
            normalized => Apply(note => _editor.SetMagazineSize(note, normalized)));
    }

    public double ReloadDuration
    {
        get => _reloadDuration;
        set => SetSchemaValue(ref _reloadDuration, value, 0, double.MaxValue,
            normalized => Apply(note => _editor.SetReloadDuration(note, normalized)));
    }

    public double FirearmAccuracy
    {
        get => _firearmAccuracy;
        set => SetSchemaValue(ref _firearmAccuracy, value, 0, 100,
            normalized => Apply(note => _editor.SetFirearmAccuracy(note, normalized)));
    }

    public double FirearmStability
    {
        get => _firearmStability;
        set => SetSchemaValue(ref _firearmStability, value, 0, 100,
            normalized => Apply(note => _editor.SetFirearmStability(note, normalized)));
    }

    public double FirearmHandling
    {
        get => _firearmHandling;
        set => SetSchemaValue(ref _firearmHandling, value, 0, 100,
            normalized => Apply(note => _editor.SetFirearmHandling(note, normalized)));
    }

    public double AimMinimumDistance
    {
        get => _aimMinimumDistance;
        set => SetSchemaValue(ref _aimMinimumDistance, value, 1, double.MaxValue,
            normalized => Apply(note => _editor.SetFirearmAimMinimumDistance(note, normalized)));
    }

    public double AimMaximumDistance
    {
        get => _aimMaximumDistance;
        set => SetSchemaValue(ref _aimMaximumDistance, value, 1, double.MaxValue,
            normalized => Apply(note => _editor.SetFirearmAimMaximumDistance(note, normalized)));
    }

    public double AimMovementMultiplier
    {
        get => _aimMovementMultiplier;
        set => SetSchemaValue(ref _aimMovementMultiplier, value, 0.01, 1,
            normalized => Apply(note => _editor.SetFirearmAimMovementMultiplier(note, normalized)));
    }

    public double AttackSkillId
    {
        get => _attackSkillId;
        set => SetIntegerSchemaValue(ref _attackSkillId, value, 1, int.MaxValue,
            normalized => ApplyStandardField(() => _creationService.SetAttackSkill(
                _session, _entryId, normalized)));
    }

    public double AttackElementId
    {
        get => _attackElementId;
        set => SetIntegerSchemaValue(ref _attackElementId, value, 0, int.MaxValue,
            normalized => ApplyStandardField(() => _creationService.SetAttackElement(
                _session, _entryId, normalized)));
    }

    public IReadOnlyList<WeaponStringOptionViewModel> PerkColumnOneOptions
    {
        get => _perkColumnOneOptions;
        private set => SetProperty(ref _perkColumnOneOptions, value);
    }

    public IReadOnlyList<WeaponStringOptionViewModel> PerkColumnTwoOptions
    {
        get => _perkColumnTwoOptions;
        private set => SetProperty(ref _perkColumnTwoOptions, value);
    }

    public IReadOnlyList<WeaponStringOptionViewModel> PerkColumnThreeOptions
    {
        get => _perkColumnThreeOptions;
        private set => SetProperty(ref _perkColumnThreeOptions, value);
    }

    public WeaponStringOptionViewModel? SelectedPerkColumnOne
    {
        get => _selectedPerkColumnOne;
        set
        {
            if (SetProperty(ref _selectedPerkColumnOne, value) && !_isRefreshing && value is not null)
            {
                ApplyPerk(0, value.Value);
            }
        }
    }

    public WeaponStringOptionViewModel? SelectedPerkColumnTwo
    {
        get => _selectedPerkColumnTwo;
        set
        {
            if (SetProperty(ref _selectedPerkColumnTwo, value) && !_isRefreshing && value is not null)
            {
                ApplyPerk(1, value.Value);
            }
        }
    }

    public WeaponStringOptionViewModel? SelectedPerkColumnThree
    {
        get => _selectedPerkColumnThree;
        set
        {
            if (SetProperty(ref _selectedPerkColumnThree, value) && !_isRefreshing && value is not null)
            {
                ApplyPerk(2, value.Value);
            }
        }
    }

    public WeaponRarityOptionViewModel? SelectedRarity
    {
        get => _selectedRarity;
        set
        {
            // ComboBox invia temporaneamente null mentre applica ItemsSource/SelectedItem.
            if (value is not null && SetProperty(ref _selectedRarity, value) && !_isRefreshing)
            {
                Apply(note => _editor.SetRarity(note, value.Value));
            }
        }
    }

    public double RequiredLevel
    {
        get => _requiredLevel;
        set
        {
            if (!TryNormalizePositiveInteger(value, out var normalized) ||
                !SetProperty(ref _requiredLevel, normalized))
            {
                return;
            }

            if (!_isRefreshing)
            {
                Apply(note => _editor.SetRequiredLevel(note, checked((int)normalized)));
            }
        }
    }

    public bool HasMaximumLevelOverride
    {
        get => _hasMaximumLevelOverride;
        set
        {
            if (!SetProperty(ref _hasMaximumLevelOverride, value) || _isRefreshing)
            {
                return;
            }

            Apply(note => _editor.SetMaximumLevel(
                note,
                value ? checked((int)Math.Max(1, MaximumLevel)) : null));
        }
    }

    public double MaximumLevel
    {
        get => _maximumLevel;
        set
        {
            if (!TryNormalizePositiveInteger(value, out var normalized) ||
                !SetProperty(ref _maximumLevel, normalized))
            {
                return;
            }

            if (!_isRefreshing && HasMaximumLevelOverride)
            {
                Apply(note => _editor.SetMaximumLevel(note, checked((int)normalized)));
            }
        }
    }

    public string AutomaticMaximumLevelText
    {
        get => _automaticMaximumLevelText;
        private set => SetProperty(ref _automaticMaximumLevelText, value);
    }

    public string MaximumLevelDiagnostic
    {
        get => _maximumLevelDiagnostic;
        private set
        {
            if (SetProperty(ref _maximumLevelDiagnostic, value))
            {
                OnPropertyChanged(nameof(HasMaximumLevelDiagnostic));
            }
        }
    }

    public bool HasMaximumLevelDiagnostic => !string.IsNullOrWhiteSpace(MaximumLevelDiagnostic);

    public ObservableCollection<WeaponCustomParameterEditorViewModel> CustomParameters { get; } = [];

    public IReadOnlyList<WeaponLoreChoiceViewModel> LoreOptions
    {
        get => _loreOptions;
        private set => SetProperty(ref _loreOptions, value);
    }

    public WeaponLoreChoiceViewModel? SelectedLore
    {
        get => _selectedLore;
        set
        {
            // Ignora il null transitorio del controllo: "Nessuna lore" è un'opzione
            // reale non-null con Key null, quindi resta comunque selezionabile.
            if (value is null || !SetProperty(ref _selectedLore, value) || _isRefreshing)
            {
                return;
            }

            if (!string.Equals(value.Key, _currentLoreChoiceKey, StringComparison.Ordinal))
            {
                Apply(note => _editor.SetLore(note, value.Key));
            }
        }
    }

    public string LoreStatusText
    {
        get => _loreStatusText;
        private set => SetProperty(ref _loreStatusText, value);
    }

    public ObservableCollection<WeaponDisassemblyResultEditorViewModel> DisassemblyResults { get; } = [];

    public bool HideItemIcon
    {
        get => _hideItemIcon;
        set
        {
            if (SetProperty(ref _hideItemIcon, value) && !_isRefreshing)
            {
                Apply(note => _editor.SetHideItemIcon(note, value));
            }
        }
    }

    public ObservableCollection<WeaponNotetagDiagnosticViewModel> Diagnostics { get; } = [];

    public bool HasDiagnostics => Diagnostics.Count > 0;

    public string SourceSummary
    {
        get => _sourceSummary;
        private set => SetProperty(ref _sourceSummary, value);
    }

    public string RawSource
    {
        get => _rawSource;
        private set => SetProperty(ref _rawSource, value);
    }

    public void RefreshFromSession()
    {
        if (_isApplying)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var note = ReadNote();
            var metadata = _metadataProvider.Parse(note, _catalog);
            RawSource = note;
            RebuildWeaponSchema(metadata);
            RebuildPerks(metadata);
            _selectedRarity = RarityChoices.FirstOrDefault(option => option.Value == metadata.Rarity) ??
                RarityChoices[0];
            OnPropertyChanged(nameof(SelectedRarity));
            _requiredLevel = metadata.RequiredLevel ?? 1;
            OnPropertyChanged(nameof(RequiredLevel));
            _hasMaximumLevelOverride = metadata.MaximumLevel is not null;
            _maximumLevel = metadata.MaximumLevel ?? metadata.AutomaticMaximumLevel ?? 1;
            OnPropertyChanged(nameof(HasMaximumLevelOverride));
            OnPropertyChanged(nameof(MaximumLevel));
            AutomaticMaximumLevelText = metadata.AutomaticMaximumLevel is int automatic
                ? $"Automatico dalla rarità: {automatic}"
                : "Automatico dalla rarità: —";
            MaximumLevelDiagnostic = metadata.Rarity is 6 or 7 && metadata.MaximumLevel is null
                ? "⚠ ZDP_WeaponLevel non definisce un massimo per questa rarità: fallback 1."
                : metadata.MaximumLevel is int explicitLevel &&
                    metadata.AutomaticMaximumLevel is int derived &&
                    explicitLevel != derived
                        ? $"Override {explicitLevel} · derivato dalla rarità: {derived}"
                        : string.Empty;
            RebuildCustomParameters(metadata);
            RebuildLore(metadata);
            RebuildDisassembly(metadata);
            _hideItemIcon = metadata.HideItemIcon;
            OnPropertyChanged(nameof(HideItemIcon));
            RebuildDiagnostics(metadata);
            SourceSummary = CreateSourceSummary(metadata);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    public void ApplyPreflightIssues(IEnumerable<PreflightIssue> issues)
    {
        PreflightIssues.Clear();
        foreach (var issue in issues)
        {
            PreflightIssues.Add(PreflightIssueViewModel.FromActive(issue));
        }

        OnPropertyChanged(nameof(HasPreflightIssues));
        OnPropertyChanged(nameof(HeaderText));
    }

    public void AddCustomParameter()
    {
        var usedIds = CustomParameters.Select(parameter => parameter.ParameterId).ToHashSet();
        var id = _catalog.CustomParameters
            .Select(option => option.Id)
            .FirstOrDefault(candidate => !usedIds.Contains(candidate));
        if (id <= 0)
        {
            id = usedIds.Count == 0 ? 1 : usedIds.Max() + 1;
        }

        Apply(note => _editor.SetCustomParameter(note, id, id, 0));
    }

    public void AddDisassemblyResult()
    {
        var resource = _catalog.DisassemblyResources.FirstOrDefault()?.RawValue ??
            "{db[0].partiArmamento}";
        Apply(note => _editor.AddDisassemblyResult(note, resource, 1, 1, 100));
    }

    internal void UpdateCustomParameter(
        WeaponCustomParameterEditorViewModel parameter,
        int previousId)
    {
        if (_isRefreshing)
        {
            return;
        }

        Apply(note => _editor.SetCustomParameter(
            note,
            previousId,
            parameter.ParameterId,
            parameter.Value));
    }

    internal void RemoveCustomParameter(WeaponCustomParameterEditorViewModel parameter) =>
        Apply(note => _editor.RemoveCustomParameter(note, parameter.ParameterId));

    internal void UpdateDisassemblyResult(WeaponDisassemblyResultEditorViewModel result)
    {
        if (_isRefreshing)
        {
            return;
        }

        Apply(note => _editor.UpdateDisassemblyResult(
            note,
            result.Index,
            result.SelectedResource?.RawValue ?? result.RawResource,
            result.MinimumQuantity,
            result.MaximumQuantity,
            result.Probability));
    }

    internal void RemoveDisassemblyResult(WeaponDisassemblyResultEditorViewModel result) =>
        Apply(note => _editor.RemoveDisassemblyResult(note, result.Index));

    private void ApplyPerk(int column, string value)
    {
        var values = new[]
        {
            SelectedPerkColumnOne?.Value ?? "random",
            SelectedPerkColumnTwo?.Value ?? "random",
            SelectedPerkColumnThree?.Value ?? "nullo",
        };
        values[column] = value;
        Apply(note => _editor.SetPerks(note, values));
    }

    private void Apply(Func<string, string> mutation)
    {
        var current = ReadNote();
        var updated = mutation(current);
        if (string.Equals(current, updated, StringComparison.Ordinal))
        {
            return;
        }

        _isApplying = true;
        try
        {
            _session.SetValue(_entryId, "note", JsonValue.Create(updated));
        }
        finally
        {
            _isApplying = false;
        }

        RefreshFromSession();
    }

    private void ApplyStandardField(Action mutation)
    {
        if (_isRefreshing)
        {
            return;
        }

        _isApplying = true;
        try
        {
            mutation();
        }
        finally
        {
            _isApplying = false;
        }

        RefreshFromSession();
    }

    private string ReadNote() =>
        _session.TryGetValue(_entryId, "note", out var node) &&
        node is JsonValue value && value.TryGetValue<string>(out var note)
            ? note
            : string.Empty;

    private void RebuildWeaponSchema(WeaponAdvancedMetadata metadata)
    {
        _selectedWeaponFamily = WeaponAuthoringSchema.FindFamily(metadata.Classification.Family);
        OnPropertyChanged(nameof(SelectedWeaponFamily));
        OnPropertyChanged(nameof(IsFirearm));

        var subtype = metadata.Classification.Subtype?.Trim();
        var subtypeOptions = (_selectedWeaponFamily?.IsFirearm == true
                ? WeaponAuthoringSchema.FirearmSubtypes
                : _selectedWeaponFamily is null
                    ? Array.Empty<WeaponSubtypeDefinition>()
                    : [new WeaponSubtypeDefinition(
                        _selectedWeaponFamily.DefaultSubtype,
                        _selectedWeaponFamily.DisplayName)])
            .ToList();
        if (!string.IsNullOrWhiteSpace(subtype) && subtypeOptions.All(option =>
            !option.Id.Equals(subtype, StringComparison.OrdinalIgnoreCase)))
        {
            subtypeOptions.Add(new WeaponSubtypeDefinition(subtype, $"Non riconosciuto · {subtype}"));
        }

        WeaponSubtypeOptions = subtypeOptions;
        _selectedWeaponSubtype = subtypeOptions.FirstOrDefault(option => option.Id.Equals(
            subtype,
            StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(SelectedWeaponSubtype));

        _handedness = metadata.Classification.Handedness ?? 1;
        _combatProfileEnabled = metadata.CombatProfile.Enabled;
        _hasDamageOverrides = metadata.CombatProfile.DamageRate is not null ||
            metadata.CombatProfile.FlatDamage is not null ||
            metadata.CombatProfile.DefenseRate is not null;
        _hasAttackBehaviorOverrides = metadata.CombatProfile.AttackInterval is not null ||
            metadata.CombatProfile.AttackRange is not null ||
            metadata.CombatProfile.AttackRadius is not null ||
            metadata.CombatProfile.ProjectileSpeed is not null ||
            metadata.CombatProfile.ProjectileColliderRadius is not null;
        _attackSkillId = ReadTraitDataId(35, 1);
        RebuildAttackSkillOptions(checked((int)_attackSkillId));
        _attackElementId = ReadTraitDataId(31, 1);
        RebuildAttackElementOptions(checked((int)_attackElementId));
        _damageRate = metadata.CombatProfile.DamageRate ?? 1;
        _flatDamage = metadata.CombatProfile.FlatDamage ?? 0;
        _defenseRate = metadata.CombatProfile.DefenseRate ?? 1;
        _attackInterval = metadata.CombatProfile.AttackInterval ?? _selectedAttackSkill?.AttackInterval ?? 1.2;
        _attackRange = metadata.CombatProfile.AttackRange ?? _selectedAttackSkill?.AttackRange ?? 1;
        _attackRadius = metadata.CombatProfile.AttackRadius ?? _selectedAttackSkill?.AttackRadius ?? 0.35;
        _projectileSpeed = metadata.CombatProfile.ProjectileSpeed ?? _selectedAttackSkill?.ProjectileSpeed ?? 0;
        _projectileColliderRadius = metadata.CombatProfile.ProjectileColliderRadius ??
            _selectedAttackSkill?.ProjectileColliderRadius ?? 8;
        _magazineSize = metadata.Firearm.MagazineSize ?? 12;
        _reloadDuration = metadata.Firearm.ReloadDuration ?? 1.4;
        _firearmAccuracy = metadata.Firearm.Accuracy ?? 65;
        _firearmStability = metadata.Firearm.Stability ?? 55;
        _firearmHandling = metadata.Firearm.Handling ?? 80;
        _aimMinimumDistance = metadata.Firearm.AimMinimumDistance ?? 4;
        _aimMaximumDistance = metadata.Firearm.AimMaximumDistance ?? 7;
        _aimMovementMultiplier = metadata.Firearm.AimMovementMultiplier ?? 0.25;
        foreach (var propertyName in new[]
        {
            nameof(Handedness), nameof(CombatProfileEnabled), nameof(DamageRate),
            nameof(FlatDamage), nameof(DefenseRate), nameof(AttackInterval),
            nameof(AttackRange), nameof(AttackRadius), nameof(ProjectileSpeed),
            nameof(ProjectileColliderRadius), nameof(MagazineSize), nameof(ReloadDuration),
            nameof(FirearmAccuracy), nameof(FirearmStability), nameof(FirearmHandling),
            nameof(AimMinimumDistance), nameof(AimMaximumDistance),
            nameof(AimMovementMultiplier), nameof(AttackSkillId), nameof(AttackElementId),
            nameof(SelectedAttackSkill), nameof(SelectedAttackElement), nameof(HasDamageOverrides),
            nameof(HasAttackBehaviorOverrides), nameof(AttackSkillOriginText),
            nameof(SkillAttackBehaviorText),
        })
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void RebuildAttackSkillOptions(int currentId)
    {
        var options = _catalog.AttackSkills.ToList();
        if (options.All(option => option.Id != currentId))
        {
            options.Add(new WeaponAttackSkillOption(
                currentId,
                $"Abilità non risolta {currentId}",
                null,
                null,
                null,
                null,
                null));
        }

        AttackSkillOptions = options.OrderBy(option => option.Id).ToArray();
        _selectedAttackSkill = AttackSkillOptions.First(option => option.Id == currentId);
    }

    private void RebuildAttackElementOptions(int currentId)
    {
        var options = _catalog.Elements.ToList();
        if (options.All(option => option.Id != currentId))
        {
            options.Add(new WeaponDatabaseOption(currentId, $"Elemento non risolto {currentId}"));
        }

        AttackElementOptions = options.OrderBy(option => option.Id).ToArray();
        _selectedAttackElement = AttackElementOptions.First(option => option.Id == currentId);
    }

    private int ReadTraitDataId(int code, int fallback)
    {
        if (!_session.TryGetValue(_entryId, "traits", out var node) || node is not JsonArray traits)
        {
            return fallback;
        }

        foreach (var trait in traits.OfType<JsonObject>())
        {
            if (trait["code"] is JsonValue codeValue && codeValue.TryGetValue<int>(out var traitCode) &&
                traitCode == code && trait["dataId"] is JsonValue dataValue &&
                dataValue.TryGetValue<int>(out var dataId))
            {
                return dataId;
            }
        }

        return fallback;
    }

    private void RebuildPerks(WeaponAdvancedMetadata metadata)
    {
        var values = Enumerable.Range(0, 3)
            .Select(index => index < metadata.Perks.Count
                ? metadata.Perks[index]
                : index == 2 ? "nullo" : "random")
            .ToArray();
        PerkColumnOneOptions = CreatePerkOptions(1, values[0]);
        PerkColumnTwoOptions = CreatePerkOptions(2, values[1]);
        PerkColumnThreeOptions = CreatePerkOptions(3, values[2]);
        _selectedPerkColumnOne = PerkColumnOneOptions.First(option =>
            option.Value.Equals(values[0], StringComparison.OrdinalIgnoreCase));
        _selectedPerkColumnTwo = PerkColumnTwoOptions.First(option =>
            option.Value.Equals(values[1], StringComparison.OrdinalIgnoreCase));
        _selectedPerkColumnThree = PerkColumnThreeOptions.First(option =>
            option.Value.Equals(values[2], StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(SelectedPerkColumnOne));
        OnPropertyChanged(nameof(SelectedPerkColumnTwo));
        OnPropertyChanged(nameof(SelectedPerkColumnThree));
    }

    private IReadOnlyList<WeaponStringOptionViewModel> CreatePerkOptions(int column, string current)
    {
        var options = _catalog.Perks
            .Where(option => option.Column == column)
            .Select(option => new WeaponStringOptionViewModel(
                option.Id,
                option.Id is "random" or "nullo"
                    ? option.DisplayName
                    : $"{option.DisplayName} · {option.Id}"))
            .ToList();
        if (options.All(option => !option.Value.Equals(current, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new WeaponStringOptionViewModel(current, $"Non riconosciuto · {current}"));
        }

        return options;
    }

    private void RebuildCustomParameters(WeaponAdvancedMetadata metadata)
    {
        CustomParameters.Clear();
        foreach (var parameter in metadata.CustomParameters)
        {
            var name = _catalog.CustomParameters.FirstOrDefault(option => option.Id == parameter.Id)
                ?.DisplayName ?? $"Parametro custom #{parameter.Id}";
            CustomParameters.Add(new WeaponCustomParameterEditorViewModel(
                this,
                parameter.Id,
                parameter.Value,
                name));
        }
    }

    private void RebuildLore(WeaponAdvancedMetadata metadata)
    {
        var choices = new List<WeaponLoreChoiceViewModel>
        {
            new(null, "Nessuna lore"),
        };
        choices.AddRange(_catalog.LoreEntries.Select(entry =>
            new WeaponLoreChoiceViewModel(entry.Key, $"{entry.Title} · ID {entry.Id}")));
        LoreOptions = choices;
        _selectedLore = metadata.ResolvedLore is null
            ? choices[0]
            : choices.First(choice => choice.Key == metadata.ResolvedLore.Key);
        _currentLoreChoiceKey = metadata.ResolvedLore?.Key;
        OnPropertyChanged(nameof(SelectedLore));
        LoreStatusText = metadata.LoreResolutionKind switch
        {
            WeaponLoreResolutionKind.LegacyAlias =>
                $"Raw: {metadata.LoreRawValue} · alias legacy preservato",
            WeaponLoreResolutionKind.NumericId =>
                $"Raw: {metadata.LoreRawValue} · risolto tramite ID",
            WeaponLoreResolutionKind.CaseInsensitiveKey =>
                $"Raw: {metadata.LoreRawValue} · risolto senza distinzione maiuscole",
            WeaponLoreResolutionKind.Missing =>
                $"⚠ Raw non risolto: {metadata.LoreRawValue}",
            WeaponLoreResolutionKind.ExactKey =>
                $"Chiave raw: {metadata.LoreRawValue}",
            _ => "Nessuna lore associata",
        };
    }

    private void RebuildDisassembly(WeaponAdvancedMetadata metadata)
    {
        DisassemblyResults.Clear();
        for (var index = 0; index < metadata.DisassemblyResults.Count; index++)
        {
            var result = metadata.DisassemblyResults[index];
            var resources = _catalog.DisassemblyResources.ToList();
            if (resources.All(option => !option.RawValue.Equals(
                result.RawResource,
                StringComparison.OrdinalIgnoreCase)))
            {
                resources.Add(new WeaponDisassemblyResourceOption(
                    result.RawResource,
                    result.DisplayName));
            }

            DisassemblyResults.Add(new WeaponDisassemblyResultEditorViewModel(
                this,
                index,
                result,
                resources));
        }
    }

    private void RebuildDiagnostics(WeaponAdvancedMetadata metadata)
    {
        Diagnostics.Clear();
        foreach (var diagnostic in metadata.Diagnostics)
        {
            Diagnostics.Add(new WeaponNotetagDiagnosticViewModel(
                diagnostic.Severity switch
                {
                    WeaponNotetagDiagnosticSeverity.Error => "✕",
                    WeaponNotetagDiagnosticSeverity.Warning => "⚠",
                    _ => "ℹ",
                },
                diagnostic.Message));
        }

        OnPropertyChanged(nameof(HasDiagnostics));
    }

    private static string CreateSourceSummary(WeaponAdvancedMetadata metadata)
    {
        var blockText = metadata.RecognizedBlockCount == 1
            ? "1 blocco riconosciuto"
            : $"{metadata.RecognizedBlockCount} blocchi riconosciuti";
        return $"{metadata.RecognizedInlineTagCount} notetag riconosciuti · " +
            $"{blockText} · {metadata.UnmanagedLineCount} non gestiti";
    }

    private static bool TryNormalizePositiveInteger(double value, out double normalized)
    {
        normalized = value;
        return !double.IsNaN(value) && !double.IsInfinity(value) &&
            value >= 1 && value <= int.MaxValue && value == Math.Truncate(value);
    }

    private string SetAttackBehaviorOverrides(string note)
    {
        var updated = _editor.SetAttackInterval(note, AttackInterval);
        updated = _editor.SetAttackRange(updated, AttackRange);
        updated = _editor.SetAttackRadius(updated, AttackRadius);
        updated = _editor.SetProjectileSpeed(updated, ProjectileSpeed);
        return _editor.SetProjectileColliderRadius(updated, ProjectileColliderRadius);
    }

    private static string FormatValue(double? value, string unit) => value is null
        ? "—"
        : $"{value.Value:0.##}{(unit.Length == 0 ? string.Empty : $" {unit}")}";

    private void SetSchemaValue(
        ref double field,
        double value,
        double minimum,
        double maximum,
        Action<double> mutation,
        [CallerMemberName] string? propertyName = null)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum)
        {
            OnPropertyChanged(propertyName);
            return;
        }

        if (SetProperty(ref field, value, propertyName) && !_isRefreshing)
        {
            mutation(value);
        }
    }

    private void SetIntegerSchemaValue(
        ref double field,
        double value,
        int minimum,
        int maximum,
        Action<int> mutation,
        [CallerMemberName] string? propertyName = null)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value != Math.Truncate(value) ||
            value < minimum || value > maximum)
        {
            OnPropertyChanged(propertyName);
            return;
        }

        if (SetProperty(ref field, value, propertyName) && !_isRefreshing)
        {
            mutation(checked((int)value));
        }
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

public sealed record WeaponStringOptionViewModel(string Value, string DisplayName);

public sealed record WeaponRarityOptionViewModel(int? Value, string DisplayName);

public sealed record WeaponLoreChoiceViewModel(string? Key, string DisplayName);

public sealed record WeaponNotetagDiagnosticViewModel(string Glyph, string Message);

public sealed class WeaponCustomParameterEditorViewModel : INotifyPropertyChanged
{
    private readonly WeaponAdvancedEditorViewModel _owner;
    private int _parameterId;
    private int _value;

    internal WeaponCustomParameterEditorViewModel(
        WeaponAdvancedEditorViewModel owner,
        int parameterId,
        int value,
        string displayName)
    {
        _owner = owner;
        _parameterId = parameterId;
        _value = value;
        DisplayName = displayName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; }

    public int ParameterId
    {
        get => _parameterId;
        set
        {
            if (value < 1 || value == _parameterId)
            {
                return;
            }

            var previous = _parameterId;
            _parameterId = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ParameterId)));
            _owner.UpdateCustomParameter(this, previous);
        }
    }

    public int Value
    {
        get => _value;
        set
        {
            if (value == _value)
            {
                return;
            }

            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            _owner.UpdateCustomParameter(this, ParameterId);
        }
    }

    public void Remove() => _owner.RemoveCustomParameter(this);
}

public sealed class WeaponDisassemblyResultEditorViewModel : INotifyPropertyChanged
{
    private readonly WeaponAdvancedEditorViewModel _owner;
    private WeaponDisassemblyResourceOption? _selectedResource;
    private int _minimumQuantity;
    private int _maximumQuantity;
    private int _probability;

    internal WeaponDisassemblyResultEditorViewModel(
        WeaponAdvancedEditorViewModel owner,
        int index,
        WeaponDisassemblyResultMetadata metadata,
        IReadOnlyList<WeaponDisassemblyResourceOption> resources)
    {
        _owner = owner;
        Index = index;
        RawResource = metadata.RawResource;
        ResourceOptions = resources;
        _selectedResource = resources.First(option => option.RawValue.Equals(
            metadata.RawResource,
            StringComparison.OrdinalIgnoreCase));
        _minimumQuantity = metadata.MinimumQuantity;
        _maximumQuantity = metadata.MaximumQuantity;
        _probability = metadata.Probability;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

    public string RawResource { get; }

    public IReadOnlyList<WeaponDisassemblyResourceOption> ResourceOptions { get; }

    public WeaponDisassemblyResourceOption? SelectedResource
    {
        get => _selectedResource;
        set
        {
            if (value is null || value == _selectedResource)
            {
                return;
            }

            _selectedResource = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedResource)));
            _owner.UpdateDisassemblyResult(this);
        }
    }

    public int MinimumQuantity
    {
        get => _minimumQuantity;
        set => SetValue(ref _minimumQuantity, value, nameof(MinimumQuantity), minimum: 0, maximum: int.MaxValue);
    }

    public int MaximumQuantity
    {
        get => _maximumQuantity;
        set => SetValue(ref _maximumQuantity, value, nameof(MaximumQuantity), minimum: 0, maximum: int.MaxValue);
    }

    public int Probability
    {
        get => _probability;
        set => SetValue(ref _probability, value, nameof(Probability), minimum: 0, maximum: 100);
    }

    public void Remove() => _owner.RemoveDisassemblyResult(this);

    private void SetValue(ref int field, int value, string propertyName, int minimum, int maximum)
    {
        if (value < minimum || value > maximum || value == field)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        _owner.UpdateDisassemblyResult(this);
    }
}
