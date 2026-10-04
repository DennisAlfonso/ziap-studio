namespace ZiapStudio.Core.Localization;

public sealed record LocalizationReferenceOrigin
{
    public required string Namespace { get; init; }

    public required string Path { get; init; }

    public required string Locale { get; init; }

    public required string SourceFile { get; init; }

    /// <summary>Resolved scalar observed while building the Story projection.</summary>
    public string? ResolvedValue { get; init; }

    /// <summary>
    /// Typed route used for safe mutations. Path remains the human-readable form
    /// for the UI and deep links, but is deliberately not reparsed by writers.
    /// </summary>
    public IReadOnlyList<LocalizationPathSegment> Segments { get; init; } = [];
}

public sealed record LocalizationPathSegment
{
    public string? PropertyName { get; init; }

    public int? ArrayIndex { get; init; }

    public bool IsProperty => PropertyName is not null;

    public static LocalizationPathSegment Property(string name) => new()
    {
        PropertyName = name,
    };

    public static LocalizationPathSegment Index(int index) => new()
    {
        ArrayIndex = index,
    };
}
