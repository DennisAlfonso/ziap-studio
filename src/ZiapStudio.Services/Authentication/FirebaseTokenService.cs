using System.Net;
using System.Text;
using System.Text.Json;

namespace ZiapStudio.Services.Authentication;

public sealed class FirebaseTokenService
{
    private const int MaximumErrorMessageLength = 240;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly Uri _customTokenEndpoint;
    private readonly Uri _refreshTokenEndpoint;

    public FirebaseTokenService(
        HttpClient httpClient,
        string apiKey,
        Uri? customTokenEndpoint = null,
        Uri? refreshTokenEndpoint = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Firebase Web API key richiesta.", nameof(apiKey));
        }

        _httpClient = httpClient;
        _apiKey = apiKey.Trim();
        _customTokenEndpoint = customTokenEndpoint ??
            new Uri("https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken");
        _refreshTokenEndpoint = refreshTokenEndpoint ??
            new Uri("https://securetoken.googleapis.com/v1/token");
    }

    public async Task<FirebaseTokenResult> SignInWithCustomTokenAsync(
        string customToken,
        string authenticatedUid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customToken))
        {
            throw new ArgumentException("Custom token richiesto.", nameof(customToken));
        }
        if (string.IsNullOrWhiteSpace(authenticatedUid))
        {
            throw new ArgumentException("UID myZenkai autenticato richiesto.", nameof(authenticatedUid));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            AddApiKey(_customTokenEndpoint))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new FirebaseCustomTokenRequest(customToken, true),
                    AuthenticationJsonContext.Default.FirebaseCustomTokenRequest),
                Encoding.UTF8,
                "application/json"),
        };

        var response = await SendAsync(request, cancellationToken);
        var payload = await DeserializeCustomTokenAsync(response, cancellationToken);
        return CreateResult(
            payload.IdToken,
            payload.RefreshToken,
            authenticatedUid.Trim(),
            payload.ExpiresIn);
    }

    public async Task<FirebaseTokenResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("Refresh token richiesto.", nameof(refreshToken));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            AddApiKey(_refreshTokenEndpoint))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            }),
        };

        var response = await SendAsync(request, cancellationToken);
        var payload = await DeserializeRefreshTokenAsync(response, cancellationToken);
        return CreateResult(
            payload.IdToken,
            payload.RefreshToken,
            payload.UserId,
            payload.ExpiresIn);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            throw new FirebaseAuthenticationException(
                "Firebase Authentication non e raggiungibile.",
                innerException: exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var serverCode = await ReadErrorCodeAsync(response, cancellationToken);
            var rejected = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized &&
                IsRejectedCredentialCode(serverCode);
            throw new FirebaseAuthenticationException(
                rejected
                    ? "La sessione myZenkai salvata non e piu valida. Accedi di nuovo."
                    : "Firebase Authentication ha rifiutato la richiesta.",
                rejected);
        }
    }

    private async Task<FirebaseCustomTokenResponse> DeserializeCustomTokenAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonSerializer.DeserializeAsync(
                    stream,
                    AuthenticationJsonContext.Default.FirebaseCustomTokenResponse,
                    cancellationToken)
                    ?? throw new JsonException("Risposta vuota.");
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                throw new FirebaseAuthenticationException(
                    "Firebase Authentication ha restituito una risposta non valida.",
                    innerException: exception);
            }
        }
    }

    private async Task<FirebaseRefreshTokenResponse> DeserializeRefreshTokenAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using (response)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonSerializer.DeserializeAsync(
                    stream,
                    AuthenticationJsonContext.Default.FirebaseRefreshTokenResponse,
                    cancellationToken)
                    ?? throw new JsonException("Risposta vuota.");
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                throw new FirebaseAuthenticationException(
                    "Firebase Authentication ha restituito una risposta non valida.",
                    innerException: exception);
            }
        }
    }

    private static FirebaseTokenResult CreateResult(
        string? idToken,
        string? refreshToken,
        string? uid,
        string? expiresIn)
    {
        if (string.IsNullOrWhiteSpace(idToken) ||
            string.IsNullOrWhiteSpace(refreshToken) ||
            string.IsNullOrWhiteSpace(uid) ||
            !int.TryParse(expiresIn, out var lifetimeSeconds) ||
            lifetimeSeconds <= 0)
        {
            throw new FirebaseAuthenticationException(
                "Firebase Authentication ha restituito una sessione incompleta.");
        }

        return new FirebaseTokenResult(
            idToken,
            refreshToken,
            uid,
            DateTimeOffset.UtcNow.AddSeconds(lifetimeSeconds));
    }

    private Uri AddApiKey(Uri endpoint)
    {
        var builder = new UriBuilder(endpoint);
        var prefix = string.IsNullOrEmpty(builder.Query) ? string.Empty : builder.Query.TrimStart('?') + "&";
        builder.Query = $"{prefix}key={Uri.EscapeDataString(_apiKey)}";
        return builder.Uri;
    }

    private static async Task<string?> ReadErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                var value = message.GetString()?.Trim();
                return value?.Length <= MaximumErrorMessageLength
                    ? value
                    : value?[..MaximumErrorMessageLength];
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
        }

        return null;
    }

    private static bool IsRejectedCredentialCode(string? code) =>
        code is "INVALID_REFRESH_TOKEN" or "TOKEN_EXPIRED" or "USER_DISABLED" or
            "USER_NOT_FOUND" or "INVALID_CUSTOM_TOKEN" or "CREDENTIAL_MISMATCH";

}
