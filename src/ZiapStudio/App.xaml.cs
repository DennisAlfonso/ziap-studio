using Microsoft.UI.Xaml;
using ZiapStudio.Platform.Windows;
using ZiapStudio.Services;
using ZiapStudio.Services.Assets;
using ZiapStudio.Services.Authentication;
using ZiapStudio.Services.Documents;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Fusion.Preflight;
using ZiapStudio.Services.Fusion.Preflight.Weapons;
using ZiapStudio.Services.Fusion.Weapons;
using ZiapStudio.Services.Initialization;
using ZiapStudio.Services.Integration.Console;
using ZiapStudio.Services.Integration.Remote;
using ZiapStudio.Services.Providers;

namespace ZiapStudio;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var fileSystem = new FileSystemService();
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Zenkaiverse",
            "ZIAP Studio");
        var settingsPath = Path.Combine(settingsDirectory, "settings.json");
        var layoutSettingsService = new StudioLayoutSettingsService(
            fileSystem,
            Path.Combine(settingsDirectory, "layout.json"));
        var layoutSettings = await layoutSettingsService.LoadAsync();
        var projectIdGenerator = new ProjectIdGenerator();
        var documentResolver = new DocumentResolver(
        [
            new RpgMakerDatabaseDocumentProvider(fileSystem),
        ]);
        var assetPreviewService = new AssetPreviewService(
            new AssetResolver(new RpgMakerAssetProvider(fileSystem)),
            new RpgMakerAssetPreviewProvider(fileSystem));
        var snapshotService = new DocumentSnapshotService(fileSystem);
        var documentSaveService = new DocumentSaveService(
            new DocumentValidationService(),
            new ExternalModificationDetector(fileSystem, snapshotService),
            new AtomicJsonFileWriter(fileSystem),
            snapshotService,
            new JsonTextPatchSerializer(fileSystem));
        var shellService = new WindowsShellService();
        var consoleIntegrationService = new ConsoleIntegrationService(
            new ConsoleDeepLinkBuilder(GetConsoleBaseUri()),
            shellService);
        var authenticationService = new ZiapAuthenticationService(
            new ZiapBrowserAuthorizationService(
                new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
                shellService,
                GetOAuthAuthorizeUri(),
                GetOAuthTokenUri()),
            new FirebaseTokenService(
                new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
                GetFirebaseWebApiKey()),
            new WindowsCredentialStore(
                "Zenkaiverse.ZiapStudio.FirebaseAuthentication.v1"));
        var remoteClient = new HttpRemoteLocalizationClient(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            GetRemoteLocalizationManifestUri(),
            GetRemoteLocalizationFileUri(),
            authenticationService,
            GetDevelopmentRemoteApiToken());
        var remoteLocalizationService = new RemoteLocalizationService(
            fileSystem,
            remoteClient);
        var publishedLocalizationSyncService = new PublishedLocalizationSyncService(
            fileSystem,
            remoteClient,
            new LocalizationJsonDiffService(),
            new PublishedLocalizationFileWriter(
                fileSystem,
                new AtomicJsonFileWriter(fileSystem)));
        var preflightScanner = new PreflightScanner(
        [
            new WeaponPreflightProvider(
                fileSystem,
                new WeaponNotetagCatalogProvider(fileSystem)),
        ]);
        var preflightSuppressionStore = new PreflightSuppressionStore(
            fileSystem,
            new AtomicJsonFileWriter(fileSystem));

        _window = new MainWindow(
            new ProjectService(fileSystem),
            new ProjectInitializationService(fileSystem, projectIdGenerator),
            projectIdGenerator,
            new ProjectProviderService(fileSystem),
            new DocumentService(documentResolver),
            assetPreviewService,
            new DocumentEditSessionFactory(),
            documentSaveService,
            new RecentProjectService(fileSystem, settingsPath),
            shellService,
            consoleIntegrationService,
            remoteLocalizationService,
            publishedLocalizationSyncService,
            authenticationService,
            preflightScanner,
            preflightSuppressionStore,
            layoutSettingsService,
            layoutSettings);
        _window.Activate();
    }

    private static Uri GetConsoleBaseUri()
    {
        var configuredBaseUrl = Environment.GetEnvironmentVariable("ZIAP_CONSOLE_BASE_URL");
        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            configuredBaseUrl = "https://ziap.zenkaiverse.net";
        }

        return new Uri(configuredBaseUrl, UriKind.Absolute);
    }

    private static Uri GetRemoteLocalizationManifestUri()
    {
        var configuredUrl = Environment.GetEnvironmentVariable(
            "ZIAP_REMOTE_LOCALIZATION_MANIFEST_URL");
        if (string.IsNullOrWhiteSpace(configuredUrl))
        {
            configuredUrl =
                "https://europe-west1-myzenkai-c58ee.cloudfunctions.net/" +
                "getLocalizationPublishedManifest";
        }

        return new Uri(configuredUrl, UriKind.Absolute);
    }

    private static Uri GetRemoteLocalizationFileUri() => GetConfiguredUri(
        "ZIAP_REMOTE_LOCALIZATION_FILE_URL",
        "https://europe-west1-myzenkai-c58ee.cloudfunctions.net/" +
        "getLocalizationPublishedFile");

    private static Uri GetOAuthAuthorizeUri() => GetConfiguredUri(
        "ZIAP_OAUTH_AUTHORIZE_URL",
        "https://europe-west1-myzenkai-c58ee.cloudfunctions.net/oauthAuthorize");

    private static Uri GetOAuthTokenUri() => GetConfiguredUri(
        "ZIAP_OAUTH_TOKEN_URL",
        "https://europe-west1-myzenkai-c58ee.cloudfunctions.net/oauthToken");

    private static string GetFirebaseWebApiKey()
    {
        var configured = Environment.GetEnvironmentVariable("ZIAP_FIREBASE_WEB_API_KEY");
        return string.IsNullOrWhiteSpace(configured)
            ? "AIzaSyAySkC0yy8I_szZ5ReTWHa_WkFlBK4Yuew"
            : configured.Trim();
    }

    private static string? GetDevelopmentRemoteApiToken()
    {
#if DEBUG
        return Environment.GetEnvironmentVariable("ZIAP_REMOTE_API_TOKEN");
#else
        return null;
#endif
    }

    private static Uri GetConfiguredUri(string variableName, string defaultValue)
    {
        var configured = Environment.GetEnvironmentVariable(variableName);
        return new Uri(
            string.IsNullOrWhiteSpace(configured) ? defaultValue : configured,
            UriKind.Absolute);
    }
}
