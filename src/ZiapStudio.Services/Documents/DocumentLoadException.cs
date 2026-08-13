namespace ZiapStudio.Services.Documents;

public sealed class DocumentLoadException : Exception
{
    public DocumentLoadException(string message)
        : base(message)
    {
    }

    public DocumentLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
