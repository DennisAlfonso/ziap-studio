using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ZiapStudio.Services.Authentication;

/// <summary>
/// Converts the short-lived Firebase identity produced by the OAuth bridge into
/// a server-registered ZIAP application session. It deliberately owns no
/// credential persistence: only <see cref="ZiapAuthenticationService"/> stores
/// the refresh token obtained after the final custom-token exchange.
/// </summary>
public sealed class ZiapAppSessionService : IZiapAppSessionService
{
    private const string ZiapApplication = "ziap_editor";
    private const string NativeLoginMethod = "native";
    private readonly HttpClient _httpClient;
    private readonly Uri _resolveLoginFlowEndpoint;
    private readonly Uri _confirmLegalStateEndpoint;
    private readonly Uri _finalizeLoginSessionEndpoint;

    public ZiapAppSessionService(
        HttpClient httpClient,
        Uri resolveLoginFlowEndpoint,
        Uri confirmLegalStateEndpoint,
        Uri finalizeLoginSessionEndpoint)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ValidateHttpsEndpoint(resolveLoginFlowEndpoint, nameof(resolveLoginFlowEndpoint));
        ValidateHttpsEndpoint(confirmLegalStateEndpoint, nameof(confirmLegalStateEndpoint));
        ValidateHttpsEndpoint(finalizeLoginSessionEndpoint, nameof(finalizeLoginSessionEndpoint));

        _httpClient = httpClient;
        _resolveLoginFlowEndpoint = resolveLoginFlowEndpoint;
        _confirmLegalStateEndpoint = confirmLegalStateEndpoint;
        _finalizeLoginSessionEndpoint = finalizeLoginSessionEndpoint;
    }

    public async Task<ZiapApplicationSessionResult> CompleteAsync(
        string temporaryFirebaseIdToken,
        string expectedUid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(temporaryFirebaseIdToken))
        {
            throw new ArgumentException("ID token Firebase temporaneo richiesto.", nameof(temporaryFirebaseIdToken));
        }
        if (string.IsNullOrWhiteSpace(expectedUid))
        {
            throw new ArgumentException("UID myZenkai autenticato richiesto.", nameof(expectedUid));
        }

        var deviceInfo = CreateDesktopDeviceInfo();
        var resolve = await PostCallableAsync(
            _resolveLoginFlowEndpoint,
            temporaryFirebaseIdToken,
            new ZiapResolveLoginFlowCallableRequest(
                new ZiapResolveLoginFlowRequest(
                    ZiapApplication,
                    NativeLoginMethod,
                    "windows",
                    deviceInfo)),
            AuthenticationJsonContext.Default.ZiapResolveLoginFlowCallableRequest,
            AuthenticationJsonContext.Default.ZiapResolveLoginFlowCallableResponse,
            cancellationToken);
        var resolved = resolve.Result;
        if (resolved?.Ok != true)
        {
            throw new AuthenticationException("myZenkai non ha autorizzato la sessione ZIAP.");
        }
        if (string.Equals(resolved.NextStep, "REQUIRE_2FA", StringComparison.Ordinal))
        {
            throw new AuthenticationException(
                "La prova OAuth non ha soddisfatto la verifica a due fattori richiesta da ZIAP.");
        }
        if (!string.Equals(resolved.NextStep, "AUTHENTICATED", StringComparison.Ordinal))
        {
            throw new AuthenticationException("myZenkai non ha completato l'autorizzazione della sessione ZIAP.");
        }
        if (!string.Equals(resolved.User?.Uid, expectedUid, StringComparison.Ordinal))
        {
            throw new AuthenticationException("myZenkai ha restituito un UID non coerente per la sessione ZIAP.");
        }
        if (string.IsNullOrWhiteSpace(resolved.LoginAttemptId))
        {
            throw new AuthenticationException("myZenkai non ha restituito un tentativo di login ZIAP valido.");
        }

        var legal = await PostCallableAsync(
            _confirmLegalStateEndpoint,
            temporaryFirebaseIdToken,
            new ZiapConfirmLoginLegalStateCallableRequest(
                new ZiapConfirmLoginLegalStateRequest(resolved.LoginAttemptId)),
            AuthenticationJsonContext.Default.ZiapConfirmLoginLegalStateCallableRequest,
            AuthenticationJsonContext.Default.ZiapConfirmLoginLegalStateCallableResponse,
            cancellationToken);
        if (legal.Result?.Success != true)
        {
            throw new AuthenticationException("myZenkai non ha verificato lo stato legale della sessione ZIAP.");
        }

        var finalized = await PostCallableAsync(
            _finalizeLoginSessionEndpoint,
            temporaryFirebaseIdToken,
            new ZiapFinalizeLoginSessionCallableRequest(
                new ZiapFinalizeLoginSessionRequest(
                    resolved.LoginAttemptId,
                    ZiapApplication,
                    NativeLoginMethod,
                    GetApplicationVersion(),
                    deviceInfo)),
            AuthenticationJsonContext.Default.ZiapFinalizeLoginSessionCallableRequest,
            AuthenticationJsonContext.Default.ZiapFinalizeLoginSessionCallableResponse,
            cancellationToken);
        if (finalized.Result?.Ok != true || string.IsNullOrWhiteSpace(finalized.Result.AppSessionToken))
        {
            throw new AuthenticationException("myZenkai non ha creato una sessione applicativa ZIAP valida.");
        }

        return new ZiapApplicationSessionResult(finalized.Result.AppSessionToken);
    }

    private async Task<TResponse> PostCallableAsync<TRequest, TResponse>(
        Uri endpoint,
        string firebaseIdToken,
        TRequest payload,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, requestTypeInfo),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", firebaseIdToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new AuthenticationException(
                "myZenkai non è raggiungibile per completare la sessione ZIAP.",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateCallableExceptionAsync(response, cancellationToken);
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonSerializer.DeserializeAsync(stream, responseTypeInfo, cancellationToken)
                    ?? throw new JsonException("Risposta callable vuota.");
            }
            catch (JsonException exception)
            {
                throw new AuthenticationException(
                    "myZenkai ha restituito una risposta di sessione ZIAP non valida.",
                    exception);
            }
        }
    }

    private static async Task<AuthenticationException> CreateCallableExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? status = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync(
                stream,
                AuthenticationJsonContext.Default.FirebaseCallableErrorEnvelope,
                cancellationToken);
            status = error?.Error?.Status;
        }
        catch (JsonException)
        {
        }

        var message = status switch
        {
            "UNAUTHENTICATED" => "La sessione Firebase temporanea non è più valida. Accedi di nuovo.",
            "PERMISSION_DENIED" => "myZenkai ha rifiutato il completamento della sessione ZIAP.",
            "FAILED_PRECONDITION" => "myZenkai richiede un prerequisito per completare la sessione ZIAP.",
            _ when response.StatusCode == HttpStatusCode.Unauthorized =>
                "La sessione Firebase temporanea non è più valida. Accedi di nuovo.",
            _ when response.StatusCode == HttpStatusCode.Forbidden =>
                "myZenkai ha rifiutato il completamento della sessione ZIAP.",
            _ => "myZenkai non ha completato la sessione ZIAP.",
        };
        return new AuthenticationException(message);
    }

    private static ZiapDesktopDeviceInfo CreateDesktopDeviceInfo() => new(
        "windows",
        Environment.MachineName,
        Environment.OSVersion.Platform.ToString(),
        Environment.OSVersion.VersionString,
        CultureInfo.CurrentUICulture.Name,
        TimeZoneInfo.Local.Id);

    private static string? GetApplicationVersion() =>
        typeof(ZiapAppSessionService).Assembly.GetName().Version?.ToString();

    private static void ValidateHttpsEndpoint(Uri endpoint, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("L'endpoint deve usare HTTPS assoluto.", parameterName);
        }
    }
}
