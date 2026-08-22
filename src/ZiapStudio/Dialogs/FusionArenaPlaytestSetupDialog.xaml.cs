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

    public FusionArenaPlaytestSetupDialog(
        string arenaId,
        string arenaDisplayName,
        IReadOnlyList<FusionArenaPlaytestMapOption> maps,
        IReadOnlyList<string> runtimeExecutables,
        FusionArenaPlaytestSettings settings,
        FusionArenaPlaytestProfile? profile,
        bool launchAfterSave)
    {
        _arenaId = arenaId;
        _hasExistingProfile = profile is not null;
        SettingsSource = settings;
        InitializeComponent();

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

        var selectedMode = profile?.Mode ?? FusionArenaPlaytestLaunchMode.Direct;
        ModeComboBox.SelectedItem = ModeOptions.First(option => option.Mode == selectedMode);
        var selectedMap = maps.FirstOrDefault(map => map.MapId == profile?.MapId) ?? maps.FirstOrDefault();
        MapComboBox.SelectedItem = selectedMap;
        DirectionComboBox.SelectedItem = DirectionOptions.First(option =>
            option.Value == (profile?.Direction ?? 2));
        CommonEventNumberBox.Value = profile?.CommonEventId ?? 0;

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
        };
        Settings = SettingsSource with
        {
            RuntimeExecutable = Path.GetFullPath(runtimePath),
        };
    }

    private FusionArenaPlaytestSettings SettingsSource { get; set; } = new();

    private static int ToInteger(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? -1 : (int)Math.Round(value);

    private sealed record ModeOption(
        FusionArenaPlaytestLaunchMode Mode,
        string DisplayName,
        string Description);

    private sealed record DirectionOption(int Value, string DisplayName);
}
