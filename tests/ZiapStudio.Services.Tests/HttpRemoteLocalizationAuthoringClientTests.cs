using System.Net;
using System.Text;
using ZiapStudio.Core.Localization;
using ZiapStudio.Services.Authentication;
using ZiapStudio.Services.Integration.Remote;

namespace ZiapStudio.Services.Tests;

public sealed class HttpRemoteLocalizationAuthoringClientTests
{
    [Fact]
    public async Task Patch_UsesBearerAndTypedPathEnvelopeWithoutDeveloperToken()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            captured = request;
            capturedBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(
                """
                {"projectId":"fusion-hexella-dive","locale":"it","file":"dialogue/mdv.json","content":"[{}]\n","currentVersionId":"v1","currentChecksum":"current","stagingBasedOnVersionId":"v1","stagingChecksum":"staging","source":"staging","lock":{"owner":"Polka","acquiredAt":"2026-10-04T00:00:00.000Z","expiresAt":"2026-10-04T00:02:00.000Z","isOwnedByCurrentUser":true}}
                """);
        });
        var client = CreateClient(handler, new StubIdTokenProvider("firebase-id-token"));

        var result = await client.PatchStagingAsync(
            "fusion-hexella-dive",
            "it",
            "dialogue/mdv.json",
            "v1",
            "staging",
            [new LocalizationScalarPatch
            {
                Path = [LocalizationPathSegment.Index(0), LocalizationPathSegment.Property("text")],
                ExpectedOldValue = "A",
                Value = "B",
            }]);

        Assert.Equal("Bearer", captured!.Headers.Authorization?.Scheme);
        Assert.Equal("firebase-id-token", captured.Headers.Authorization?.Parameter);
        Assert.False(captured.Headers.Contains("X-ZIAP-Studio-Token"));
        Assert.Contains("\"kind\":\"index\"", capturedBody);
        Assert.Contains("\"kind\":\"property\"", capturedBody);
        Assert.Contains("\"expectedStagingChecksum\":\"staging\"", capturedBody);
        Assert.Equal(LocalizationAuthoringSource.Staging, result.Source);
        Assert.Equal("staging", result.StagingChecksum);
    }

    [Fact]
    public async Task Claim_RequiresFirebaseIdentityAndReadsLockEnvelope()
    {
        var client = CreateClient(
            new StubHttpMessageHandler(_ => Task.FromResult(JsonResponse(
                """{"lock":{"owner":"Polka","acquiredAt":"2026-10-04T00:00:00.000Z","expiresAt":"2026-10-04T00:02:00.000Z","isOwnedByCurrentUser":true}}"""))),
            new StubIdTokenProvider("firebase-id-token"));

        var lockInfo = await client.ClaimLockAsync("fusion-hexella-dive", "it", "dialogue/mdv.json");

        Assert.Equal("Polka", lockInfo.Owner);
        Assert.True(lockInfo.IsOwnedByCurrentUser);

        var unauthenticated = CreateClient(
            new StubHttpMessageHandler(_ => throw new InvalidOperationException("request must not be sent")),
            new StubIdTokenProvider(null));
        var exception = await Assert.ThrowsAsync<RemoteLocalizationAuthoringException>(() =>
            unauthenticated.ClaimLockAsync("fusion-hexella-dive", "it", "dialogue/mdv.json"));
        Assert.Equal(RemoteLocalizationAuthoringFailure.Unauthorized, exception.Failure);
    }

    [Fact]
    public async Task Append_UsesOperationIdClosedTemplateAndReturnsAuthoritativePath()
    {
        string? body = null;
        var client = CreateClient(new StubHttpMessageHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return JsonResponse(
                """
                {"operationId":"b6f0300c-7791-4c34-b2d4-7df4c7afb7da","assignedIndex":87,"entryPath":[{"kind":"index","index":0},{"kind":"property","name":"DestinyOfBirth"},{"kind":"index","index":0},{"kind":"property","name":"newPrologoStory"},{"kind":"index","index":87}],"projectId":"fusion-hexella-dive","locale":"it","file":"dialogue/mdv.json","content":"[{}]\n","currentVersionId":"v1","currentChecksum":"current","stagingBasedOnVersionId":"v1","stagingChecksum":"staging","source":"staging","isIdempotentReplay":true,"lock":{"isOwnedByCurrentUser":true}}
                """);
        }), new StubIdTokenProvider("firebase-id-token"));

        var result = await client.AppendStagingEntryAsync(new LocalizationAppendRequest
        {
            ProjectId = "fusion-hexella-dive",
            Locale = "it",
            SourceFile = "dialogue/mdv.json",
            OperationId = "b6f0300c-7791-4c34-b2d4-7df4c7afb7da",
            BasedOnVersionId = "v1",
            ExpectedStagingChecksum = "staging",
            ExpectedArrayLength = 87,
            BranchPath =
            [
                LocalizationPathSegment.Index(0), LocalizationPathSegment.Property("DestinyOfBirth"),
                LocalizationPathSegment.Index(0), LocalizationPathSegment.Property("newPrologoStory"),
            ],
            Template = "dialogue",
            Speaker = "Ilan",
            Text = "E allora da dove è iniziato tutto?",
        });

        Assert.Equal(87, result.AssignedIndex);
        Assert.True(result.IsIdempotentReplay);
        Assert.Equal("newPrologoStory", result.EntryPath[^2].PropertyName);
        Assert.Equal(87, result.EntryPath[^1].ArrayIndex);
        Assert.Contains("\"operationId\":\"b6f0300c", body);
        Assert.Contains("\"expectedArrayLength\":87", body);
        Assert.Contains("\"template\":\"dialogue\"", body);
        Assert.Contains("\"name\":\"Ilan\"", body);
        Assert.Contains("\"text\":", body);
    }

    private static HttpRemoteLocalizationAuthoringClient CreateClient(
        HttpMessageHandler handler,
        IIdTokenProvider tokens) => new(
        new HttpClient(handler),
        new Uri("https://api.example.test/get"),
        new Uri("https://api.example.test/claim"),
        new Uri("https://api.example.test/renew"),
        new Uri("https://api.example.test/release"),
        new Uri("https://api.example.test/patch"),
        new Uri("https://api.example.test/append"),
        tokens);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubIdTokenProvider(string? token) : IIdTokenProvider
    {
        public Task<string?> GetValidIdTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(token);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
}
}
