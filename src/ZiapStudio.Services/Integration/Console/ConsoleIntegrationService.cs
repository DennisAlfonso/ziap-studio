namespace ZiapStudio.Services.Integration.Console;

public interface IExternalUriLauncher
{
    void Open(Uri uri);
}

public sealed class ConsoleIntegrationService
{
    private readonly ConsoleDeepLinkBuilder _deepLinkBuilder;
    private readonly IExternalUriLauncher _uriLauncher;

    public ConsoleIntegrationService(
        ConsoleDeepLinkBuilder deepLinkBuilder,
        IExternalUriLauncher uriLauncher)
    {
        ArgumentNullException.ThrowIfNull(deepLinkBuilder);
        ArgumentNullException.ThrowIfNull(uriLauncher);
        _deepLinkBuilder = deepLinkBuilder;
        _uriLauncher = uriLauncher;
    }

    public Uri CreateDeepLink(ConsoleNavigationTarget target) =>
        _deepLinkBuilder.Build(target);

    public Uri Open(ConsoleNavigationTarget target)
    {
        var uri = CreateDeepLink(target);
        _uriLauncher.Open(uri);
        return uri;
    }
}
