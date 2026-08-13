namespace ZiapStudio.Core.Localization;

public enum LocalizationDifferenceKind
{
    Modified,
    OnlyLocal,
    OnlyPublished,
}

public sealed record LocalizationJsonDifference
{
    public required string Path { get; init; }

    public required LocalizationDifferenceKind Kind { get; init; }

    public string? LocalValue { get; init; }

    public string? PublishedValue { get; init; }

    public string KindLabel => Kind switch
    {
        LocalizationDifferenceKind.Modified => "MODIFICATO",
        LocalizationDifferenceKind.OnlyLocal => "SOLO LOCALE",
        LocalizationDifferenceKind.OnlyPublished => "SOLO REMOTO",
        _ => "DIFFERENTE",
    };

    public string LocalDisplayValue => LocalValue ?? "—";

    public string PublishedDisplayValue => PublishedValue ?? "—";
}

public sealed record PublishedLocalizationFile
{
    public required string ProjectId { get; init; }

    public required string Locale { get; init; }

    public required string File { get; init; }

    public required string VersionId { get; init; }

    public string? Checksum { get; init; }

    public string? ChecksumAlgorithm { get; init; }

    public required byte[] Contents { get; init; }
}

public sealed record PublishedLocalizationComparison
{
    public required string ProjectId { get; init; }

    public required string Locale { get; init; }

    public required string File { get; init; }

    public required string VersionId { get; init; }

    public required string DestinationPath { get; init; }

    public required bool LocalFileExists { get; init; }

    public string? LocalContentHash { get; init; }

    public IReadOnlyList<LocalizationJsonDifference> Differences { get; init; } = [];

    public int ModifiedCount => Differences.Count(item =>
        item.Kind == LocalizationDifferenceKind.Modified);

    public int OnlyLocalCount => Differences.Count(item =>
        item.Kind == LocalizationDifferenceKind.OnlyLocal);

    public int OnlyPublishedCount => Differences.Count(item =>
        item.Kind == LocalizationDifferenceKind.OnlyPublished);
}

public sealed record PublishedLocalizationSyncResult(
    string DestinationPath,
    string VersionId,
    bool Created);
