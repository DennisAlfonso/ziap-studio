using ZiapStudio.Core.Models;
using ZiapStudio.Core.Documents;

namespace ZiapStudio.ViewModels;

public sealed class ProjectExplorerItemViewModel
{
    private ProjectExplorerItemViewModel(ProjectExplorerNode node, bool isRoot)
    {
        Name = node.Name;
        Path = node.Path;
        Document = node.Document;
        IconGlyph = GetIconGlyph(node.Kind);
        IsExpanded = isRoot && node.Kind != ProjectExplorerNodeKind.File;
        Children = node.Children
            .Select(child => new ProjectExplorerItemViewModel(child, isRoot: false))
            .ToArray();
    }

    public string Name { get; }

    public string? Path { get; }

    public DocumentDescriptor? Document { get; }

    public string IconGlyph { get; }

    public bool IsExpanded { get; }

    public IReadOnlyList<ProjectExplorerItemViewModel> Children { get; }

    public static ProjectExplorerItemViewModel FromRoot(ProjectExplorerNode node) =>
        new(node, isRoot: true);

    private static string GetIconGlyph(ProjectExplorerNodeKind kind) => kind switch
    {
        ProjectExplorerNodeKind.Category => "\uE8B7",
        ProjectExplorerNodeKind.Database => "\uE8F1",
        ProjectExplorerNodeKind.Map => "\uE707",
        ProjectExplorerNodeKind.Plugin => "\uE74C",
        ProjectExplorerNodeKind.Asset => "\uEB9F",
        ProjectExplorerNodeKind.Directory => "\uE8B7",
        ProjectExplorerNodeKind.Information => "\uE946",
        _ => "\uE8A5",
    };
}
