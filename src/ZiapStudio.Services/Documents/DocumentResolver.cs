using ZiapStudio.Core.Documents;

namespace ZiapStudio.Services.Documents;

public sealed class DocumentResolver
{
    private readonly IReadOnlyList<IDocumentProvider> _providers;

    public DocumentResolver(IEnumerable<IDocumentProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public IDocumentProvider Resolve(DocumentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return _providers.FirstOrDefault(provider => provider.CanOpen(descriptor))
            ?? throw new DocumentLoadException(
                $"Nessun provider può aprire la risorsa '{descriptor.ResourceId}'.");
    }
}
