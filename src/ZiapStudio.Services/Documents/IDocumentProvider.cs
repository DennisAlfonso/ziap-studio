using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Documents;

public interface IDocumentProvider
{
    bool CanOpen(DocumentDescriptor descriptor);

    Task<StudioDocument> OpenAsync(
        ZiapProject project,
        DocumentDescriptor descriptor,
        CancellationToken cancellationToken = default);
}
