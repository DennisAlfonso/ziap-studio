using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiapStudio.Services.Fusion.Bosses;

namespace ZiapStudio.Dialogs;

public sealed record FusionArenaPlaytestMapOption(
    int MapId,
    string DisplayName,
    int Width,
    int Height,
    int SuggestedX,
    int SuggestedY);

public sealed partial class FusionArenaPlaytestSetupDialog : ContentDialog
{
    private readonly string _arenaId;
    private readonly bool _hasExistingProfile;
    private readonly IReadOnlyList<ComboBox> _armorComboBoxes;

    public FusionArenaPlaytestSetupDialog(
        string arenaId,
        string arenaDisplayName,
        IReadOnlyList<FusionArenaPlaytestMapOption> maps,
        IReadOnlyList<string> runtimeExecutables,
        FusionArenaPlaytestCatalog catalog,
        FusionArenaPlaytestSettings settings,
        FusionArenaPlaytestProfile? profile,
        bool launchAfterSave)
    {
        _arenaId = arenaId;
        _hasExistingProfile = profile is not null;
        SettingsSource = settings;
        InitializeComponent();
        _armorComboBoxes =
        [
            Armor1ComboBox,
            Armor2ComboBox,
            Armor3ComboBox,
            Armor4ComboBox,
            Armor5ComboBox,
            Armor6ComboBox,
            Armor7ComboBox,
            Armor8ComboBox,
        ];

        Title = profile is null ? "Setup primo avvio" : "Configura playtest arena";
        PrimaryButtonText = launchAfterSave ? "Salva e avvia" : "Salva";
        ArenaTextBlock.Text = arenaDisplayName;

        RuntimeComboBox.ItemsSource = runtimeExecutables;
        RuntimeComboBox.Text = string.IsNullOrWhiteSpace(settings.RuntimeExecutable)
            ? runtimeExecutables.FirstOrDefault() ?? string.Empty
            : settings.RuntimeExecutable;

        ModeComboBox.ItemsSource = ModeOptions;
        MapComboBox.ItemsSource = maps;
        DirectionComboBox.ItemsSource = DirectionOptions;

        PopulateLoadoutSelectors(catalog, profile?.Loadout);

        var selectedMode = profile?.Mode ?? FusionArenaPlaytestLaunchMode.Direct;
        ModeComboBox.SelectedItem = ModeOptions.First(option => option.Mode == selectedMode);
        var selectedMap = maps.FirstOrDefault(map => map.MapId == profile?.MapId) ?? maps.FirstOrDefault();
        MapComboBox.SelectedItem = selectedMap;
        DirectionComboBox.SelectedItem = DirectionOptions.First(option =>
            option.Value == (profile?.Direction ?? 2));
        CommonEventNumberBox.Value = profile?.CommonEventId ?? 0;
        LevelNumberBox.Value = profile?.Loadout.Level ?? new FusionArenaPlaytestLoadout().Level;
        JumpNumberBox.Value = profile?.JumpValue ?? 12;

        SelectSkills(profile?.Loadout.SkillIds ?? new FusionArenaPlaytestLoadout().SkillIds);

        if (profile is not null)
        {
            PlayerXNumberBox.Value = profile.PlayerX;
            PlayerYNumberBox.Value = profile.PlayerY;
        }
        else if (selectedMap is not null)
        {
            UseSuggestedPosition(selectedMap);
        }

        UpdateModeDescription();
        UpdateMapLimitsAndHint(selectedMap);
    }

    public FusionArenaPlaytestSettings? Settings { get; private set; }

    public FusionArenaPlaytestProfile? Profile { get; private set; }

    private static IReadOnlyList<ModeOption> ModeOptions { get; } =
    [
        new(
            FusionArenaPlaytestLaunchMode.Direct,
            "Battaglia diretta (consigliata)",
            "Trasferisce Yato nell'arena e avvia FusionArena direttamente. Salta dialoghi, switch e lock narrativi, ma conserva plugin, encounter, spawn e trace runtime reali."),
        new(
            FusionArenaPlaytestLaunchMode.Narrative,
            "Ingresso narrativo",
            "Trasferisce Yato sulla mappa senza evocare il boss. Autorun e interazioni della mappa restano responsabili dell'avvio; puoi prenotare un evento comune d'ingresso."),
        new(
            FusionArenaPlaytestLaunchMode.MapOnly,
            "Sola mappa",
            "Carica la mappa e il loadout senza avviare encounter o sequenze narrative. Utile per controllare marker, collisioni e setup fisico."),
    ];

    private static IReadOnlyList<DirectionOption> DirectionOptions { get; } =
    [
        new(2, "Giù"),
        new(4, "Sinistra"),
        new(6, "Destra"),
        new(8, "Su"),
    ];

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateModeDescription();

    private void MapComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var map = MapComboBox.SelectedItem as FusionArenaPlaytestMapOption;
        if (!_hasExistingProfile && map is not null)
        {
            UseSuggestedPosition(map);
        }
        UpdateMapLimitsAndHint(map);
    }

    private void UpdateModeDescription()
    {
        var option = ModeComboBox.SelectedItem as ModeOption;
        ModeDescriptionTextBlock.Text = option?.Description ?? string.Empty;
        CommonEventNumberBox.IsEnabled = option?.Mode == FusionArenaPlaytestLaunchMode.Narrative;
    }

    private void UpdateMapLimitsAndHint(FusionArenaPlaytestMapOption? map)
    {
        if (map is null)
        {
            MapHintTextBlock.Text = "Nessuna mappa disponibile per l'arena.";
            return;
        }
        PlayerXNumberBox.Maximum = Math.Max(0, map.Width - 1);
        PlayerYNumberBox.Maximum = Math.Max(0, map.Height - 1);
        MapHintTextBlock.Text =
            $"Mappa {map.Width}×{map.Height} tile · punto sicuro suggerito ({map.SuggestedX}, {map.SuggestedY}), calcolato sotto il bossSpawn quando disponibile.";
    }

    private void UseSuggestedPosition(FusionArenaPlaytestMapOption map)
    {
        PlayerXNumberBox.Value = map.SuggestedX;
        PlayerYNumberBox.Value = map.SuggestedY;
    }

    private void ContentDialog_PrimaryButtonClick(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        var runtimePath = (RuntimeComboBox.Text ?? string.Empty).Trim().Trim('"');
        var map = MapComboBox.SelectedItem as FusionArenaPlaytestMapOption;
        var mode = (ModeComboBox.SelectedItem as ModeOption)?.Mode;
        var direction = (DirectionComboBox.SelectedItem as DirectionOption)?.Value;
        var playerX = ToInteger(PlayerXNumberBox.Value);
        var playerY = ToInteger(PlayerYNumberBox.Value);
        var loadout = BuildLoadout();
        var jumpValue = Math.Max(0, ToInteger(JumpNumberBox.Value));

        string? validationMessage = null;
        if (!File.Exists(runtimePath) || !runtimePath.EndsWith("nw.exe", StringComparison.OrdinalIgnoreCase))
        {
            validationMessage = "Seleziona il file nw.exe incluso in RPG Maker MZ.";
        }
        else if (map is null || mode is null || direction is null)
        {
            validationMessage = "Completa metodo d'ingresso, mappa e direzione.";
        }
        else if (playerX < 0 || playerY < 0 || playerX >= map.Width || playerY >= map.Height)
        {
            validationMessage = $"Lo spawn deve rientrare nella mappa: X 0–{map.Width - 1}, Y 0–{map.Height - 1}.";
        }
        else if (loadout.ActorId <= 0 || loadout.WeaponId <= 0)
        {
            validationMessage = "Completa attore iniziale e arma iniziale.";
        }
        else if (loadout.Level <= 0)
        {
            validationMessage = "Il livello iniziale deve essere almeno 1.";
        }

        if (validationMessage is not null)
        {
            args.Cancel = true;
            ValidationInfoBar.Message = validationMessage;
            ValidationInfoBar.IsOpen = true;
            return;
        }

        ValidationInfoBar.IsOpen = false;
        Profile = new FusionArenaPlaytestProfile
        {
            ArenaId = _arenaId,
            Mode = mode!.Value,
            MapId = map!.MapId,
            PlayerX = playerX,
            PlayerY = playerY,
            Direction = direction!.Value,
            CommonEventId = mode == FusionArenaPlaytestLaunchMode.Narrative
                ? Math.Max(0, ToInteger(CommonEventNumberBox.Value))
                : 0,
            JumpValue = jumpValue,
            Loadout = loadout,
        };
        Settings = SettingsSource with
        {
            RuntimeExecutable = Path.GetFullPath(runtimePath),
        };
    }

    private FusionArenaPlaytestSettings SettingsSource { get; set; } = new();

    private static int ToInteger(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? -1 : (int)Math.Round(value);

    private void PopulateLoadoutSelectors(
        FusionArenaPlaytestCatalog catalog,
        FusionArenaPlaytestLoadout? loadout)
    {
        var defaultLoadout = new FusionArenaPlaytestLoadout();
        var effectiveLoadout = loadout ?? defaultLoadout;

        ActorComboBox.ItemsSource = EnsureOption(catalog.Actors, effectiveLoadout.ActorId, "Attore");
        ActorComboBox.SelectedItem = SelectOption(
            ActorComboBox.ItemsSource as IReadOnlyList<FusionArenaPlaytestCatalogOption>,
            effectiveLoadout.ActorId);

        WeaponComboBox.ItemsSource = EnsureOption(catalog.Weapons, effectiveLoadout.WeaponId, "Arma");
        WeaponComboBox.SelectedItem = SelectOption(
            WeaponComboBox.ItemsSource as IReadOnlyList<FusionArenaPlaytestCatalogOption>,
            effectiveLoadout.WeaponId);

        var armorOptions = EnsureOption(catalog.Armors, effectiveLoadout.ArmorIds, "Armatura");
        for (var index = 0; index < _armorComboBoxes.Count; index++)
        {
            var comboBox = _armorComboBoxes[index];
            comboBox.ItemsSource = armorOptions;
            var armorId = index < effectiveLoadout.ArmorIds.Count
                ? effectiveLoadout.ArmorIds[index]
                : 0;
            comboBox.SelectedItem = SelectOption(armorOptions, armorId);
        }

        SkillsListView.ItemsSource = EnsureOption(catalog.Skills, effectiveLoadout.SkillIds, "Skill");
    }

    private FusionArenaPlaytestLoadout BuildLoadout() => new()
    {
        ActorId = SelectedOptionId(ActorComboBox),
        Level = Math.Max(1, ToInteger(LevelNumberBox.Value)),
        WeaponId = SelectedOptionId(WeaponComboBox),
        ArmorIds = _armorComboBoxes.Select(SelectedOptionId).ToArray(),
        SkillIds = SkillsListView.SelectedItems
            .OfType<FusionArenaPlaytestCatalogOption>()
            .Select(option => option.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToArray(),
    };

    private void SelectSkills(IReadOnlyList<int> skillIds)
    {
        var options = SkillsListView.ItemsSource as IReadOnlyList<FusionArenaPlaytestCatalogOption>;
        if (options is null)
        {
            return;
        }

        foreach (var option in options.Where(option => skillIds.Contains(option.Id)))
        {
            SkillsListView.SelectedItems.Add(option);
        }
    }

    private static IReadOnlyList<FusionArenaPlaytestCatalogOption> EnsureOption(
        IReadOnlyList<FusionArenaPlaytestCatalogOption> options,
        int requiredId,
        string label) =>
        EnsureOption(options, [requiredId], label);

    private static IReadOnlyList<FusionArenaPlaytestCatalogOption> EnsureOption(
        IReadOnlyList<FusionArenaPlaytestCatalogOption> options,
        IReadOnlyList<int> requiredIds,
        string label)
    {
        var normalized = new Dictionary<int, FusionArenaPlaytestCatalogOption>();
        foreach (var option in options)
        {
            normalized[option.Id] = option;
        }

        foreach (var id in requiredIds.Where(id => id > 0))
        {
            if (!normalized.ContainsKey(id))
            {
                normalized[id] = new FusionArenaPlaytestCatalogOption(
                    id,
                    $"#{id:000} · {label} non trovato");
            }
        }

        return normalized.Values
            .OrderBy(option => option.Id)
            .ToArray();
    }

    private static FusionArenaPlaytestCatalogOption? SelectOption(
        IReadOnlyList<FusionArenaPlaytestCatalogOption>? options,
        int id) => options?.FirstOrDefault(option => option.Id == id);

    private static int SelectedOptionId(ComboBox comboBox) =>
        (comboBox.SelectedItem as FusionArenaPlaytestCatalogOption)?.Id ?? 0;

    private sealed record ModeOption(
        FusionArenaPlaytestLaunchMode Mode,
        string DisplayName,
        string Description);

    private sealed record DirectionOption(int Value, string DisplayName);
}
