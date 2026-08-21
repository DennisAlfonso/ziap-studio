using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiapStudio.Core.Fusion.Bosses;
using ZiapStudio.Services.Fusion.Bosses;
using ZiapStudio.ViewModels;

namespace ZiapStudio.Views;

public sealed partial class FusionBossDocumentView : UserControl
{
    private FusionBossDocumentViewModel? _viewModel;
    private readonly FusionRuntimeTraceService _runtimeTraceService = new();
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

    private async void RefreshRuntimeTraces_Click(object sender, RoutedEventArgs e) =>
        await ReloadRuntimeTracesAsync();

    private void OpenSelectedRuntimeInSequence_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedRuntimeAttack is not null)
        {
            FusionModeTabs.SelectedIndex = 2;
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
        _isEditMode = !_isEditMode;
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
