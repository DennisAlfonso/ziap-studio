using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Core.Integrations;

public interface IProjectIntegrationProvider
{
    string Id { get; }

    Task<bool> IsAvailableAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentDescriptor>> GetDocumentsAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default);
}
