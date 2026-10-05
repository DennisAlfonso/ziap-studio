using System.Security.Cryptography;
using System.Text;
using ZiapStudio.Services;
using ZiapStudio.Services.ProjectSafety;

namespace ZiapStudio.Services.Tests;

public sealed class ProjectWriteCoordinatorTests
{
    [Fact]
    public void Classifier_AppliesRpgMakerExceptionsBeforeSharedSubtrees()
    {
        using var workspace = new TestWorkspace();
        var classifier = new ProjectFileClassifier();

        Assert.Equal(ProjectFileOwnership.RpgMakerOwned,
            classifier.Classify(workspace.RootPath, workspace.WriteFile("data/Map007.json", "{}")));
        Assert.Equal(ProjectFileOwnership.RpgMakerOwned,
            classifier.Classify(workspace.RootPath, workspace.WriteFile("data/Actors.json", "[]")));
        Assert.Equal(ProjectFileOwnership.RpgMakerOwned,
            classifier.Classify(workspace.RootPath, workspace.WriteFile("js/plugins.js", "[]")));
        Assert.Equal(ProjectFileOwnership.SharedProject,
            classifier.Classify(workspace.RootPath, workspace.WriteFile("data/fusion/audio.json", "{}")));
        Assert.Equal(ProjectFileOwnership.SharedProject,
            classifier.Classify(workspace.RootPath, workspace.WriteFile("locales/it/dialogue/mdv.json", "[]")));
        Assert.Equal(ProjectFileOwnership.StudioOwned,
            classifier.Classify(workspace.RootPath, workspace.WriteFile(".ziap/project.json", "{}")));
    }

    [Fact]
    public async Task Replace_UsesExpectedHashThenReturnsNewSnapshot()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.WriteFile("locales/it/dialogue/mdv.json", "[]");
        var coordinator = CreateCoordinator(workspace.RootPath);

        var result = await coordinator.WriteAsync(Replace(workspace.RootPath, target, "[\"updated\"]", Hash("[]")));

        Assert.Equal(ProjectFileOwnership.SharedProject, result.Ownership);
        Assert.Equal("[\"updated\"]", await File.ReadAllTextAsync(target));
        Assert.Equal(Hash("[\"updated\"]"), result.Snapshot.ContentHash);
    }

    [Fact]
    public async Task Write_RejectsPathTraversalOutsideOpenedProject()
    {
        using var workspace = new TestWorkspace();
        var coordinator = CreateCoordinator(workspace.RootPath);
        var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");

        var exception = await Assert.ThrowsAsync<ProjectWriteException>(() =>
            coordinator.WriteAsync(new ProjectWriteRequest
            {
                ProjectRoot = workspace.RootPath,
                TargetPath = outside,
                Operation = ProjectWriteOperation.CreateNew,
                Contents = Encoding.UTF8.GetBytes("{}"),
            }));

        Assert.Equal(ProjectWriteFailure.InvalidProjectPath, exception.Failure);
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task Replace_RejectsExternalChangesBeforeAndDuringTempWrite()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.WriteFile("locales/it/dialogue/mdv.json", "[]");
        var coordinator = CreateCoordinator(workspace.RootPath);
        var expected = Hash("[]");
        await File.WriteAllTextAsync(target, "[\"external-before\"]");

        var before = await Assert.ThrowsAsync<ProjectWriteException>(() =>
            coordinator.WriteAsync(Replace(workspace.RootPath, target, "[\"studio\"]", expected)));
        Assert.Equal(ProjectWriteFailure.ExternalModification, before.Failure);
        Assert.Equal("[\"external-before\"]", await File.ReadAllTextAsync(target));

        await File.WriteAllTextAsync(target, "[]");
        var during = await Assert.ThrowsAsync<ProjectWriteException>(() =>
            coordinator.WriteAsync(Replace(
                workspace.RootPath,
                target,
                "[\"studio\"]",
                expected,
                (_, _) =>
                {
                    File.WriteAllText(target, "[\"external-during\"]");
                    return Task.CompletedTask;
                })));
        Assert.Equal(ProjectWriteFailure.ExternalModification, during.Failure);
        Assert.Equal("[\"external-during\"]", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task ReplaceAndCreate_RejectMissingOrNewTargetAndPreserveOriginalOnFailure()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.WriteFile("locales/it/dialogue/mdv.json", "[]");
        var coordinator = CreateCoordinator(workspace.RootPath);
        var expected = Hash("[]");

        File.Delete(target);
        var deleted = await Assert.ThrowsAsync<ProjectWriteException>(() =>
            coordinator.WriteAsync(Replace(workspace.RootPath, target, "[1]", expected)));
        Assert.Equal(ProjectWriteFailure.ExternalModification, deleted.Failure);

        var newTarget = Path.Combine(workspace.RootPath, "data", "fusion", "audio.json");
        var appeared = await Assert.ThrowsAsync<ProjectWriteException>(() =>
            coordinator.WriteAsync(new ProjectWriteRequest
            {
                ProjectRoot = workspace.RootPath,
                TargetPath = newTarget,
                Operation = ProjectWriteOperation.CreateNew,
                Contents = Encoding.UTF8.GetBytes("{}"),
                Validator = (_, _) =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newTarget)!);
                    File.WriteAllText(newTarget, "{\"external\":true}");
                    return Task.CompletedTask;
                },
            }));
        Assert.Equal(ProjectWriteFailure.ExternalModification, appeared.Failure);
        Assert.Equal("{\"external\":true}", await File.ReadAllTextAsync(newTarget));

        var original = workspace.WriteFile("locales/it/dialogue/other.json", "[]");
        var failed = await Assert.ThrowsAsync<ProjectWriteException>(() =>
            coordinator.WriteAsync(Replace(workspace.RootPath, original, "{", Hash("[]"))));
        Assert.Equal(ProjectWriteFailure.AtomicWriteFailed, failed.Failure);
        Assert.Equal("[]", await File.ReadAllTextAsync(original));
    }

    [Fact]
    public async Task RpgMakerMode_BlocksOnlyRpgMakerOwnedFilesUntilRevalidation()
    {
        using var workspace = new TestWorkspace();
        var map = workspace.WriteFile("data/Map001.json", "{\"events\":[]}");
        var actors = workspace.WriteFile("data/Actors.json", "[]");
        var commonEvents = workspace.WriteFile("data/CommonEvents.json", "[]");
        var plugin = workspace.WriteFile("js/plugins.js", "[]");
        var shared = workspace.WriteFile("data/fusion/audio.json", "{}");
        var provider = new FakeProcessProvider();
        var monitor = new RpgMakerProcessMonitor(provider);
        var coordinator = new ProjectWriteCoordinator(new FileSystemService(), processMonitor: monitor);
        coordinator.OpenProject(workspace.RootPath);
        Assert.Equal(ProjectSafetyState.Safe, coordinator.SafetyState);

        provider.IsRunning = true;
        monitor.Refresh();
        Assert.Equal(ProjectSafetyState.ProtectedCoexistence, coordinator.SafetyState);
        foreach (var target in new[] { map, actors, commonEvents, plugin })
        {
            var expectedHash = Hash(await File.ReadAllTextAsync(target));
            var blocked = await Assert.ThrowsAsync<ProjectWriteException>(() =>
                coordinator.WriteAsync(Replace(workspace.RootPath, target, "[]", expectedHash)));
            Assert.Equal(ProjectWriteFailure.RpgMakerActive, blocked.Failure);
        }

        await coordinator.WriteAsync(Replace(workspace.RootPath, shared, "{\"allowed\":true}", Hash("{}")));
        await coordinator.WriteAsync(new ProjectWriteRequest
        {
            ProjectRoot = workspace.RootPath,
            TargetPath = Path.Combine(workspace.RootPath, ".ziap", "project.json"),
            Operation = ProjectWriteOperation.CreateNew,
            Contents = Encoding.UTF8.GetBytes("{}"),
        });

        provider.IsRunning = false;
        monitor.Refresh();
        Assert.Equal(ProjectSafetyState.RevalidationRequired, coordinator.SafetyState);
        var revalidationRequired = await Assert.ThrowsAsync<ProjectWriteException>(() =>
            coordinator.WriteAsync(Replace(workspace.RootPath, map, "{\"events\":[1]}", Hash("{\"events\":[]}"))));
        Assert.Equal(ProjectWriteFailure.RevalidationRequired, revalidationRequired.Failure);

        await coordinator.RevalidateAsync(workspace.RootPath, [Snapshot(map)]);
        await coordinator.WriteAsync(Replace(workspace.RootPath, map, "{\"events\":[1]}", Hash("{\"events\":[]}")));
        Assert.Equal(ProjectSafetyState.Safe, coordinator.SafetyState);
    }

    [Fact]
    public async Task Lease_BlocksSecondStudioWriterAndHashIsStillCheckedAfterRelease()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.WriteFile("locales/it/dialogue/mdv.json", "[]");
        var provider = new FakeProcessProvider();
        var monitorA = new RpgMakerProcessMonitor(provider);
        var monitorB = new RpgMakerProcessMonitor(provider);
        var coordinatorA = new ProjectWriteCoordinator(
            new FileSystemService(), processMonitor: monitorA,
            leaseTimeout: TimeSpan.FromMilliseconds(100), leaseRetryDelay: TimeSpan.FromMilliseconds(10));
        var coordinatorB = new ProjectWriteCoordinator(
            new FileSystemService(), processMonitor: monitorB,
            leaseTimeout: TimeSpan.FromMilliseconds(100), leaseRetryDelay: TimeSpan.FromMilliseconds(10));
        coordinatorA.OpenProject(workspace.RootPath);
        coordinatorB.OpenProject(workspace.RootPath);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = coordinatorA.WriteAsync(Replace(
            workspace.RootPath, target, "[\"first\"]", Hash("[]"), async (_, _) =>
            {
                entered.SetResult();
                await release.Task;
            }));
        await entered.Task;
        var concurrent = await Assert.ThrowsAsync<ProjectWriteException>(() =>
            coordinatorB.WriteAsync(Replace(workspace.RootPath, target, "[\"second\"]", Hash("[]"))));
        Assert.Equal(ProjectWriteFailure.ConcurrentStudioWriter, concurrent.Failure);

        release.SetResult();
        await first;
        await coordinatorB.WriteAsync(Replace(workspace.RootPath, target, "[\"second\"]", Hash("[\"first\"]")));
        Assert.Equal("[\"second\"]", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task ChangeMonitor_CoalescesEventsAndRecognizesSelfWritesByHash()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.WriteFile("locales/it/dialogue/mdv.json", "[]");
        var selfWrites = new SelfWriteRegistry();
        var coordinator = new ProjectWriteCoordinator(new FileSystemService(), selfWrites: selfWrites);
        coordinator.OpenProject(workspace.RootPath);
        using var monitor = new ProjectChangeMonitor(new FileSystemService(), selfWrites, startWatching: false);
        monitor.OpenProject(workspace.RootPath);
        var notifications = new List<ProjectChangeNotification>();
        monitor.Changed += (_, notification) => notifications.Add(notification);

        await coordinator.WriteAsync(Replace(workspace.RootPath, target, "[\"self\"]", Hash("[]")));
        monitor.Queue(ProjectChangeKind.Changed, target);
        monitor.Queue(ProjectChangeKind.Changed, target);
        await monitor.FlushAsync();
        var self = Assert.Single(notifications);
        Assert.Equal(ProjectChangeOrigin.SelfWrite, self.Origin);

        notifications.Clear();
        await File.WriteAllTextAsync(target, "[\"external\"]");
        monitor.Queue(ProjectChangeKind.Changed, target);
        await monitor.FlushAsync();
        Assert.Equal(ProjectChangeOrigin.ExternalWrite, Assert.Single(notifications).Origin);

        notifications.Clear();
        var created = Path.Combine(Path.GetDirectoryName(target)!, "created.json");
        await File.WriteAllTextAsync(created, "[]");
        monitor.Queue(ProjectChangeKind.Created, created);
        monitor.Queue(ProjectChangeKind.Created, created);
        await monitor.FlushAsync();
        var creation = Assert.Single(notifications);
        Assert.Equal(ProjectChangeKind.Created, creation.Change.Kind);
        Assert.Equal(ProjectChangeOrigin.ExternalWrite, creation.Origin);

        notifications.Clear();
        File.Delete(created);
        monitor.Queue(ProjectChangeKind.Deleted, created);
        await monitor.FlushAsync();
        var deletion = Assert.Single(notifications);
        Assert.Equal(ProjectChangeKind.Deleted, deletion.Change.Kind);
        Assert.Equal(ProjectChangeOrigin.ExternalWrite, deletion.Origin);

        notifications.Clear();
        var renamed = Path.Combine(Path.GetDirectoryName(target)!, "renamed.json");
        File.Move(target, renamed);
        monitor.Queue(ProjectChangeKind.Renamed, renamed, target);
        await monitor.FlushAsync();
        var rename = Assert.Single(notifications);
        Assert.Equal(ProjectChangeKind.Renamed, rename.Change.Kind);
        Assert.Equal(target, rename.Change.OldPath);
        Assert.Equal(ProjectChangeOrigin.ExternalWrite, rename.Origin);
    }

    [Fact]
    public async Task ChangeMonitor_CanCloseReopenAndDisposeWithoutLeakingEvents()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.WriteFile("locales/it/dialogue/mdv.json", "[]");
        using var monitor = new ProjectChangeMonitor(
            new FileSystemService(), new SelfWriteRegistry(), startWatching: false);
        var notifications = new List<ProjectChangeNotification>();
        monitor.Changed += (_, notification) => notifications.Add(notification);

        monitor.OpenProject(workspace.RootPath);
        monitor.Queue(ProjectChangeKind.Changed, target);
        await monitor.FlushAsync();
        Assert.Single(notifications);

        monitor.CloseProject();
        notifications.Clear();
        monitor.Queue(ProjectChangeKind.Changed, target);
        await monitor.FlushAsync();
        Assert.Empty(notifications);

        monitor.OpenProject(workspace.RootPath);
        monitor.Queue(ProjectChangeKind.Changed, target);
        await monitor.FlushAsync();
        Assert.Single(notifications);

        monitor.Dispose();
        Assert.Throws<ObjectDisposedException>(() => monitor.OpenProject(workspace.RootPath));
    }

    private static ProjectWriteCoordinator CreateCoordinator(string root)
    {
        var coordinator = new ProjectWriteCoordinator(new FileSystemService());
        coordinator.OpenProject(root);
        return coordinator;
    }

    private static ProjectWriteRequest Replace(
        string root,
        string path,
        string contents,
        string expectedHash,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task>? validator = null) => new()
    {
        ProjectRoot = root,
        TargetPath = path,
        Operation = ProjectWriteOperation.ReplaceExisting,
        Contents = Encoding.UTF8.GetBytes(contents),
        ExpectedContentHash = expectedHash,
        Validator = validator,
    };

    private static string Hash(string contents) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(contents)));

    private static ZiapStudio.Core.Editing.DocumentSourceSnapshot Snapshot(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return new()
        {
            SourcePath = path,
            ContentHash = Convert.ToHexString(SHA256.HashData(bytes)),
            Length = bytes.LongLength,
            LastWriteTimeUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero),
            LoadedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private sealed class FakeProcessProvider : IRpgMakerProcessProvider
    {
        public bool IsRunning { get; set; }

        public bool IsRpgMakerRunning() => IsRunning;
    }
}
