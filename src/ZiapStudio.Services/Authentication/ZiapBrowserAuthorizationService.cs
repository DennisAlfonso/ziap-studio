using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ZiapStudio.Services.Integration.Console;

namespace ZiapStudio.Services.Authentication;

public sealed class ZiapBrowserAuthorizationService : IZiapAuthorizationService
{
    public const string DefaultClientId = "ziap_studio_desktop_v1";
    public static readonly Uri DefaultCallbackUri =
        new("http://127.0.0.1:49173/auth/callback/");

    private static readonly TimeSpan DefaultAuthorizationTimeout = TimeSpan.FromMinutes(10);
    private readonly HttpClient _httpClient;
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly Uri _authorizationEndpoint;
    private readonly Uri _tokenEndpoint;
    private readonly Uri _callbackUri;
    private readonly string _clientId;
    private readonly TimeSpan _authorizationTimeout;

    public ZiapBrowserAuthorizationService(
        HttpClient httpClient,
        IExternalUriLauncher uriLauncher,
        Uri authorizationEndpoint,
        Uri tokenEndpoint,
        Uri? callbackUri = null,
        string clientId = DefaultClientId,
        TimeSpan? authorizationTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(uriLauncher);
        ValidateHttpsEndpoint(authorizationEndpoint, nameof(authorizationEndpoint));
        ValidateHttpsEndpoint(tokenEndpoint, nameof(tokenEndpoint));

        _callbackUri = callbackUri ?? DefaultCallbackUri;
        if (!_callbackUri.IsAbsoluteUri ||
            _callbackUri.Scheme != Uri.UriSchemeHttp ||
            !_callbackUri.IsLoopback ||
            !_callbackUri.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Il callback OAuth deve essere un URL HTTP loopback assoluto con slash finale.",
                nameof(callbackUri));
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("OAuth client ID richiesto.", nameof(clientId));
        }

        _httpClient = httpClient;
        _uriLauncher = uriLauncher;
        _authorizationEndpoint = authorizationEndpoint;
        _tokenEndpoint = tokenEndpoint;
        _clientId = clientId.Trim();
        _authorizationTimeout = authorizationTimeout ?? DefaultAuthorizationTimeout;
    }

    public async Task<ZiapAuthorizationResult> AuthorizeAsync(
        CancellationToken cancellationToken = default)
    {
        var state = CreateOpaqueValue(32);
        var codeVerifier = CreateOpaqueValue(64);
        var codeChallenge = Base64Url(
            SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var authorizationUri = BuildAuthorizationUri(state, codeChallenge);

        using var listener = new HttpListener();
        listener.Prefixes.Add(_callbackUri.AbsoluteUri);
        try
        {
            listener.Start();
        }
        catch (Exception exception) when (
            exception is HttpListenerException or InvalidOperationException)
        {
            throw new AuthenticationException(
                "Impossibile avviare il callback locale di ZIAP Studio. " +
                "Verifica che la porta 49173 non sia gia in uso.",
                exception);
        }

        try
        {
            _uriLauncher.Open(authorizationUri);
            var context = await listener.GetContextAsync()
                .WaitAsync(_authorizationTimeout, cancellationToken);
            var callback = ParseCallback(context.Request.Url, state);
            await WriteBrowserResponseAsync(context.Response, callback.Error is null);

            if (callback.Error is not null)
            {
                throw new AuthenticationException(callback.Error);
            }

            return await ExchangeCodeAsync(callback.Code!, codeVerifier, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            throw new AuthenticationException(
                "La richiesta di accesso myZenkai e scaduta.",
                exception);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AuthenticationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpListenerException or HttpRequestException or
                System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new AuthenticationException(
                "Non e stato possibile completare l'accesso con myZenkai.",
                exception);
        }
        finally
        {
            listener.Stop();
        }
    }

    internal Uri BuildAuthorizationUri(string state, string codeChallenge)
    {
        var query = BuildFormQuery(new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _clientId,
            ["redirect_uri"] = _callbackUri.AbsoluteUri,
            ["scope"] = "openid profile:read email:read",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        });
        var builder = new UriBuilder(_authorizationEndpoint)
        {
            Query = query,
        };
        return builder.Uri;
    }

    private async Task<ZiapAuthorizationResult> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = _clientId,
                ["code"] = code,
                ["redirect_uri"] = _callbackUri.AbsoluteUri,
                ["code_verifier"] = codeVerifier,
            }),
        };

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadOAuthErrorAsync(response, cancellationToken);
            throw new AuthenticationException(
                string.IsNullOrWhiteSpace(detail)
                    ? "myZenkai ha rifiutato lo scambio del codice di accesso."
                    : $"myZenkai ha rifiutato lo scambio del codice: {detail}");
        }

        ZiapOAuthTokenResponse? payload;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            payload = await JsonSerializer.DeserializeAsync(
                stream,
                AuthenticationJsonContext.Default.ZiapOAuthTokenResponse,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new AuthenticationException(
                "myZenkai ha restituito una risposta OAuth non valida.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(payload?.FirebaseCustomToken) ||
            string.IsNullOrWhiteSpace(payload.User?.Uid))
        {
            throw new AuthenticationException(
                "Il backend myZenkai non ha restituito la sessione Firebase per ZIAP Studio.");
        }

        return new ZiapAuthorizationResult(
            payload.FirebaseCustomToken,
            new AuthenticationAccount(
                payload.User.Uid,
                payload.User.Nickname,
                payload.User.Email));
    }

    private static OAuthCallback ParseCallback(Uri? callbackUri, string expectedState)
    {
        if (callbackUri is null)
        {
            return new OAuthCallback(null, "Callback myZenkai non valido.");
        }

        var values = ParseQuery(callbackUri.Query);
        if (!values.TryGetValue("state", out var state) || !SecureEquals(state, expectedState))
        {
            return new OAuthCallback(null, "Risposta OAuth non valida: state non corrispondente.");
        }

        if (values.TryGetValue("error", out var error))
        {
            values.TryGetValue("error_description", out var description);
            return new OAuthCallback(
                null,
                MapAuthorizationError(error, description));
        }

        if (!values.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            return new OAuthCallback(null, "Risposta OAuth senza authorization code.");
        }

        return new OAuthCallback(code, null);
    }

    private static async Task WriteBrowserResponseAsync(
        HttpListenerResponse response,
        bool success)
    {
        const string successHtml =
            "<!doctype html><html lang=\"it\"><meta charset=\"utf-8\">" +
            "<link rel=\"icon\" href=\"data:,\">" +
            "<title>ZIAP Studio connesso</title><body style=\"font-family:Segoe UI,sans-serif;" +
            "max-width:620px;margin:80px auto;padding:24px\"><h1>Accesso completato</h1>" +
            "<p>Puoi chiudere questa scheda e tornare a ZIAP Studio.</p></body></html>";
        const string errorHtml =
            "<!doctype html><html lang=\"it\"><meta charset=\"utf-8\">" +
            "<link rel=\"icon\" href=\"data:,\">" +
            "<title>Accesso non completato</title><body style=\"font-family:Segoe UI,sans-serif;" +
            "max-width:620px;margin:80px auto;padding:24px\"><h1>Accesso non completato</h1>" +
            "<p>Torna a ZIAP Studio per i dettagli.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(success ? successHtml : errorHtml);
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static string MapAuthorizationError(string error, string? description)
    {
        if (error.Equals("access_denied", StringComparison.Ordinal))
        {
            return "Accesso annullato dall'utente.";
        }

        var normalizedDescription = description?.Replace('+', ' ').Trim() ?? string.Empty;
        if (normalizedDescription.Contains(
                "Client esterno non trovato",
                StringComparison.OrdinalIgnoreCase))
        {
            return "ZIAP Studio non e ancora abilitato sul backend myZenkai pubblicato.";
        }

        return "myZenkai ha rifiutato la richiesta di accesso.";
    }

    private static async Task<string?> ReadOAuthErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error_description", out var detail) &&
                detail.ValueKind == JsonValueKind.String)
            {
                var value = detail.GetString()?.Trim();
                return value?.Length <= 240 ? value : value?[..240];
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
        }

        return null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            var value = Uri.UnescapeDataString(separator < 0 ? string.Empty : pair[(separator + 1)..]);
            result[key] = value;
        }
        return result;
    }

    private static string BuildFormQuery(IReadOnlyDictionary<string, string> values) =>
        string.Join('&', values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static string CreateOpaqueValue(int bytes) =>
        Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool SecureEquals(string actual, string expected)
    {
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static void ValidateHttpsEndpoint(Uri endpoint, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("L'endpoint OAuth deve essere HTTPS e assoluto.", parameterName);
        }
    }

    private sealed record OAuthCallback(string? Code, string? Error);

}
