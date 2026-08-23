using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiapStudio.Core.Fusion.Bosses;
using ZiapStudio.Dialogs;
using ZiapStudio.Services.Fusion.Bosses;
using ZiapStudio.ViewModels;

namespace ZiapStudio.Views;

public sealed partial class FusionBossDocumentView : UserControl
{
    private FusionBossDocumentViewModel? _viewModel;
    private readonly FusionRuntimeTraceService _runtimeTraceService = new();
    private readonly FusionArenaPlaytestService _arenaPlaytestService = new();
    private CancellationTokenSource? _runtimeTraceLoadCancellation;
    private bool _isFocusMode;
    private bool _isEditMode;

    public FusionBossDocumentView()
    {
        InitializeComponent();
        DataContextChanged += View_DataContextChanged;
        Loaded += View_Loaded;
        Unloaded += View_Unloaded;
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as FusionBossDocumentViewModel);
        UpdateArenaPreviewSurface();
        await ReloadRuntimeTracesAsync();
    }

    private void View_Unloaded(object sender, RoutedEventArgs e)
    {
        CancelRuntimeTraceLoad();
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
        _viewModel = null;
    }

    private void View_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        AttachViewModel(args.NewValue as FusionBossDocumentViewModel);
        UpdateArenaPreviewSurface();
        if (IsLoaded)
        {
            _ = ReloadRuntimeTracesAsync();
        }
    }

    private void AttachViewModel(FusionBossDocumentViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
        ReleaseEmbeddedPreview();
        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FusionBossDocumentViewModel.IsArenaPreviewDetached))
        {
            UpdateArenaPreviewSurface();
        }
        else if (args.PropertyName == nameof(FusionBossDocumentViewModel.SelectedTimelineStep) &&
            _viewModel?.SelectedTimelineStep is { } selectedStep)
        {
            DispatcherQueue.TryEnqueue(() => TimelineListView.ScrollIntoView(selectedStep));
        }
    }

    private void UpdateArenaPreviewSurface()
    {
        var detached = _viewModel?.IsArenaPreviewDetached == true;
        EmbeddedArenaPreviewHost.Visibility = detached
            ? Visibility.Collapsed
            : Visibility.Visible;
        DetachedArenaPreviewPlaceholder.Visibility = detached
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (detached || _viewModel is null)
        {
            ReleaseEmbeddedPreview();
            return;
        }
        if (EmbeddedArenaPreviewHost.Children.Count == 0)
        {
            EmbeddedArenaPreviewHost.Children.Add(new FusionArenaPreviewView
            {
                DataContext = _viewModel,
            });
        }
    }

    private void ReleaseEmbeddedPreview()
    {
        if (EmbeddedArenaPreviewHost.Children.FirstOrDefault() is
            FusionArenaPreviewView preview)
        {
            preview.ReleaseRenderer();
        }
        EmbeddedArenaPreviewHost.Children.Clear();
    }

    private void OpenArenaPreviewWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }
        if (_viewModel.IsArenaPreviewDetached)
        {
            FusionArenaPreviewWindowCoordinator.Show(_viewModel);
        }
        else
        {
            FusionArenaPreviewWindowCoordinator.Detach(_viewModel);
        }
    }

    private void ShowDetachedArenaPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            FusionArenaPreviewWindowCoordinator.Show(_viewModel);
        }
    }

    private void AttachArenaPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            FusionArenaPreviewWindowCoordinator.Attach(_viewModel);
        }
    }

    private async void ConfigureArenaPlaytest_Click(object sender, RoutedEventArgs e) =>
        await ShowArenaPlaytestSetupAsync(launchAfterSave: false);

    private async void LaunchArenaPlaytest_Click(object sender, RoutedEventArgs e)
    {
        var viewModel = _viewModel;
        var arena = viewModel?.SelectedArena;
        var map = viewModel?.SelectedMap;
        if (viewModel is null || arena is null || map is null)
        {
            await ShowArenaPlaytestErrorAsync("Seleziona un'arena e una mappa prima di avviare il playtest.");
            return;
        }

        try
        {
            var settings = await _arenaPlaytestService.LoadSettingsAsync(viewModel.ProjectPath);
            var profileKey = ArenaPlaytestProfileKey(arena.Id, map.Scene.MapId);
            if (!settings.Profiles.TryGetValue(profileKey, out var profile) ||
                !File.Exists(settings.RuntimeExecutable))
            {
                await ShowArenaPlaytestSetupAsync(launchAfterSave: true);
                return;
            }
            await LaunchArenaPlaytestAsync(settings, profile);
        }
        catch (Exception exception)
        {
            await ShowArenaPlaytestErrorAsync(exception.Message);
        }
    }

    private async Task ShowArenaPlaytestSetupAsync(bool launchAfterSave)
    {
        var viewModel = _viewModel;
        var arena = viewModel?.SelectedArena;
        var selectedMap = viewModel?.SelectedMap;
        if (viewModel is null || arena is null || selectedMap is null)
        {
            await ShowArenaPlaytestErrorAsync("Seleziona un'arena e una mappa da configurare.");
            return;
        }

        try
        {
            var settings = await _arenaPlaytestService.LoadSettingsAsync(viewModel.ProjectPath);
            var profileKey = ArenaPlaytestProfileKey(arena.Id, selectedMap.Scene.MapId);
            settings.Profiles.TryGetValue(profileKey, out var profile);
            var runtimes = _arenaPlaytestService.DiscoverRuntimeExecutables().ToList();
            var catalog = await _arenaPlaytestService.LoadCatalogAsync(viewModel.ProjectPath);
            if (!string.IsNullOrWhiteSpace(settings.RuntimeExecutable) &&
                !runtimes.Contains(settings.RuntimeExecutable, StringComparer.OrdinalIgnoreCase))
            {
                runtimes.Insert(0, settings.RuntimeExecutable);
            }

            var mapOptions = arena.Maps
                .Select(CreatePlaytestMapOption)
                .OrderByDescending(option => option.MapId == selectedMap.Scene.MapId)
                .ThenBy(option => option.MapId)
                .ToArray();
            var dialog = new FusionArenaPlaytestSetupDialog(
                arena.Id,
                arena.DisplayName,
                mapOptions,
                runtimes,
                catalog,
                settings,
                profile,
                launchAfterSave)
            {
                XamlRoot = XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary ||
                dialog.Settings is null ||
                dialog.Profile is null)
            {
                return;
            }

            var savedProfileKey = ArenaPlaytestProfileKey(
                dialog.Profile.ArenaId,
                dialog.Profile.MapId);
            var profiles = new Dictionary<string, FusionArenaPlaytestProfile>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var pair in dialog.Settings.Profiles)
            {
                profiles[pair.Key] = pair.Value;
            }
            profiles[savedProfileKey] = dialog.Profile;
            var savedSettings = dialog.Settings with { Profiles = profiles };
            await _arenaPlaytestService.SaveSettingsAsync(viewModel.ProjectPath, savedSettings);
            ArenaPlaytestStatusTextBlock.Text =
                $"Setup salvato · {PlaytestModeText(dialog.Profile.Mode)} · mappa #{dialog.Profile.MapId:000}";
            if (launchAfterSave)
            {
                await LaunchArenaPlaytestAsync(savedSettings, dialog.Profile);
            }
        }
        catch (Exception exception)
        {
            await ShowArenaPlaytestErrorAsync(exception.Message);
        }
    }

    private async Task LaunchArenaPlaytestAsync(
        FusionArenaPlaytestSettings settings,
        FusionArenaPlaytestProfile profile)
    {
        var viewModel = _viewModel ??
            throw new InvalidOperationException("Il documento Fusion Boss Battle non è più disponibile.");
        var arena = viewModel.SelectedArena ??
            throw new InvalidOperationException("Nessuna arena selezionata.");

        LaunchArenaPlaytestButton.IsEnabled = false;
        ArenaPlaytestStatusTextBlock.Text = "Avvio del runtime RPG Maker MZ…";
        try
        {
            var launch = await _arenaPlaytestService.LaunchAsync(
                viewModel.ProjectPath,
                arena.EncounterId,
                settings,
                profile);
            ArenaPlaytestStatusTextBlock.Text =
                $"Playtest avviato · PID {launch.ProcessId} · {PlaytestModeText(profile.Mode)}";
        }
        finally
        {
            LaunchArenaPlaytestButton.IsEnabled = true;
        }
    }

    private async Task ShowArenaPlaytestErrorAsync(string message)
    {
        ArenaPlaytestStatusTextBlock.Text = "Avvio playtest non riuscito.";
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Playtest arena",
            Content = message,
            CloseButtonText = "Chiudi",
        };
        await dialog.ShowAsync();
    }

    private static FusionArenaPlaytestMapOption CreatePlaytestMapOption(
        FusionBossMapSceneViewModel mapViewModel)
    {
        var map = mapViewModel.Scene;
        var bossSpawn = map.Markers.FirstOrDefault(marker =>
            marker.Kind == FusionBossMapMarkerKind.Anchor &&
            marker.Id.Equals("bossSpawn", StringComparison.OrdinalIgnoreCase));
        var suggestedX = bossSpawn is null
            ? Math.Max(0, map.Width / 2)
            : (int)Math.Round(bossSpawn.X);
        var suggestedY = bossSpawn is null
            ? Math.Max(0, map.Height / 2)
            : (int)Math.Round(bossSpawn.Y) + 8;
        suggestedX = Math.Clamp(suggestedX, 0, Math.Max(0, map.Width - 1));
        suggestedY = Math.Clamp(suggestedY, 0, Math.Max(0, map.Height - 1));
        return new FusionArenaPlaytestMapOption(
            map.MapId,
            mapViewModel.DisplayName,
            map.Width,
            map.Height,
            suggestedX,
            suggestedY);
    }

    private static string ArenaPlaytestProfileKey(string arenaId, int mapId) =>
        $"{arenaId}@{mapId}";

    private static string PlaytestModeText(FusionArenaPlaytestLaunchMode mode) => mode switch
    {
        FusionArenaPlaytestLaunchMode.Direct => "battaglia diretta",
        FusionArenaPlaytestLaunchMode.Narrative => "ingresso narrativo",
        FusionArenaPlaytestLaunchMode.MapOnly => "sola mappa",
        _ => mode.ToString(),
    };

    private async void RefreshRuntimeTraces_Click(object sender, RoutedEventArgs e) =>
        await ReloadRuntimeTracesAsync();

    private void EditSelectedRuntimeDeclaration_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedRuntimeAttack is not null)
        {
            FusionModeTabs.SelectedIndex = 2;
            SetEditMode(true);
            StepEditorExpander.IsExpanded = true;
            DispatcherQueue.TryEnqueue(() => StepEditorTextBox.Focus(FocusState.Programmatic));
        }
    }

    private void OpenSelectedRuntimeInArena_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedRuntimeAttack is not null)
        {
            FusionModeTabs.SelectedIndex = 4;
        }
    }

    private void OpenSelectedExecutionTimeline_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.CanOpenSelectedExecutionTimeline == true)
        {
            FusionModeTabs.SelectedIndex = 2;
        }
    }

    private void ToggleFocusMode_Click(object sender, RoutedEventArgs e)
    {
        _isFocusMode = !_isFocusMode;
        ApplyFocusMode();
    }

    private void ApplyFocusMode()
    {
        EncounterPanel.Visibility = _isFocusMode
            ? Visibility.Collapsed
            : Visibility.Visible;
        EncounterPanelColumn.Width = new GridLength(_isFocusMode ? 0 : 260);
        UpdateWorkspaceSpacing();
        FocusModeButton.Content = _isFocusMode ? "Mostra contesto" : "Focus";
        ToolTipService.SetToolTip(
            FocusModeButton,
            _isFocusMode
                ? "Ripristina il selettore degli encounter"
                : "Nasconde il selettore degli encounter e amplia l'area di lavoro");
    }

    private void ToggleEditMode_Click(object sender, RoutedEventArgs e)
    {
        SetEditMode(!_isEditMode);
    }

    private void SetEditMode(bool enabled)
    {
        _isEditMode = enabled;
        if (_isEditMode && !_isFocusMode)
        {
            _isFocusMode = true;
            ApplyFocusMode();
        }
        EditorPanel.Visibility = _isEditMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        EditorPanelColumn.Width = new GridLength(_isEditMode ? 380 : 0);
        EditModeButton.Content = _isEditMode ? "Chiudi modifica" : "Modifica";
        UpdateWorkspaceSpacing();
    }

    private void UpdateWorkspaceSpacing() =>
        WorkspaceGrid.ColumnSpacing = _isFocusMode && !_isEditMode ? 0 : 18;

    private void ApplyPhaseEdits_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.ApplyPhaseEdits();

    private void ApplySequenceEdits_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.ApplySequenceEdits();

    private void ApplyStepJson_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.ApplyStepJson();

    private void UndoEditor_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.UndoEditorChange();

    private void RedoEditor_Click(object sender, RoutedEventArgs e) =>
        _viewModel?.RedoEditorChange();

    private async Task ReloadRuntimeTracesAsync()
    {
        CancelRuntimeTraceLoad();
        var viewModel = _viewModel;
        if (viewModel is null)
        {
            return;
        }
        var cancellation = new CancellationTokenSource();
        _runtimeTraceLoadCancellation = cancellation;
        try
        {
            var result = await _runtimeTraceService.LoadAsync(
                viewModel.ProjectPath,
                cancellation.Token);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(_viewModel, viewModel))
            {
                viewModel.ApplyRuntimeTraceLoadResult(result);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_viewModel, viewModel))
            {
                viewModel.ApplyRuntimeTraceLoadResult(new FusionRuntimeTraceLoadResult
                {
                    Errors = [$"Impossibile caricare i trace: {exception.Message}"],
                });
            }
        }
        finally
        {
            if (ReferenceEquals(_runtimeTraceLoadCancellation, cancellation))
            {
                _runtimeTraceLoadCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void CancelRuntimeTraceLoad()
    {
        _runtimeTraceLoadCancellation?.Cancel();
        _runtimeTraceLoadCancellation = null;
    }
}
