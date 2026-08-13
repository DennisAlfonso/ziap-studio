using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Documents;

public sealed class DocumentService
{
    private readonly DocumentResolver _resolver;

    public DocumentService(DocumentResolver resolver)
    {
        _resolver = resolver;
    }

    public Task<StudioDocument> OpenAsync(
        ZiapProject project,
        DocumentDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(descriptor);

        return _resolver.Resolve(descriptor).OpenAsync(project, descriptor, cancellationToken);
    }
}
