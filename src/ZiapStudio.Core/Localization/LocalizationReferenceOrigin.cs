namespace ZiapStudio.Core.Localization;

public sealed record LocalizationReferenceOrigin
{
    public required string Namespace { get; init; }

    public required string Path { get; init; }

    public required string Locale { get; init; }

    public required string SourceFile { get; init; }
}
