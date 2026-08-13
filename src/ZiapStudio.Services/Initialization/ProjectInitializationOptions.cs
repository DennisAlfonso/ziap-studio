namespace ZiapStudio.Services.Initialization;

public sealed class ProjectInitializationOptions
{
    public string Name { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string? Version { get; init; }

    public string? Publisher { get; init; }
}
