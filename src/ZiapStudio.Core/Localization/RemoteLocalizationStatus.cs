namespace ZiapStudio.Core.Localization;

public enum RemoteLocalizationAlignment
{
    Unknown,
    Aligned,
    Different,
    MissingLocal,
    MissingRemote,
    Error,
}

public sealed record RemoteLocalizationManifest
{
    public required string ProjectId { get; init; }

    public string? Generation { get; init; }

    public DateTimeOffset? GeneratedAt { get; init; }

    public IReadOnlyList<RemoteLocalizationManifestFile> Files { get; init; } = [];
}

public sealed record RemoteLocalizationManifestFile
{
    public required string Locale { get; init; }

    public required string File { get; init; }

    public string? VersionId { get; init; }

    public string? Checksum { get; init; }

    public string? ChecksumAlgorithm { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public long? Size { get; init; }
}

public sealed record RemoteLocalizationFileStatus
{
    public required string Locale { get; init; }

    public required string File { get; init; }

    public required RemoteLocalizationAlignment Alignment { get; init; }

    public string? LocalPath { get; init; }

    public string? LocalChecksum { get; init; }

    public RemoteLocalizationManifestFile? Remote { get; init; }

    public string? Diagnostic { get; init; }
}

public sealed record RemoteLocalizationWorkspaceStatus
{
    public required string ProjectId { get; init; }

    public string? Generation { get; init; }

    public DateTimeOffset? GeneratedAt { get; init; }

    public IReadOnlyList<RemoteLocalizationFileStatus> Files { get; init; } = [];
}
