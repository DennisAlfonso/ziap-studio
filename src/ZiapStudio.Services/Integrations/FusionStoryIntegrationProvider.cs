using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Integrations;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Integrations;

public sealed class FusionStoryIntegrationProvider : IProjectIntegrationProvider
{
    public const string ResourceUri = "fusionstory://workspace/";

    private readonly FileSystemService _fileSystem;

    public FusionStoryIntegrationProvider(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public string Id => "fusion-story-events";

    public Task<bool> IsAvailableAsync(ZiapProject project, CancellationToken cancellationToken = default) =>
        Task.FromResult(_fileSystem.FileExists(Path.Combine(project.Path, "data", "MapInfos.json")) ||
                        _fileSystem.FileExists(Path.Combine(project.Path, "data", "CommonEvents.json")));

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
                Id = new DocumentId($"{project.Id}:integration:fusion-story"),
                DisplayName = "Story & Events",
                Kind = DocumentKind.ProjectIntegration,
                ResourceId = new Uri(ResourceUri),
                SourcePath = Path.Combine(project.Path, "data"),
            },
        ];
    }
}
