using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Integrations;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Integrations;

public sealed class FusionWorldIntegrationProvider : IProjectIntegrationProvider
{
    public const string ResourceUri = "fusionworld://workspace/";

    private readonly FileSystemService _fileSystem;

    public FusionWorldIntegrationProvider(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public string Id => "fusion-world-navigation";

    public Task<bool> IsAvailableAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default) => Task.FromResult(
            _fileSystem.FileExists(Path.Combine(project.Path, "data", "Tilesets.json")));

    public async Task<IReadOnlyList<DocumentDescriptor>> GetDocumentsAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAvailableAsync(project, cancellationToken))
        {
            return [];
        }

        return
        [
            new DocumentDescriptor
            {
                Id = new DocumentId($"{project.Id}:integration:fusion-world"),
                DisplayName = "World & Navigation",
                Kind = DocumentKind.ProjectIntegration,
                ResourceId = new Uri(ResourceUri),
                SourcePath = Path.Combine(project.Path, "data"),
            },
        ];
    }
}
