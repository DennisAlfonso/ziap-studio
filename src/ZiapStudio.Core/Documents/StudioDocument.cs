namespace ZiapStudio.Core.Documents;

public abstract record StudioDocument
{
    public DocumentDescriptor Descriptor { get; init; } = new();
}
