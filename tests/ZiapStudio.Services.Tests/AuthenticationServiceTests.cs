using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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
        var handler = new AsyncStubHttpMessageHandler(async request =>
        {
            tokenRequestBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(
                """
                {
                  "firebase_custom_token": "firebase-custom-token",
                  "user": {
                    "uid": "uid-123",
                    "nickname": "YuukiToyaro",
                    "email": "yuuki@example.test"
                  }
                }
                """);
        });
        var launcher = new LoopbackCallbackLauncher(callbackUri);
        var service = new ZiapBrowserAuthorizationService(
            new HttpClient(handler),
            launcher,
            new Uri("https://identity.example.test/oauth/authorize"),
            new Uri("https://identity.example.test/oauth/token"),
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
        var tokenForm = ParseForm(tokenRequestBody!);
        Assert.Equal("authorization-code", tokenForm["code"]);
        Assert.Equal(callbackUri.AbsoluteUri, tokenForm["redirect_uri"]);
        var expectedChallenge = Base64Url(
            SHA256.HashData(Encoding.ASCII.GetBytes(tokenForm["code_verifier"])));
        Assert.Equal(authorizationQuery["code_challenge"], expectedChallenge);
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
                  "expiresIn": "3600"
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
                  "expiresIn": "3600"
                }
                """)))),
            "public-api-key");
        var firstSession = new ZiapAuthenticationService(
            new StubAuthorizationService(),
            firstFirebase,
            store);

        await firstSession.SignInAsync();

        Assert.True(firstSession.IsAuthenticated);
        Assert.Equal("YuukiToyaro", firstSession.CurrentAccount?.DisplayName);
        Assert.NotNull(store.Value);
        Assert.Contains("first-refresh-token", store.Value);

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
            store);

        await restartedSession.InitializeAsync();

        Assert.True(restartedSession.IsAuthenticated);
        Assert.Equal("restored-id-token", await restartedSession.GetValidIdTokenAsync());
        Assert.Contains("rotated-refresh-token", store.Value);
        Assert.DoesNotContain("first-refresh-token", store.Value);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

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

        public void Open(Uri uri)
        {
            AuthorizationUri = uri;
            var query = ParseForm(uri.Query.TrimStart('?'));
            var callback = new UriBuilder(callbackUri)
            {
                Query = $"code=authorization-code&state={Uri.EscapeDataString(query["state"])}",
            }.Uri;
            CallbackCompleted = Task.Run(async () =>
            {
                using var client = new HttpClient();
                using var response = await client.GetAsync(callback);
                response.EnsureSuccessStatusCode();
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
