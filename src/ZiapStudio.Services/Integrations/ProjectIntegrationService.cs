using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Integrations;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Integrations;

public sealed class ProjectIntegrationService
{
    private readonly IReadOnlyList<IProjectIntegrationProvider> _providers;

    public ProjectIntegrationService(IEnumerable<IProjectIntegrationProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public async Task<IReadOnlyList<DocumentDescriptor>> GetDocumentsAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        var documents = new List<DocumentDescriptor>();
        foreach (var provider in _providers)
        {
            if (await provider.IsAvailableAsync(project, cancellationToken))
            {
                documents.AddRange(await provider.GetDocumentsAsync(project, cancellationToken));
            }
        }

        return documents
            .DistinctBy(document => document.Id)
            .OrderBy(document => document.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<ProjectExplorerNode>> AddExplorerNodesAsync(
        ZiapProject project,
        IReadOnlyList<ProjectExplorerNode> roots,
        CancellationToken cancellationToken = default)
    {
        var documents = await GetDocumentsAsync(project, cancellationToken);
        if (documents.Count == 0)
        {
            return roots;
        }

        var result = roots.ToList();
        var systemIndex = result.FindIndex(node =>
            node.Name.Equals("System", StringComparison.OrdinalIgnoreCase));
        if (systemIndex < 0)
        {
            return roots;
        }

        var system = result[systemIndex];
        var systemChildren = system.Children.ToList();
        var pluginIndex = systemChildren.FindIndex(node =>
            node.Name.Equals("Plugin", StringComparison.OrdinalIgnoreCase));
        var integrationNodes = documents.Select(document => new ProjectExplorerNode
        {
            Name = document.DisplayName,
            Kind = ProjectExplorerNodeKind.Integration,
            Path = document.SourcePath ?? project.Path,
            Document = document,
        }).ToArray();

        if (pluginIndex < 0)
        {
            systemChildren.Insert(0, new ProjectExplorerNode
            {
                Name = "Plugin",
                Kind = ProjectExplorerNodeKind.Category,
                Path = Path.Combine(project.Path, "js", "plugins"),
                Children = integrationNodes,
            });
        }
        else
        {
            var pluginNode = systemChildren[pluginIndex];
            systemChildren[pluginIndex] = pluginNode with
            {
                Children = pluginNode.Children.Concat(integrationNodes).ToArray(),
            };
        }

        result[systemIndex] = system with { Children = systemChildren };
        return result;
    }
}
