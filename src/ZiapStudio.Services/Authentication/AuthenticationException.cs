namespace ZiapStudio.Services.Authentication;

public class AuthenticationException : Exception
{
    public AuthenticationException(string message)
        : base(message)
    {
    }

    public AuthenticationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

public sealed class FirebaseAuthenticationException : AuthenticationException
{
    public FirebaseAuthenticationException(
        string message,
        bool isCredentialRejected = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        IsCredentialRejected = isCredentialRejected;
    }

    public bool IsCredentialRejected { get; }
}
