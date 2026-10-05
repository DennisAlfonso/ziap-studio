using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ZiapStudio.Services.Authentication;
using ZiapStudio.Services.Integration.Console;

namespace ZiapStudio.Services.Tests;

public sealed class AuthenticationServiceTests
{
    [Fact]
    public async Task BrowserAuthorization_UsesLoopbackCodeAndPkceWithoutTokensInUrl()
    {
        var callbackUri = CreateAvailableLoopbackUri();
        string? tokenRequestBody = null;
        Uri? bridgeRequestUri = null;
        AuthenticationHeaderValue? bridgeAuthorization = null;
        string? bridgeRequestBody = null;
        var handler = new AsyncStubHttpMessageHandler(async request =>
        {
            if (request.RequestUri?.AbsolutePath == "/oauth/token")
            {
                tokenRequestBody = await request.Content!.ReadAsStringAsync();
                return JsonResponse(
                    """
                    {
                      "access_token": "oauth-access-token",
                      "token_type": "Bearer",
                      "expires_in": 3600,
                      "scope": "openid profile:read email:read firebase_session:create",
                      "issued_at": "2026-10-05T00:00:00Z",
                      "user": {
                        "uid": "uid-123",
                        "nickname": "YuukiToyaro",
                        "email": "yuuki@example.test"
                      }
                    }
                    """);
            }

            bridgeRequestUri = request.RequestUri;
            bridgeAuthorization = request.Headers.Authorization;
            bridgeRequestBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(
                """
                {
                  "ok": true,
                  "uid": "uid-123",
                  "customToken": "firebase-custom-token"
                }
                """);
        });
        var launcher = new LoopbackCallbackLauncher(callbackUri);
        var service = new ZiapBrowserAuthorizationService(
            new HttpClient(handler),
            launcher,
            new Uri("https://identity.example.test/oauth/authorize"),
            new Uri("https://identity.example.test/oauth/token"),
            new Uri("https://identity.example.test/oauth/firebase/custom-token"),
            callbackUri,
            authorizationTimeout: TimeSpan.FromSeconds(5));

        var result = await service.AuthorizeAsync();
        await launcher.CallbackCompleted!;

        Assert.Equal("firebase-custom-token", result.FirebaseCustomToken);
        Assert.NotNull(launcher.AuthorizationUri);
        Assert.DoesNotContain("token", launcher.AuthorizationUri.Query, StringComparison.OrdinalIgnoreCase);
        var authorizationQuery = ParseForm(launcher.AuthorizationUri.Query.TrimStart('?'));
        Assert.Equal("S256", authorizationQuery["code_challenge_method"]);
        Assert.Equal(callbackUri.AbsoluteUri, authorizationQuery["redirect_uri"]);
        var requestedScopes = authorizationQuery["scope"].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("openid", requestedScopes);
        Assert.Contains("profile:read", requestedScopes);
        Assert.Contains("email:read", requestedScopes);
        Assert.Contains("firebase_session:create", requestedScopes);
        var tokenForm = ParseForm(tokenRequestBody!);
        Assert.Equal("authorization-code", tokenForm["code"]);
        Assert.Equal(callbackUri.AbsoluteUri, tokenForm["redirect_uri"]);
        var expectedChallenge = Base64Url(
            SHA256.HashData(Encoding.ASCII.GetBytes(tokenForm["code_verifier"])));
        Assert.Equal(authorizationQuery["code_challenge"], expectedChallenge);
        Assert.Equal("Bearer", bridgeAuthorization?.Scheme);
        Assert.Equal("oauth-access-token", bridgeAuthorization?.Parameter);
        Assert.DoesNotContain("oauth-access-token", launcher.CallbackRequestUri?.AbsoluteUri);
        Assert.DoesNotContain("firebase-custom-token", launcher.CallbackRequestUri?.AbsoluteUri);
        Assert.DoesNotContain("oauth-access-token", bridgeRequestUri?.AbsoluteUri);
        Assert.DoesNotContain("firebase-custom-token", launcher.AuthorizationUri.AbsoluteUri);
        Assert.DoesNotContain("firebase-custom-token", bridgeRequestUri?.AbsoluteUri);
        Assert.DoesNotContain("oauth-access-token", bridgeRequestBody);
        Assert.DoesNotContain("firebase-custom-token", tokenRequestBody);
        Assert.Contains("Accesso completato", launcher.CallbackResponseBody);
    }

    [Fact]
    public async Task BrowserAuthorization_RejectsOAuthErrorsWithoutLeakingTokens()
    {
        var rejected = await AuthorizeExpectingFailureAsync(
            JsonResponse("{\"error_description\":\"oauth-access-token must not escape\"}", HttpStatusCode.BadRequest),
            JsonResponse("{}"));
        Assert.Contains("scambio del codice OAuth", rejected.Message);

        var missingAccessToken = await AuthorizeExpectingFailureAsync(
            JsonResponse("""{"user":{"uid":"uid-123"}}"""),
            JsonResponse("{}"));
        Assert.Contains("access token OAuth valido", missingAccessToken.Message);

        var missingUid = await AuthorizeExpectingFailureAsync(
            JsonResponse("""{"access_token":"oauth-access-token","user":{}}"""),
            JsonResponse("{}"));
        Assert.Contains("identificativo utente OAuth valido", missingUid.Message);

        var malformed = await AuthorizeExpectingFailureAsync(
            JsonResponse("{ invalid"),
            JsonResponse("{}"));
        Assert.Contains("risposta OAuth non valida", malformed.Message);

        AssertNoSensitiveTokens(rejected, missingAccessToken, missingUid, malformed);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task BrowserAuthorization_RejectsFirebaseBridgeHttpErrors(HttpStatusCode status)
    {
        var failure = await AuthorizeExpectingFailureAsync(
            ValidOAuthResponse,
            JsonResponse("{\"error_description\":\"firebase-custom-token must not escape\"}", status));

        Assert.Contains("bridge della sessione Firebase", failure.Message);
        AssertNoSensitiveTokens(failure);
    }

    [Fact]
    public async Task BrowserAuthorization_RejectsInvalidFirebaseBridgePayloadsAndUidMismatch()
    {
        var missingToken = await AuthorizeExpectingFailureAsync(
            ValidOAuthResponse,
            JsonResponse("""{"ok":true,"uid":"uid-123"}"""));
        Assert.Contains("sessione Firebase valida", missingToken.Message);

        var rejected = await AuthorizeExpectingFailureAsync(
            ValidOAuthResponse,
            JsonResponse("""{"ok":false,"uid":"uid-123","customToken":"firebase-custom-token"}"""));
        Assert.Contains("sessione Firebase valida", rejected.Message);

        var malformed = await AuthorizeExpectingFailureAsync(
            ValidOAuthResponse,
            JsonResponse("{ invalid"));
        Assert.Contains("Firebase bridge non valida", malformed.Message);

        var mismatch = await AuthorizeExpectingFailureAsync(
            ValidOAuthResponse,
            JsonResponse("""{"ok":true,"uid":"uid-other","customToken":"firebase-custom-token"}"""));
        Assert.Contains("identita OAuth e Firebase", mismatch.Message);
        AssertNoSensitiveTokens(missingToken, rejected, malformed, mismatch);
    }

    [Fact]
    public async Task FirebaseTokenService_ExchangesCustomTokenWithoutPuttingItInUrl()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new AsyncStubHttpMessageHandler(async request =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(
                """
                {
                  "idToken": "firebase-id-token",
                  "refreshToken": "firebase-refresh-token",
                  "expiresIn": "3600",
                  "localId": "uid-123"
                }
                """);
        });
        var service = new FirebaseTokenService(new HttpClient(handler), "public-api-key");

        var result = await service.SignInWithCustomTokenAsync(
            "one-time-custom-token",
            "uid-123");

        Assert.Equal(
            "https://identitytoolkit.googleapis.com/v1/accounts:signInWithCustomToken?key=public-api-key",
            capturedRequest?.RequestUri?.AbsoluteUri);
        Assert.DoesNotContain("one-time-custom-token", capturedRequest?.RequestUri?.AbsoluteUri);
        Assert.Contains("one-time-custom-token", capturedBody);
        Assert.Equal("firebase-id-token", result.IdToken);
        Assert.Equal("firebase-refresh-token", result.RefreshToken);
        Assert.Equal("uid-123", result.Uid);
    }

    [Fact]
    public async Task FirebaseTokenService_RefreshesAndAcceptsSnakeCaseResponse()
    {
        string? capturedBody = null;
        var handler = new AsyncStubHttpMessageHandler(async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(
                """
                {
                  "id_token": "rotated-id-token",
                  "refresh_token": "rotated-refresh-token",
                  "expires_in": "3600",
                  "user_id": "uid-123"
                }
                """);
        });
        var service = new FirebaseTokenService(new HttpClient(handler), "public-api-key");

        var result = await service.RefreshAsync("saved-refresh-token");

        Assert.Contains("grant_type=refresh_token", capturedBody);
        Assert.Contains("refresh_token=saved-refresh-token", capturedBody);
        Assert.Equal("rotated-id-token", result.IdToken);
        Assert.Equal("rotated-refresh-token", result.RefreshToken);
    }

    [Fact]
    public async Task AuthenticationSession_PersistsRefreshCredentialAndRestoresOnRestart()
    {
        var store = new MemoryCredentialStore();
        var firstFirebase = new FirebaseTokenService(
            new HttpClient(new AsyncStubHttpMessageHandler(_ => Task.FromResult(JsonResponse(
                """
                {
                  "idToken": "first-id-token",
                  "refreshToken": "first-refresh-token",
                  "expiresIn": "3600",
                  "localId": "uid-123"
                }
                """)))),
            "public-api-key");
        var firstSession = new ZiapAuthenticationService(
            new StubAuthorizationService(),
            firstFirebase,
            new StubZiapAppSessionService(),
            store);

        await firstSession.SignInAsync();

        Assert.True(firstSession.IsAuthenticated);
        Assert.Equal("YuukiToyaro", firstSession.CurrentAccount?.DisplayName);
        Assert.NotNull(store.Value);
        Assert.Contains("first-refresh-token", store.Value);
        Assert.DoesNotContain("firebase-custom-token", store.Value);
        Assert.DoesNotContain("oauth-access-token", store.Value);

        var restartedFirebase = new FirebaseTokenService(
            new HttpClient(new AsyncStubHttpMessageHandler(_ => Task.FromResult(JsonResponse(
                """
                {
                  "id_token": "restored-id-token",
                  "refresh_token": "rotated-refresh-token",
                  "expires_in": "3600",
                  "user_id": "uid-123"
                }
                """)))),
            "public-api-key");
        var restartedSession = new ZiapAuthenticationService(
            new StubAuthorizationService(),
            restartedFirebase,
            new StubZiapAppSessionService(),
            store);

        await restartedSession.InitializeAsync();

        Assert.True(restartedSession.IsAuthenticated);
        Assert.Equal("restored-id-token", await restartedSession.GetValidIdTokenAsync());
        Assert.Contains("rotated-refresh-token", store.Value);
        Assert.DoesNotContain("first-refresh-token", store.Value);
    }

    [Fact]
    public async Task AuthenticationSession_CompletesOAuthBridgeAndServerBackedZiapSession()
    {
        var callbackUri = CreateAvailableLoopbackUri();
        var authorizationService = new ZiapBrowserAuthorizationService(
            new HttpClient(new AsyncStubHttpMessageHandler(request => Task.FromResult(
                request.RequestUri?.AbsolutePath == "/oauth/token"
                    ? ValidOAuthResponse
                    : JsonResponse("""{"ok":true,"uid":"uid-123","customToken":"firebase-custom-token"}""")))),
            new LoopbackCallbackLauncher(callbackUri),
            new Uri("https://identity.example.test/oauth/authorize"),
            new Uri("https://identity.example.test/oauth/token"),
            new Uri("https://identity.example.test/oauth/firebase/custom-token"),
            callbackUri,
            authorizationTimeout: TimeSpan.FromSeconds(5));
        var firebaseCustomTokens = new List<string>();
        var firebase = new FirebaseTokenService(
            new HttpClient(new AsyncStubHttpMessageHandler(async request =>
            {
                var body = await request.Content!.ReadAsStringAsync();
                using var document = JsonDocument.Parse(body);
                var customToken = document.RootElement.GetProperty("token").GetString();
                firebaseCustomTokens.Add(customToken!);
                return customToken switch
                {
                    "firebase-custom-token" => JsonResponse(
                        """{"idToken":"temporary-id-token","refreshToken":"temporary-refresh-token","expiresIn":"3600","localId":"uid-123"}"""),
                    "app-session-token" => JsonResponse(
                        """{"idToken":"final-id-token","refreshToken":"final-refresh-token","expiresIn":"3600","localId":"uid-123"}"""),
                    _ => throw new InvalidOperationException("Custom token inatteso."),
                };
            })),
            "public-api-key");
        var callableRequests = new List<HttpRequestMessage>();
        var appSessionService = new ZiapAppSessionService(
            new HttpClient(new AsyncStubHttpMessageHandler(async request =>
            {
                callableRequests.Add(request);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("temporary-id-token", request.Headers.Authorization?.Parameter);
                Assert.DoesNotContain("temporary-id-token", request.RequestUri?.AbsoluteUri);
                var body = await request.Content!.ReadAsStringAsync();
                Assert.DoesNotContain("temporary-id-token", body);
                using var document = JsonDocument.Parse(body);
                Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("data").ValueKind);
                return request.RequestUri?.AbsolutePath switch
                {
                    "/resolve" => JsonResponse(
                        """{"result":{"ok":true,"nextStep":"AUTHENTICATED","loginAttemptId":"attempt-123","user":{"uid":"uid-123"}}}"""),
                    "/legal" when body.Contains("attempt-123", StringComparison.Ordinal) =>
                        JsonResponse("""{"result":{"success":true}}"""),
                    "/finalize" when body.Contains("attempt-123", StringComparison.Ordinal) =>
                        JsonResponse("""{"result":{"ok":true,"appSessionToken":"app-session-token"}}"""),
                    _ => throw new InvalidOperationException("Callable inattesa."),
                };
            })),
            new Uri("https://identity.example.test/resolve"),
            new Uri("https://identity.example.test/legal"),
            new Uri("https://identity.example.test/finalize"));
        var store = new MemoryCredentialStore();
        var session = new ZiapAuthenticationService(
            authorizationService,
            firebase,
            appSessionService,
            store);

        await session.SignInAsync();

        Assert.True(session.IsAuthenticated);
        Assert.Equal("uid-123", session.CurrentAccount?.Uid);
        Assert.Equal("final-id-token", await session.GetValidIdTokenAsync());
        Assert.Equal(["firebase-custom-token", "app-session-token"], firebaseCustomTokens);
        Assert.Equal(3, callableRequests.Count);
        Assert.Contains("final-refresh-token", store.Value);
        Assert.DoesNotContain("temporary-refresh-token", store.Value);
        Assert.DoesNotContain("oauth-access-token", store.Value);
        Assert.DoesNotContain("firebase-custom-token", store.Value);
        Assert.DoesNotContain("app-session-token", store.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", "sessione Firebase temporanea")]
    [InlineData(HttpStatusCode.Forbidden, "PERMISSION_DENIED", "rifiutato il completamento")]
    [InlineData(HttpStatusCode.BadRequest, "FAILED_PRECONDITION", "richiede un prerequisito")]
    public async Task ZiapAppSession_SanitizesCallableFailures(
        HttpStatusCode status,
        string callableStatus,
        string expectedMessage)
    {
        var service = CreateAppSessionService(_ => JsonResponse(
            $"{{\"error\":{{\"status\":\"{callableStatus}\",\"message\":\"temporary-id-token must not escape\"}}}}",
            status));

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.CompleteAsync("temporary-id-token", "uid-123"));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("temporary-id-token", exception.ToString());
    }

    [Fact]
    public async Task ZiapAppSession_RejectsRequiredSecondFactorAndUidMismatch()
    {
        var requireSecondFactor = CreateAppSessionService(_ => JsonResponse(
            """{"result":{"ok":true,"nextStep":"REQUIRE_2FA","loginAttemptId":"attempt-123"}}"""));
        var secondFactor = await Assert.ThrowsAsync<AuthenticationException>(() =>
            requireSecondFactor.CompleteAsync("temporary-id-token", "uid-123"));
        Assert.Contains("prova OAuth", secondFactor.Message);

        var uidMismatch = CreateAppSessionService(_ => JsonResponse(
            """{"result":{"ok":true,"nextStep":"AUTHENTICATED","loginAttemptId":"attempt-123","user":{"uid":"uid-other"}}}"""));
        var mismatch = await Assert.ThrowsAsync<AuthenticationException>(() =>
            uidMismatch.CompleteAsync("temporary-id-token", "uid-123"));
        Assert.Contains("UID non coerente", mismatch.Message);
    }

    private static HttpResponseMessage ValidOAuthResponse => JsonResponse(
        """
        {
          "access_token": "oauth-access-token",
          "token_type": "Bearer",
          "expires_in": 3600,
          "scope": "openid profile:read email:read firebase_session:create",
          "issued_at": "2026-10-05T00:00:00Z",
          "user": {"uid":"uid-123","nickname":"YuukiToyaro","email":"yuuki@example.test"}
        }
        """);

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static ZiapAppSessionService CreateAppSessionService(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) =>
        new(
            new HttpClient(new AsyncStubHttpMessageHandler(request =>
                Task.FromResult(responseFactory(request)))),
            new Uri("https://identity.example.test/resolve"),
            new Uri("https://identity.example.test/legal"),
            new Uri("https://identity.example.test/finalize"));

    private static async Task<AuthenticationException> AuthorizeExpectingFailureAsync(
        HttpResponseMessage oauthResponse,
        HttpResponseMessage bridgeResponse)
    {
        var callbackUri = CreateAvailableLoopbackUri();
        var launcher = new LoopbackCallbackLauncher(callbackUri);
        var service = new ZiapBrowserAuthorizationService(
            new HttpClient(new AsyncStubHttpMessageHandler(request => Task.FromResult(
                request.RequestUri?.AbsolutePath == "/oauth/token" ? oauthResponse : bridgeResponse))),
            launcher,
            new Uri("https://identity.example.test/oauth/authorize"),
            new Uri("https://identity.example.test/oauth/token"),
            new Uri("https://identity.example.test/oauth/firebase/custom-token"),
            callbackUri,
            authorizationTimeout: TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() => service.AuthorizeAsync());
        await launcher.CallbackCompleted!;
        Assert.Contains("Accesso non completato", launcher.CallbackResponseBody);
        return exception;
    }

    private static void AssertNoSensitiveTokens(params AuthenticationException[] exceptions)
    {
        foreach (var exception in exceptions)
        {
            Assert.DoesNotContain("oauth-access-token", exception.ToString());
            Assert.DoesNotContain("firebase-custom-token", exception.ToString());
        }
    }

    private static Uri CreateAvailableLoopbackUri()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return new Uri($"http://127.0.0.1:{port}/auth/callback/");
    }

    private static Dictionary<string, string> ParseForm(string value) =>
        value.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                pair => Uri.UnescapeDataString(
                    (pair.Length > 1 ? pair[1] : string.Empty).Replace('+', ' ')));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class StubAuthorizationService : IZiapAuthorizationService
    {
        public Task<ZiapAuthorizationResult> AuthorizeAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ZiapAuthorizationResult(
                "firebase-custom-token",
                new AuthenticationAccount(
                    "uid-123",
                    "YuukiToyaro",
                    "yuuki@example.test")));
    }

    private sealed class StubZiapAppSessionService : IZiapAppSessionService
    {
        public Task<ZiapApplicationSessionResult> CompleteAsync(
            string temporaryFirebaseIdToken,
            string expectedUid,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ZiapApplicationSessionResult("app-session-token"));
    }

    private sealed class MemoryCredentialStore : ISecureCredentialStore
    {
        public string? Value { get; private set; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Value);

        public Task WriteAsync(string secret, CancellationToken cancellationToken = default)
        {
            Value = secret;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            Value = null;
            return Task.CompletedTask;
        }
    }

    private sealed class LoopbackCallbackLauncher(Uri callbackUri) : IExternalUriLauncher
    {
        public Uri? AuthorizationUri { get; private set; }

        public Task? CallbackCompleted { get; private set; }
        public Uri? CallbackRequestUri { get; private set; }
        public string? CallbackResponseBody { get; private set; }

        public void Open(Uri uri)
        {
            AuthorizationUri = uri;
            var query = ParseForm(uri.Query.TrimStart('?'));
            var callback = new UriBuilder(callbackUri)
            {
                Query = $"code=authorization-code&state={Uri.EscapeDataString(query["state"])}",
            }.Uri;
            CallbackRequestUri = callback;
            CallbackCompleted = Task.Run(async () =>
            {
                using var client = new HttpClient();
                using var response = await client.GetAsync(callback);
                response.EnsureSuccessStatusCode();
                CallbackResponseBody = await response.Content.ReadAsStringAsync();
            });
        }
    }

    private sealed class AsyncStubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
