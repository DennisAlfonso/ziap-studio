namespace ZiapStudio.Services;

public sealed class SettingsException : Exception
{
    public SettingsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
