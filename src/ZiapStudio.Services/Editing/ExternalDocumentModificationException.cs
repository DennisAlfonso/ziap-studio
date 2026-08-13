namespace ZiapStudio.Services.Editing;

public sealed class ExternalDocumentModificationException : IOException
{
    public ExternalDocumentModificationException(string message)
        : base(message)
    {
    }
}
