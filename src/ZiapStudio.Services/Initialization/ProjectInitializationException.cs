namespace ZiapStudio.Services.Initialization;

public sealed class ProjectInitializationException : Exception
{
    public ProjectInitializationException(string message)
        : base(message)
    {
    }

    public ProjectInitializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
