using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using ZiapStudio.Services;
using ZiapStudio.Services.Fusion.Bosses;
using ZiapStudio.Services.Integrations;
using ZiapStudio.ViewModels;

namespace ZiapStudio.Views;

internal sealed class FusionBossDocumentationWindow : Window
{
    private const int MinimumWidth = 960;
    private const int MinimumHeight = 620;

    public FusionBossDocumentationWindow(FusionBossDocumentationViewModel viewModel)
    {
        SystemBackdrop = new MicaBackdrop();
        Content = new FusionBossDocumentationView(viewModel);
        Title = "ZIAP Studio Docs — Fusion Boss Battle Engine";
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Changed += AppWindow_Changed;
        Closed += Window_Closed;
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
        var width = Math.Min(1460, Math.Max(MinimumWidth, workArea.Width - 120));
        var height = Math.Min(920, Math.Max(MinimumHeight, workArea.Height - 120));
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

    private void Window_Closed(object sender, WindowEventArgs args) =>
        AppWindow.Changed -= AppWindow_Changed;
}

internal static class FusionBossDocumentationWindowCoordinator
{
    private static readonly Dictionary<string, FusionBossDocumentationWindow> Windows =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Opening = new(StringComparer.OrdinalIgnoreCase);

    public static async Task ShowAsync(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var normalizedPath = Path.GetFullPath(projectPath);
        if (Windows.TryGetValue(normalizedPath, out var existing))
        {
            existing.BringToFront();
            return;
        }
        if (!Opening.Add(normalizedPath))
        {
            return;
        }

        try
        {
            var fileSystem = new FileSystemService();
            var service = new FusionBossActionApiService(
                fileSystem,
                new RpgMakerPluginRegistryService(fileSystem));
            var documentation = await service.LoadAsync(normalizedPath);
            var window = new FusionBossDocumentationWindow(
                new FusionBossDocumentationViewModel(documentation));
            window.Closed += (_, _) => Windows.Remove(normalizedPath);
            Windows.Add(normalizedPath, window);
            window.Activate();
        }
        finally
        {
            Opening.Remove(normalizedPath);
        }
    }
}
