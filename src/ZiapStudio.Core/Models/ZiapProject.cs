using System.Text.Json.Serialization;

namespace ZiapStudio.Core.Models;

public sealed record ZiapProject
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? PackageName { get; init; }

    public string? Version { get; init; }

    public required string ProjectType { get; init; }

    public string? Publisher { get; init; }

    public required string Path { get; init; }

    public bool IsZiapInitialized { get; init; }

    [JsonIgnore]
    public string ProjectTypeDisplayName => KnownProjectTypes.GetDisplayName(ProjectType);
}
