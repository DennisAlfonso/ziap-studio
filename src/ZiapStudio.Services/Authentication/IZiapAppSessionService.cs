namespace ZiapStudio.Services.Authentication;

public interface IZiapAppSessionService
{
    Task<ZiapApplicationSessionResult> CompleteAsync(
        string temporaryFirebaseIdToken,
        string expectedUid,
        CancellationToken cancellationToken = default);
}
