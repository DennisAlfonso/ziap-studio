namespace ZiapStudio.Core.Documents;

public sealed record DocumentDescriptor
{
    public DocumentId Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public DocumentKind Kind { get; init; }

    public Uri ResourceId { get; init; } = new("ziap://document/unknown");

    public string? SourcePath { get; init; }
}
