using ZiapStudio.Core.Documents;

namespace ZiapStudio.Core.Models;

public sealed record ProjectExplorerNode
{
    public string Name { get; init; } = string.Empty;

    public ProjectExplorerNodeKind Kind { get; init; }

    public string? Path { get; init; }

    public DocumentDescriptor? Document { get; init; }

    public IReadOnlyList<ProjectExplorerNode> Children { get; init; } = [];
}

public enum ProjectExplorerNodeKind
{
    Category,
    Database,
    Map,
    Plugin,
    Integration,
    Asset,
    Directory,
    File,
    Information,
}
