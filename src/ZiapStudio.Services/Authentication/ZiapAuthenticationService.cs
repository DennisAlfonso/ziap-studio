using System.Text.Json;

namespace ZiapStudio.Services.Authentication;

public sealed class ZiapAuthenticationService : IIdTokenProvider
{
    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromMinutes(2);
    private readonly IZiapAuthorizationService _browserAuthorizationService;
    private readonly FirebaseTokenService _firebaseTokenService;
    private readonly IZiapAppSessionService _ziapAppSessionService;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AuthenticationSession? _session;
    private bool _initialized;

    public ZiapAuthenticationService(
        IZiapAuthorizationService browserAuthorizationService,
        FirebaseTokenService firebaseTokenService,
        IZiapAppSessionService ziapAppSessionService,
        ISecureCredentialStore credentialStore)
    {
        _browserAuthorizationService = browserAuthorizationService ??
            throw new ArgumentNullException(nameof(browserAuthorizationService));
        _firebaseTokenService = firebaseTokenService ??
            throw new ArgumentNullException(nameof(firebaseTokenService));
        _ziapAppSessionService = ziapAppSessionService ??
            throw new ArgumentNullException(nameof(ziapAppSessionService));
        _credentialStore = credentialStore ??
            throw new ArgumentNullException(nameof(credentialStore));
    }

    public bool IsAuthenticated => _session is not null;

    public AuthenticationAccount? CurrentAccount => _session?.Account;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            var stored = await ReadStoredCredentialAsync(cancellationToken);
            if (stored is not null)
            {
                await RefreshAsync(stored, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SignInAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var authorization = await _browserAuthorizationService.AuthorizeAsync(cancellationToken);
            var temporaryFirebase = await _firebaseTokenService.SignInWithCustomTokenAsync(
                authorization.FirebaseCustomToken,
                authorization.Account.Uid,
                cancellationToken);
            if (!string.Equals(temporaryFirebase.Uid, authorization.Account.Uid, StringComparison.Ordinal))
            {
                throw new AuthenticationException("Firebase ha restituito un UID non coerente con OAuth.");
            }
            var appSession = await _ziapAppSessionService.CompleteAsync(
                temporaryFirebase.IdToken,
                authorization.Account.Uid,
                cancellationToken);
            var firebase = await _firebaseTokenService.SignInWithCustomTokenAsync(
                appSession.AppSessionToken,
                authorization.Account.Uid,
                cancellationToken);
            if (!string.Equals(firebase.Uid, authorization.Account.Uid, StringComparison.Ordinal))
            {
                throw new AuthenticationException("La sessione ZIAP finale ha restituito un UID non coerente con OAuth.");
            }
            var account = authorization.Account with
            {
                Uid = firebase.Uid,
            };
            var stored = new StoredAuthenticationCredential(
                firebase.RefreshToken,
                account.Uid,
                account.Nickname,
                account.Email);
            await WriteStoredCredentialAsync(stored, cancellationToken);
            _session = new AuthenticationSession(
                account,
                firebase.IdToken,
                firebase.RefreshToken,
                firebase.ExpiresAt);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _credentialStore.DeleteAsync(cancellationToken);
            _session = null;
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetValidIdTokenAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null &&
                _session.IdTokenExpiresAt > DateTimeOffset.UtcNow.Add(TokenRefreshMargin))
            {
                return _session.IdToken;
            }

            var stored = _session is null
                ? await ReadStoredCredentialAsync(cancellationToken)
                : new StoredAuthenticationCredential(
                    _session.RefreshToken,
                    _session.Account.Uid,
                    _session.Account.Nickname,
                    _session.Account.Email);
            if (stored is null)
            {
                _initialized = true;
                return null;
            }

            await RefreshAsync(stored, cancellationToken);
            _initialized = true;
            return _session?.IdToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshAsync(
        StoredAuthenticationCredential stored,
        CancellationToken cancellationToken)
    {
        try
        {
            var firebase = await _firebaseTokenService.RefreshAsync(
                stored.RefreshToken,
                cancellationToken);
            var account = new AuthenticationAccount(
                firebase.Uid,
                stored.Nickname,
                stored.Email);
            var rotated = stored with
            {
                RefreshToken = firebase.RefreshToken,
                Uid = firebase.Uid,
            };
            await WriteStoredCredentialAsync(rotated, cancellationToken);
            _session = new AuthenticationSession(
                account,
                firebase.IdToken,
                firebase.RefreshToken,
                firebase.ExpiresAt);
        }
        catch (FirebaseAuthenticationException exception) when (exception.IsCredentialRejected)
        {
            await _credentialStore.DeleteAsync(cancellationToken);
            _session = null;
            throw;
        }
    }

    private async Task<StoredAuthenticationCredential?> ReadStoredCredentialAsync(
        CancellationToken cancellationToken)
    {
        var value = await _credentialStore.ReadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize(
                value,
                AuthenticationJsonContext.Default.StoredAuthenticationCredential);
            if (stored is null ||
                string.IsNullOrWhiteSpace(stored.RefreshToken) ||
                string.IsNullOrWhiteSpace(stored.Uid))
            {
                throw new JsonException("Credenziale incompleta.");
            }

            return stored;
        }
        catch (JsonException exception)
        {
            await _credentialStore.DeleteAsync(cancellationToken);
            throw new AuthenticationException(
                "La credenziale myZenkai protetta non e valida ed e stata rimossa.",
                exception);
        }
    }

    private Task WriteStoredCredentialAsync(
        StoredAuthenticationCredential credential,
        CancellationToken cancellationToken) =>
        _credentialStore.WriteAsync(
            JsonSerializer.Serialize(
                credential,
                AuthenticationJsonContext.Default.StoredAuthenticationCredential),
            cancellationToken);
}
