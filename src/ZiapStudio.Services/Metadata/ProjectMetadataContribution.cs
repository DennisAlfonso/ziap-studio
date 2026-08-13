namespace ZiapStudio.Services.Metadata;

internal sealed record ProjectMetadataContribution
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public string? PackageName { get; init; }

    public string? Version { get; init; }

    public string? ProjectType { get; init; }

    public string? Publisher { get; init; }
}
