namespace ZiapStudio.Services.Integration.Console;

public sealed class ConsoleDeepLinkBuilder
{
    private readonly Uri _baseUri;

    public ConsoleDeepLinkBuilder(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri ||
            baseUri.Scheme != Uri.UriSchemeHttp &&
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "ConsoleBaseUrl deve essere un URL HTTP o HTTPS assoluto.",
                nameof(baseUri));
        }

        _baseUri = baseUri;
    }

    public Uri Build(ConsoleNavigationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.ProjectId);

        var areaPath = target.Area switch
        {
            ConsoleNavigationArea.Localization => "localization",
            _ => throw new NotSupportedException(
                $"L'area Console '{target.Area}' non è ancora supportata."),
        };
        var route = string.Join(
            '/',
            _baseUri.AbsoluteUri.TrimEnd('/'),
            "dashboard/zenkaiverse-games/giochi",
            Uri.EscapeDataString(target.ProjectId),
            areaPath);
        var query = CreateQuery(
            ("source", target.Source),
            ("language", target.Language),
            ("file", target.File),
            ("focus", target.Focus));

        return new Uri(query.Length == 0 ? route : $"{route}?{query}");
    }

    private static string CreateQuery(params (string Name, string? Value)[] parameters) =>
        string.Join(
            '&',
            parameters
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
                .Select(parameter =>
                    $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value!)}"));
}
