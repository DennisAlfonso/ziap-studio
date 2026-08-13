using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Localization;

namespace ZiapStudio.Services.Localization;

public interface ILocalizationProvider
{
    bool CanResolve(string rawValue);

    Task<LocalizationResolution> ResolveAsync(
        string projectPath,
        string locale,
        string rawValue,
        CancellationToken cancellationToken = default);
}

public sealed record LocalizationResolution
{
    public string RawValue { get; init; } = string.Empty;

    public string? ResolvedValue { get; init; }

    public string Target { get; init; } = string.Empty;

    public string Locale { get; init; } = string.Empty;

    public LocalizationReferenceOrigin? Origin { get; init; }

    public RpgMakerResolutionStatus Status { get; init; }
}
