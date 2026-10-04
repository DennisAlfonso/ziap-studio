using ZiapStudio.Core.Localization;

namespace ZiapStudio.Services.Localization;

/// <summary>
/// Resolves one or more localization tokens embedded in an RPG Maker string while
/// retaining the exact raw text and leaving all RPG Maker escape codes untouched.
/// </summary>
public sealed class CompositeLocalizationTextResolver
{
    private readonly LocalizationService _localizationService;

    public CompositeLocalizationTextResolver(LocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    public async Task<CompositeLocalizationText> ResolveAsync(
        string projectPath,
        string rawText,
        string locale = LocalizationService.DefaultLocale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        var display = new System.Text.StringBuilder(rawText.Length);
        var tokens = new List<CompositeLocalizationToken>();
        var position = 0;
        while (position < rawText.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var opening = rawText.IndexOf('{', position);
            if (opening < 0)
            {
                display.Append(rawText, position, rawText.Length - position);
                break;
            }

            display.Append(rawText, position, opening - position);
            var closing = rawText.IndexOf('}', opening + 1);
            if (closing < 0)
            {
                display.Append(rawText, opening, rawText.Length - opening);
                break;
            }

            var rawToken = rawText[opening..(closing + 1)];
            var resolution = await _localizationService.ResolveAsync(
                projectPath,
                rawToken,
                locale,
                cancellationToken);
            if (resolution is { ResolvedValue: { } resolvedValue })
            {
                display.Append(resolvedValue);
                tokens.Add(new CompositeLocalizationToken
                {
                    RawToken = rawToken,
                    DisplayValue = resolvedValue,
                    IsResolved = true,
                    Origin = resolution.Origin,
                });
            }
            else
            {
                display.Append(rawToken);
                if (resolution is not null)
                {
                    tokens.Add(new CompositeLocalizationToken
                    {
                        RawToken = rawToken,
                        DisplayValue = rawToken,
                        IsResolved = false,
                        Origin = resolution.Origin,
                    });
                }
            }

            position = closing + 1;
        }

        return new CompositeLocalizationText
        {
            RawText = rawText,
            DisplayText = display.ToString(),
            Tokens = tokens,
        };
    }
}

public sealed record CompositeLocalizationText
{
    public required string RawText { get; init; }
    public required string DisplayText { get; init; }
    public IReadOnlyList<CompositeLocalizationToken> Tokens { get; init; } = [];
    public IReadOnlyList<LocalizationReferenceOrigin> Origins => Tokens
        .Where(token => token.Origin is not null)
        .Select(token => token.Origin!)
        .ToArray();
}

public sealed record CompositeLocalizationToken
{
    public required string RawToken { get; init; }
    public required string DisplayValue { get; init; }
    public required bool IsResolved { get; init; }
    public LocalizationReferenceOrigin? Origin { get; init; }
}
