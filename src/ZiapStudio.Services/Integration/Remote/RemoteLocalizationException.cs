namespace ZiapStudio.Services.Integration.Remote;

public sealed class RemoteLocalizationException : Exception
{
    public RemoteLocalizationException(string message)
        : base(message)
    {
    }

    public RemoteLocalizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
