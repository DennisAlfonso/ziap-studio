using ZiapStudio.Services.Integration.Console;

namespace ZiapStudio.Services.Tests;

public sealed class ConsoleIntegrationTests
{
    [Fact]
    public void CreateDeepLink_BuildsPreciseLocalizationRoute()
    {
        var service = new ConsoleIntegrationService(
            new ConsoleDeepLinkBuilder(new Uri("https://ziap.zenkaiverse.net")),
            new RecordingUriLauncher());

        var uri = service.CreateDeepLink(new ConsoleNavigationTarget(
            ProjectId: "fusion-hexella-dive",
            Area: ConsoleNavigationArea.Localization,
            Language: "it",
            File: "db.json",
            Focus: "1.viaggiamondi"));

        Assert.Equal(
            "https://ziap.zenkaiverse.net/dashboard/zenkaiverse-games/giochi/" +
            "fusion-hexella-dive/localization?source=studio&language=it&file=db.json&focus=1.viaggiamondi",
            uri.AbsoluteUri);
    }

    [Fact]
    public void CreateDeepLink_SupportsAnyLocalizationNamespaceAndEncodesValues()
    {
        var builder = new ConsoleDeepLinkBuilder(new Uri("http://localhost:4200/"));

        var uri = builder.Build(new ConsoleNavigationTarget(
            ProjectId: "fusion hexella/dive",
            Area: ConsoleNavigationArea.Localization,
            Language: "it-IT",
            File: "main.json",
            Focus: "4.strings.2.items.weapon name"));

        Assert.Equal(
            "http://localhost:4200/dashboard/zenkaiverse-games/giochi/" +
            "fusion%20hexella%2Fdive/localization?source=studio&language=it-IT&file=main.json&focus=4.strings.2.items.weapon%20name",
            uri.AbsoluteUri);
    }

    [Fact]
    public void Open_LaunchesTheBuiltUriWithoutAuthenticationData()
    {
        var launcher = new RecordingUriLauncher();
        var service = new ConsoleIntegrationService(
            new ConsoleDeepLinkBuilder(new Uri("https://ziap.zenkaiverse.net")),
            launcher);
        var target = new ConsoleNavigationTarget(
            "fusion-hexella-dive",
            ConsoleNavigationArea.Localization,
            Language: "it",
            File: "main.json",
            Focus: "0.weapon");

        var uri = service.Open(target);

        Assert.Same(uri, launcher.OpenedUri);
        Assert.DoesNotContain("token", uri.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_RejectsAreasThatAreNotImplementedYet()
    {
        var builder = new ConsoleDeepLinkBuilder(new Uri("https://ziap.zenkaiverse.net"));

        Assert.Throws<NotSupportedException>(() => builder.Build(
            new ConsoleNavigationTarget(
                "fusion-hexella-dive",
                ConsoleNavigationArea.Missions)));
    }

    private sealed class RecordingUriLauncher : IExternalUriLauncher
    {
        public Uri? OpenedUri { get; private set; }

        public void Open(Uri uri) => OpenedUri = uri;
    }
}
