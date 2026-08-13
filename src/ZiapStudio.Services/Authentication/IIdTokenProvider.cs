namespace ZiapStudio.Services.Authentication;

public interface IIdTokenProvider
{
    Task<string?> GetValidIdTokenAsync(CancellationToken cancellationToken = default);
}
