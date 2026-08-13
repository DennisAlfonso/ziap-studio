using System.Text;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Models;
using ZiapStudio.Services;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Integration.Remote;

namespace ZiapStudio.Services.Tests;

public sealed class PublishedLocalizationSyncTests
{
    [Fact]
    public async Task CompareAsync_ReportsSemanticJsonDifferencesWithoutWriting()
    {
        using var workspace = new TestWorkspace();
        const string localJson =
            "{\"1\":{\"name\":\"Locale\",\"onlyLocal\":true},\"same\":4}";
        const string publishedJson =
            "{\"1\":{\"name\":\"Pubblicato\",\"onlyRemote\":true},\"same\":4}";
        workspace.WriteFile("locales/it/db.json", localJson);
        var fixture = CreateFixture(workspace, publishedJson);

        var comparison = await fixture.Service.CompareAsync(fixture.Project, fixture.Status);

        Assert.Equal(3, comparison.Differences.Count);
        Assert.Equal(
            LocalizationDifferenceKind.Modified,
            Find(comparison, "1.name").Kind);
        Assert.Equal(
            LocalizationDifferenceKind.OnlyLocal,
            Find(comparison, "1.onlyLocal").Kind);
        Assert.Equal(
            LocalizationDifferenceKind.OnlyPublished,
            Find(comparison, "1.onlyRemote").Kind);
        Assert.Equal(localJson, await File.ReadAllTextAsync(comparison.DestinationPath));
    }

    [Fact]
    public async Task SynchronizeAsync_ReplacesExistingJsonAfterBaselineCheck()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("locales/it/db.json", "{\"name\":\"Locale\"}");
        const string publishedJson = "{\"name\":\"Pubblicato\"}";
        var fixture = CreateFixture(workspace, publishedJson);
        var comparison = await fixture.Service.CompareAsync(fixture.Project, fixture.Status);

        var result = await fixture.Service.SynchronizeAsync(
            fixture.Project,
            fixture.Status,
            comparison);

        Assert.False(result.Created);
        Assert.Equal(
            publishedJson,
            await File.ReadAllTextAsync(Path.Combine(
                workspace.RootPath,
                "locales",
                "it",
                "db.json")));
        Assert.False(File.Exists($"{result.DestinationPath}.ziap-tmp"));
    }

    [Fact]
    public async Task SynchronizeAsync_CreatesMissingNestedJson()
    {
        using var workspace = new TestWorkspace();
        const string publishedJson = "{\"quest\":true}";
        var fixture = CreateFixture(workspace, publishedJson, "quests/chapter1.json");
        var comparison = await fixture.Service.CompareAsync(fixture.Project, fixture.Status);

        var result = await fixture.Service.SynchronizeAsync(
            fixture.Project,
            fixture.Status,
            comparison);

        Assert.True(result.Created);
        Assert.Equal(publishedJson, await File.ReadAllTextAsync(result.DestinationPath));
    }

    [Fact]
    public async Task SynchronizeAsync_RefusesLocalChangesMadeAfterComparison()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("locales/it/db.json", "{\"name\":\"Prima\"}");
        var fixture = CreateFixture(workspace, "{\"name\":\"Pubblicato\"}");
        var comparison = await fixture.Service.CompareAsync(fixture.Project, fixture.Status);
        workspace.WriteFile("locales/it/db.json", "{\"name\":\"Modificato dopo\"}");

        await Assert.ThrowsAsync<ExternalDocumentModificationException>(() =>
            fixture.Service.SynchronizeAsync(fixture.Project, fixture.Status, comparison));

        Assert.Equal(
            "{\"name\":\"Modificato dopo\"}",
            await File.ReadAllTextAsync(comparison.DestinationPath));
    }

    private static LocalizationJsonDifference Find(
        PublishedLocalizationComparison comparison,
        string path) => Assert.Single(comparison.Differences.Where(item => item.Path == path));

    private static Fixture CreateFixture(
        TestWorkspace workspace,
        string publishedJson,
        string file = "db.json")
    {
        var contents = Encoding.UTF8.GetBytes(publishedJson);
        var checksum = LocalizationChecksum.Compute(
            contents,
            LocalizationChecksum.Sha256);
        var remote = new RemoteLocalizationManifestFile
        {
            Locale = "it",
            File = file,
            VersionId = "published-v18",
            Checksum = checksum,
            ChecksumAlgorithm = LocalizationChecksum.Sha256,
        };
        var status = new RemoteLocalizationFileStatus
        {
            Locale = "it",
            File = file,
            Alignment = File.Exists(Path.Combine(
                workspace.RootPath,
                "locales",
                "it",
                file.Replace('/', Path.DirectorySeparatorChar)))
                ? RemoteLocalizationAlignment.Different
                : RemoteLocalizationAlignment.MissingLocal,
            Remote = remote,
        };
        var project = new ZiapProject
        {
            Id = "fusion-hexella-dive",
            Name = "Fusion: Hexella Dive",
            ProjectType = KnownProjectTypes.RpgMakerMz,
            Path = workspace.RootPath,
            IsZiapInitialized = true,
        };
        var fileSystem = new FileSystemService();
        var client = new StubRemoteLocalizationClient(new PublishedLocalizationFile
        {
            ProjectId = project.Id,
            Locale = remote.Locale,
            File = remote.File,
            VersionId = remote.VersionId,
            Checksum = checksum,
            ChecksumAlgorithm = LocalizationChecksum.Sha256,
            Contents = contents,
        });
        var service = new PublishedLocalizationSyncService(
            fileSystem,
            client,
            new LocalizationJsonDiffService(),
            new PublishedLocalizationFileWriter(
                fileSystem,
                new AtomicJsonFileWriter(fileSystem)));
        return new Fixture(project, status, service);
    }

    private sealed record Fixture(
        ZiapProject Project,
        RemoteLocalizationFileStatus Status,
        PublishedLocalizationSyncService Service);

    private sealed class StubRemoteLocalizationClient(PublishedLocalizationFile published)
        : IRemoteLocalizationClient
    {
        public Task<RemoteLocalizationManifest> GetManifestAsync(
            string projectId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PublishedLocalizationFile> GetPublishedFileAsync(
            string projectId,
            RemoteLocalizationManifestFile file,
            CancellationToken cancellationToken = default) => Task.FromResult(published);
    }
}
