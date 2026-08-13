using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using ZiapStudio.Platform.Windows;
using ZiapStudio.Services;
using ZiapStudio.Services.Assets;
using ZiapStudio.Services.Authentication;
using ZiapStudio.Services.Documents;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Initialization;
using ZiapStudio.Services.Integration.Console;
using ZiapStudio.Services.Integration.Remote;
using ZiapStudio.Services.Providers;
using ZiapStudio.ViewModels;

namespace ZiapStudio;

public sealed partial class MainWindow : Window
{
    private const int MinimumWindowWidth = 1100;
    private const int MinimumWindowHeight = 700;
    private readonly MainPage _mainPage;
    private readonly StudioLayoutSettingsService _layoutSettingsService;
    private RectInt32 _lastRestoredBounds;
    private bool _allowClose;
    private bool _closeConfirmationInProgress;

    public MainWindow(
        ProjectService projectService,
        ProjectInitializationService projectInitializationService,
        ProjectIdGenerator projectIdGenerator,
        ProjectProviderService projectProviderService,
        DocumentService documentService,
        AssetPreviewService assetPreviewService,
        DocumentEditSessionFactory editSessionFactory,
        DocumentSaveService documentSaveService,
        IRecentProjectService recentProjectService,
        WindowsShellService shellService,
        ConsoleIntegrationService consoleIntegrationService,
        RemoteLocalizationService remoteLocalizationService,
        PublishedLocalizationSyncService publishedLocalizationSyncService,
        ZiapAuthenticationService authenticationService,
        StudioLayoutSettingsService layoutSettingsService,
        StudioLayoutSettings layoutSettings)
    {
        InitializeComponent();

        _layoutSettingsService = layoutSettingsService;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ApplyWindowLayout(layoutSettings);

        var viewModel = new MainPageViewModel(
            projectService,
            projectInitializationService,
            projectIdGenerator,
            projectProviderService,
            documentService,
            assetPreviewService,
            editSessionFactory,
            documentSaveService,
            recentProjectService,
            new WindowsFolderPickerService(AppWindow.Id),
            shellService,
            consoleIntegrationService,
            remoteLocalizationService,
            publishedLocalizationSyncService,
            authenticationService);
        _mainPage = new MainPage(viewModel, layoutSettings);
        RootFrame.Content = _mainPage;
        AppWindow.Changed += AppWindow_Changed;
        AppWindow.Closing += AppWindow_Closing;
    }

    private void ApplyWindowLayout(StudioLayoutSettings settings)
    {
        var displayArea = settings is { WindowX: int savedX, WindowY: int savedY }
            ? DisplayArea.GetFromPoint(
                new PointInt32(savedX, savedY),
                DisplayAreaFallback.Primary)
            : DisplayArea.GetFromWindowId(
                AppWindow.Id,
                DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var width = Math.Min(
            Math.Max(MinimumWindowWidth, settings.WindowWidth),
            workArea.Width);
        var height = Math.Min(
            Math.Max(MinimumWindowHeight, settings.WindowHeight),
            workArea.Height);
        var x = settings.WindowX ?? workArea.X + ((workArea.Width - width) / 2);
        var y = settings.WindowY ?? workArea.Y + ((workArea.Height - height) / 2);
        x = Math.Clamp(x, workArea.X, workArea.X + workArea.Width - width);
        y = Math.Clamp(y, workArea.Y, workArea.Y + workArea.Height - height);
        _lastRestoredBounds = new RectInt32(x, y, width, height);
        AppWindow.MoveAndResize(_lastRestoredBounds);

        if (settings.IsMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (sender.Presenter is not OverlappedPresenter
            {
                State: OverlappedPresenterState.Restored,
            })
        {
            return;
        }

        var workArea = DisplayArea.GetFromWindowId(
            sender.Id,
            DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(
            Math.Max(MinimumWindowWidth, sender.Size.Width),
            workArea.Width);
        var height = Math.Min(
            Math.Max(MinimumWindowHeight, sender.Size.Height),
            workArea.Height);
        if (width != sender.Size.Width || height != sender.Size.Height)
        {
            sender.Resize(new SizeInt32(width, height));
        }

        _lastRestoredBounds = new RectInt32(
            sender.Position.X,
            sender.Position.Y,
            width,
            height);
    }

    private async void AppWindow_Closing(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        if (_mainPage.ViewModel.IsBusy)
        {
            return;
        }

        if (_closeConfirmationInProgress)
        {
            return;
        }

        _closeConfirmationInProgress = true;
        try
        {
            if (_mainPage.ViewModel.DirtyDocuments.Count > 0 &&
                !await _mainPage.ConfirmCloseDirtyDocumentsAsync())
            {
                return;
            }

            await SaveLayoutAsync();
            _allowClose = true;
            Close();
        }
        finally
        {
            _closeConfirmationInProgress = false;
        }
    }

    private async Task SaveLayoutAsync()
    {
        var isMaximized = AppWindow.Presenter is OverlappedPresenter
        {
            State: OverlappedPresenterState.Maximized,
        };
        var pageLayout = _mainPage.CaptureLayoutSettings();
        var settings = pageLayout with
        {
            WindowX = _lastRestoredBounds.X,
            WindowY = _lastRestoredBounds.Y,
            WindowWidth = _lastRestoredBounds.Width,
            WindowHeight = _lastRestoredBounds.Height,
            IsMaximized = isMaximized,
        };

        try
        {
            await _layoutSettingsService.SaveAsync(settings);
        }
        catch (SettingsException)
        {
            // Il layout non deve mai impedire la chiusura dell'applicazione.
        }
    }
}
