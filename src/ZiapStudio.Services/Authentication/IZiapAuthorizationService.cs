namespace ZiapStudio.Services.Authentication;

public interface IZiapAuthorizationService
{
    Task<ZiapAuthorizationResult> AuthorizeAsync(
        CancellationToken cancellationToken = default);
}
