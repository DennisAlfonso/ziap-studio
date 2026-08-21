using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using ZiapStudio.ViewModels;

namespace ZiapStudio.Views;

internal sealed class FusionArenaPreviewWindow : Window
{
    private const int MinimumWidth = 720;
    private const int MinimumHeight = 480;
    private readonly FusionBossDocumentViewModel _viewModel;
    private readonly FusionArenaPreviewView _preview;

    public FusionArenaPreviewWindow(FusionBossDocumentViewModel viewModel)
    {
        _viewModel = viewModel;
        _preview = new FusionArenaPreviewView
        {
            DataContext = viewModel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        SystemBackdrop = new MicaBackdrop();
        Content = _preview;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Changed += AppWindow_Changed;
        Closed += Window_Closed;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateTitle();
        ApplyInitialBounds();
    }

    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter
            {
                State: OverlappedPresenterState.Minimized,
            } presenter)
        {
            presenter.Restore();
        }
        Activate();
    }

    private void ApplyInitialBounds()
    {
        var workArea = DisplayArea.GetFromWindowId(
            AppWindow.Id,
            DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(1400, Math.Max(MinimumWidth, workArea.Width - 120));
        var height = Math.Min(900, Math.Max(MinimumHeight, workArea.Height - 120));
        var x = workArea.X + (workArea.Width - width) / 2;
        var y = workArea.Y + (workArea.Height - height) / 2;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private static void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || sender.Presenter is not OverlappedPresenter
            {
                State: OverlappedPresenterState.Restored,
            })
        {
            return;
        }
        var width = Math.Max(MinimumWidth, sender.Size.Width);
        var height = Math.Max(MinimumHeight, sender.Size.Height);
        if (width != sender.Size.Width || height != sender.Size.Height)
        {
            sender.Resize(new SizeInt32(width, height));
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FusionBossDocumentViewModel.SelectedMap))
        {
            UpdateTitle();
        }
    }

    private void UpdateTitle()
    {
        Title = _viewModel.SelectedMap is { } map
            ? $"Arena Preview — {map.DisplayName}"
            : "Arena Preview — Fusion Boss Battle";
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        AppWindow.Changed -= AppWindow_Changed;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _preview.ReleaseRenderer();
    }
}

internal static class FusionArenaPreviewWindowCoordinator
{
    private static readonly Dictionary<FusionBossDocumentViewModel, FusionArenaPreviewWindow>
        Windows = [];

    public static void Detach(FusionBossDocumentViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (Windows.TryGetValue(viewModel, out var existing))
        {
            existing.BringToFront();
            return;
        }

        viewModel.PrepareArenaPreviewDetach();
        var window = new FusionArenaPreviewWindow(viewModel);
        EventHandler closeRequested = (_, _) => Attach(viewModel);
        viewModel.ExternalSurfacesCloseRequested += closeRequested;
        window.Closed += (_, _) =>
        {
            viewModel.ExternalSurfacesCloseRequested -= closeRequested;
            Windows.Remove(viewModel);
            if (viewModel.ReattachArenaPreviewOnWindowClose)
            {
                viewModel.SetArenaPreviewDetached(false);
            }
        };
        Windows.Add(viewModel, window);
        viewModel.SetArenaPreviewDetached(true);
        window.Activate();
    }

    public static void Show(FusionBossDocumentViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (Windows.TryGetValue(viewModel, out var window))
        {
            window.BringToFront();
        }
        else
        {
            Detach(viewModel);
        }
    }

    public static void Attach(FusionBossDocumentViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (Windows.TryGetValue(viewModel, out var window))
        {
            window.Close();
        }
        else
        {
            viewModel.SetArenaPreviewDetached(false);
        }
    }
}
