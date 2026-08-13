namespace ZiapStudio.Core.Models;

public sealed class ProjectMetadata
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; }

    public string? Id { get; init; }

    public string? Name { get; init; }

    public string? Type { get; init; }

    public string? Version { get; init; }

    public string? Publisher { get; init; }
}
