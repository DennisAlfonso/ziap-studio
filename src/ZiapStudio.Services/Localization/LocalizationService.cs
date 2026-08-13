namespace ZiapStudio.Services.Localization;

public sealed class LocalizationService
{
    public const string DefaultLocale = "it";

    private readonly IReadOnlyList<ILocalizationProvider> _providers;

    public LocalizationService(IEnumerable<ILocalizationProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public async Task<LocalizationResolution?> ResolveAsync(
        string projectPath,
        string rawValue,
        string locale = DefaultLocale,
        CancellationToken cancellationToken = default)
    {
        var provider = _providers.FirstOrDefault(candidate => candidate.CanResolve(rawValue));
        return provider is null
            ? null
            : await provider.ResolveAsync(
                projectPath,
                locale,
                rawValue,
                cancellationToken);
    }
}
