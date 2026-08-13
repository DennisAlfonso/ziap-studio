using System.Net;
using System.Text;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Authentication;
using ZiapStudio.Services.Integration.Remote;

namespace ZiapStudio.Services.Tests;

public sealed class RemoteLocalizationServiceTests
{
    [Fact]
    public async Task GetStatusAsync_ReportsAlignedDifferentAndMissingFiles()
    {
        using var workspace = new TestWorkspace();
        var alignedContents = "[{\"nome\":\"Viaggiamondi\"}]";
        workspace.WriteFile("locales/it/db.json", alignedContents);
        workspace.WriteFile("locales/it/main.json", "{\"locale\":true}");
        workspace.WriteFile("locales/it/only-local.json", "{}");
        var manifest = new RemoteLocalizationManifest
        {
            ProjectId = "fusion-hexella-dive",
            Generation = "generation-1",
            Files =
            [
                CreateRemote("it", "db.json", LocalizationChecksum.Compute(
                    Encoding.UTF8.GetBytes(alignedContents),
                    LocalizationChecksum.LegacyFnv1A32),
                    LocalizationChecksum.LegacyFnv1A32),
                CreateRemote("it", "main.json", new string('a', 64), LocalizationChecksum.Sha256),
                CreateRemote("it", "only-remote.json", new string('b', 64), LocalizationChecksum.Sha256),
            ],
        };
        var service = new RemoteLocalizationService(
            new FileSystemService(),
            new StubRemoteLocalizationClient(manifest));

        var status = await service.GetStatusAsync(CreateProject(workspace.RootPath));

        Assert.Equal("generation-1", status.Generation);
        Assert.Equal(RemoteLocalizationAlignment.Aligned, Find(status, "db.json").Alignment);
        Assert.Equal(RemoteLocalizationAlignment.Different, Find(status, "main.json").Alignment);
        Assert.Equal(RemoteLocalizationAlignment.MissingRemote, Find(status, "only-local.json").Alignment);
        Assert.Equal(RemoteLocalizationAlignment.MissingLocal, Find(status, "only-remote.json").Alignment);
    }

    [Fact]
    public async Task GetStatusAsync_PreservesNestedLocalizationPaths()
    {
        using var workspace = new TestWorkspace();
        var contents = "{\"dialogue\":true}";
        workspace.WriteFile("locales/it/dialogue/mdv.json", contents);
        var checksum = LocalizationChecksum.Compute(
            Encoding.UTF8.GetBytes(contents),
            LocalizationChecksum.Sha256);
        var service = new RemoteLocalizationService(
            new FileSystemService(),
            new StubRemoteLocalizationClient(new RemoteLocalizationManifest
            {
                ProjectId = "fusion-hexella-dive",
                Files = [CreateRemote("it", "dialogue/mdv.json", checksum, "sha-256")],
            }));

        var status = await service.GetStatusAsync(CreateProject(workspace.RootPath));

        var file = Assert.Single(status.Files);
        Assert.Equal("dialogue/mdv.json", file.File);
        Assert.Equal(RemoteLocalizationAlignment.Aligned, file.Alignment);
    }

    [Fact]
    public async Task HttpClient_SendsDevelopmentTokenAndParsesManifest()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "projectId": "fusion-hexella-dive",
                      "generation": "abc",
                      "files": [{
                        "locale": "it",
                        "file": "db.json",
                        "versionId": "loc-1",
                        "checksum": "12345678",
                        "checksumAlgorithm": "fnv1a32"
                      }]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        var client = new HttpRemoteLocalizationClient(
            new HttpClient(handler),
            new Uri("https://api.example.test/manifest"),
            "development-token");

        var manifest = await client.GetManifestAsync("fusion-hexella-dive");

        Assert.NotNull(capturedRequest);
        Assert.Equal(
            "https://api.example.test/manifest?projectId=fusion-hexella-dive",
            capturedRequest.RequestUri?.AbsoluteUri);
        Assert.Equal(
            "development-token",
            Assert.Single(capturedRequest.Headers.GetValues("X-ZIAP-Studio-Token")));
        Assert.Equal("abc", manifest.Generation);
        Assert.Equal("db.json", Assert.Single(manifest.Files).File);
    }

    [Fact]
    public async Task HttpClient_MapsUnauthorizedResponseWithoutLeakingToken()
    {
        var client = new HttpRemoteLocalizationClient(
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(
                HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    "{\"error\":{\"message\":\"Autenticazione richiesta.\"}}"),
            })),
            new Uri("https://api.example.test/manifest"),
            "secret-value");

        var exception = await Assert.ThrowsAsync<RemoteLocalizationException>(() =>
            client.GetManifestAsync("fusion-hexella-dive"));

        Assert.Contains("non configurato o non autorizzato", exception.Message);
        Assert.DoesNotContain("secret-value", exception.Message);
    }

    [Fact]
    public async Task HttpClient_PrefersFirebaseBearerTokenOverDevelopmentToken()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "projectId": "fusion-hexella-dive",
                      "files": []
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        var client = new HttpRemoteLocalizationClient(
            new HttpClient(handler),
            new Uri("https://api.example.test/manifest"),
            new Uri("https://api.example.test/published-file"),
            new StubIdTokenProvider("firebase-id-token"),
            "development-token");

        await client.GetManifestAsync("fusion-hexella-dive");

        Assert.Equal("Bearer", capturedRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("firebase-id-token", capturedRequest?.Headers.Authorization?.Parameter);
        Assert.False(capturedRequest?.Headers.Contains("X-ZIAP-Studio-Token"));
    }

    [Fact]
    public async Task HttpClient_DownloadsExactPublishedVersionWithBearerToken()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"published\":true}", Encoding.UTF8, "application/json"),
            };
            response.Headers.Add("X-ZIAP-Version-Id", "published-v18");
            response.Headers.Add("X-ZIAP-Checksum", "12345678");
            response.Headers.Add("X-ZIAP-Checksum-Algorithm", "fnv1a32");
            return response;
        });
        var client = new HttpRemoteLocalizationClient(
            new HttpClient(handler),
            new Uri("https://api.example.test/manifest"),
            new Uri("https://api.example.test/published-file"),
            new StubIdTokenProvider("firebase-id-token"));

        var published = await client.GetPublishedFileAsync(
            "fusion-hexella-dive",
            new RemoteLocalizationManifestFile
            {
                Locale = "it",
                File = "dialogue/db.json",
                VersionId = "published-v18",
                Checksum = "12345678",
                ChecksumAlgorithm = "fnv1a32",
            });

        Assert.Equal("Bearer", capturedRequest?.Headers.Authorization?.Scheme);
        Assert.Contains("projectId=fusion-hexella-dive", capturedRequest?.RequestUri?.Query);
        Assert.Contains("file=dialogue%2Fdb.json", capturedRequest?.RequestUri?.Query);
        Assert.Contains("versionId=published-v18", capturedRequest?.RequestUri?.Query);
        Assert.Equal("published-v18", published.VersionId);
        Assert.Equal("{\"published\":true}", Encoding.UTF8.GetString(published.Contents));
    }

    private static RemoteLocalizationManifestFile CreateRemote(
        string locale,
        string file,
        string checksum,
        string algorithm) => new()
    {
        Locale = locale,
        File = file,
        VersionId = "loc-1",
        Checksum = checksum,
        ChecksumAlgorithm = algorithm,
    };

    private static RemoteLocalizationFileStatus Find(
        RemoteLocalizationWorkspaceStatus status,
        string file) => Assert.Single(status.Files.Where(candidate => candidate.File == file));

    private static ZiapProject CreateProject(string path) => new()
    {
        Id = "fusion-hexella-dive",
        Name = "Fusion: Hexella Dive",
        ProjectType = KnownProjectTypes.RpgMakerMz,
        Path = path,
        IsZiapInitialized = true,
    };

    private sealed class StubRemoteLocalizationClient(RemoteLocalizationManifest manifest)
        : IRemoteLocalizationClient
    {
        public Task<RemoteLocalizationManifest> GetManifestAsync(
            string projectId,
            CancellationToken cancellationToken = default) => Task.FromResult(manifest);

        public Task<PublishedLocalizationFile> GetPublishedFileAsync(
            string projectId,
            RemoteLocalizationManifestFile file,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubIdTokenProvider(string token) : IIdTokenProvider
    {
        public Task<string?> GetValidIdTokenAsync(
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(token);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
