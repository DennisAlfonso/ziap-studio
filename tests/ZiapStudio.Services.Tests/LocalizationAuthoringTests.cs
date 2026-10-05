using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Models;
using ZiapStudio.Services;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Integration.Remote;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Tests;

public sealed class LocalizationAuthoringTests
{
    [Fact]
    public void BeginEditingAvailability_LockedSessionDoesNotThrowAndReturnsFalse()
    {
        var origin = CreateOrigin();
        var locked = StoryLocalizationAuthoringSession.Locked(origin, new LocalizationLockInfo
        {
            Owner = "another-author",
            IsOwnedByCurrentUser = false,
        });

        var exception = Record.Exception(() => StoryLocalizationAuthoringAvailability.CanBeginEditing(
            isAuthoringBusy: false,
            selectedBlock: CreateStoryBlock(origin),
            authoringSession: locked));

        Assert.Null(exception);
        Assert.False(StoryLocalizationAuthoringAvailability.CanBeginEditing(
            isAuthoringBusy: false,
            selectedBlock: CreateStoryBlock(origin),
            authoringSession: locked));
    }

    [Fact]
    public void BeginEditingAvailability_NoSessionAllowsEditableMasterOrigin()
    {
        Assert.True(StoryLocalizationAuthoringAvailability.CanBeginEditing(
            isAuthoringBusy: false,
            selectedBlock: CreateStoryBlock(CreateOrigin()),
            authoringSession: null));
    }

    [Fact]
    public void BeginEditingAvailability_BusyReturnsFalse()
    {
        Assert.False(StoryLocalizationAuthoringAvailability.CanBeginEditing(
            isAuthoringBusy: true,
            selectedBlock: CreateStoryBlock(CreateOrigin()),
            authoringSession: null));
    }

    [Fact]
    public void BeginEditingAvailability_NonMasterOriginReturnsFalse()
    {
        var nonMasterOrigin = CreateOrigin() with { Locale = "en" };

        Assert.False(StoryLocalizationAuthoringAvailability.CanBeginEditing(
            isAuthoringBusy: false,
            selectedBlock: CreateStoryBlock(nonMasterOrigin),
            authoringSession: null));
    }

    [Theory]
    [InlineData(StoryBlockKind.PluginCommand)]
    [InlineData(StoryBlockKind.Comment)]
    [InlineData(StoryBlockKind.Script)]
    public void BeginEditingAvailability_LocalizationLookingTechnicalContentIsReadOnly(StoryBlockKind kind)
    {
        Assert.False(StoryLocalizationAuthoringAvailability.CanBeginEditing(
            isAuthoringBusy: false,
            selectedBlock: CreateStoryBlock(CreateOrigin(), kind),
            authoringSession: null));
    }

    [Fact]
    public void BeginEditingAvailability_LocalizedChoiceIsEditableButLiteralChoiceIsNot()
    {
        Assert.True(StoryLocalizationAuthoringAvailability.CanBeginEditing(
            isAuthoringBusy: false,
            selectedBlock: CreateStoryBlock(CreateOrigin(), StoryBlockKind.Choices),
            authoringSession: null));
        Assert.False(StoryLocalizationAuthoringAvailability.CanBeginEditing(
            isAuthoringBusy: false,
            selectedBlock: new StoryBlock { Kind = StoryBlockKind.Choices, Title = "Choices" },
            authoringSession: null));
    }

    [Fact]
    public void BeginEditingAvailability_DirtySessionAllowsSameFile()
    {
        var origin = CreateOrigin();
        var session = CreateDirtyAuthoringSession(origin);

        Assert.True(StoryLocalizationAuthoringAvailability.CanBeginEditing(
            isAuthoringBusy: false,
            selectedBlock: CreateStoryBlock(origin),
            authoringSession: session));
    }

    [Fact]
    public void BeginEditingAvailability_DirtySessionBlocksDifferentFile()
    {
        var session = CreateDirtyAuthoringSession(CreateOrigin());
        var otherFileOrigin = CreateOrigin() with { SourceFile = "dialogue/other.json" };

        Assert.False(StoryLocalizationAuthoringAvailability.CanBeginEditing(
            isAuthoringBusy: false,
            selectedBlock: CreateStoryBlock(otherFileOrigin),
            authoringSession: session));
    }

    [Fact]
    public void EditSession_TracksScalarChangesUndoRedoDiscardAndSharedOrigins()
    {
        var origin = CreateOrigin();
        var sameLeaf = origin with { Path = "friendly.path" };
        var session = new LocalizationEditSession(
            CreateSnapshot("""[{"dialogue":[{"text":"A"}]}]"""),
            new DocumentSourceSnapshot { SourcePath = "fixture.json", ContentHash = "fixture" });

        Assert.True(session.CanEdit(origin));
        Assert.True(session.SetValue(origin, "B"));
        Assert.True(session.IsDirty);
        Assert.True(session.TryGetValue(sameLeaf, out var shared));
        Assert.Equal("B", shared);
        Assert.Single(session.ChangeSet.Changes);
        Assert.Equal("A", session.ChangeSet.Changes[0].OldValue);
        Assert.Equal("B", session.ChangeSet.Changes[0].NewValue);

        Assert.True(session.Undo());
        Assert.True(session.TryGetValue(origin, out var undone));
        Assert.Equal("A", undone);
        Assert.True(session.Redo());
        Assert.True(session.TryGetValue(origin, out var redone));
        Assert.Equal("B", redone);

        session.Discard();
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.True(session.TryGetValue(origin, out var discarded));
        Assert.Equal("A", discarded);
    }

    [Fact]
    public async Task Save_WritesCanonicalRemoteStagingInvalidatesCacheAndPreservesRpgMakerSources()
    {
        using var workspace = new TestWorkspace();
        const string source = """[{"DestinyOfBirth":[{"newPrologoStory":[{"name":"Polka","text":"A"}]}]}]""";
        const string canonicalStaging =
            "[\n  {\n    \"DestinyOfBirth\": [\n      {\n        \"newPrologoStory\": [\n          {\n            \"name\": \"Polka\",\n            \"text\": \"B\"\n          }\n        ]\n      }\n    ]\n  }\n]\n";
        workspace.WriteFile("locales/it/dialogue/mdv.json", source);
        var mapPath = workspace.WriteFile("data/Map001.json", "{\"events\":[]}");
        var commonEventsPath = workspace.WriteFile("data/CommonEvents.json", "[]");
        var originalMap = await File.ReadAllBytesAsync(mapPath);
        var originalCommonEvents = await File.ReadAllBytesAsync(commonEventsPath);
        var fileSystem = new FileSystemService();
        var localization = new LocalizationService([new FusionLocalizationProvider(fileSystem)]);
        var resolution = await localization.ResolveAsync(
            workspace.RootPath,
            "{mdv[0].DestinyOfBirth[0].newPrologoStory[0].text}");
        Assert.Equal("A", resolution!.ResolvedValue);
        Assert.NotNull(resolution.Origin);
        Assert.Equal(6, resolution.Origin.Segments.Count);
        Assert.Equal(0, resolution.Origin.Segments[0].ArrayIndex);
        Assert.Equal("DestinyOfBirth", resolution.Origin.Segments[1].PropertyName);

        var remote = new StubAuthoringClient(CreateSnapshot(source), CreateSnapshot(canonicalStaging));
        var service = new StoryLocalizationAuthoringService(
            remote,
            new DocumentSnapshotService(fileSystem),
            new ExternalModificationDetector(fileSystem, new DocumentSnapshotService(fileSystem)),
            new PublishedLocalizationFileWriter(fileSystem, new AtomicJsonFileWriter(fileSystem)),
            localization);
        var project = CreateProject(workspace.RootPath);
        var authoring = await service.BeginAsync(project, resolution.Origin!);
        Assert.NotNull(authoring.EditSession);
        authoring.EditSession!.SetValue(resolution.Origin!, "B");

        var saved = await service.SaveAsync(project, authoring);

        Assert.True(saved.RemoteSaved);
        Assert.True(saved.LocalMirrorSynchronized);
        Assert.True(remote.PatchCalled);
        Assert.Equal(canonicalStaging, await File.ReadAllTextAsync(
            Path.Combine(workspace.RootPath, "locales", "it", "dialogue", "mdv.json")));
        var after = await localization.ResolveAsync(
            workspace.RootPath,
            "{mdv[0].DestinyOfBirth[0].newPrologoStory[0].text}");
        Assert.Equal("B", after!.ResolvedValue);
        Assert.Equal(originalMap, await File.ReadAllBytesAsync(mapPath));
        Assert.Equal(originalCommonEvents, await File.ReadAllBytesAsync(commonEventsPath));
    }

    [Fact]
    public async Task Save_RejectsExternalLocalModificationBeforeRemotePatch()
    {
        using var workspace = new TestWorkspace();
        const string source = """[{"dialogue":[{"text":"A"}]}]""";
        workspace.WriteFile("locales/it/dialogue/mdv.json", source);
        var fileSystem = new FileSystemService();
        var remote = new StubAuthoringClient(CreateSnapshot(source), CreateSnapshot("[{\"dialogue\":[{\"text\":\"B\"}]}]"));
        var localization = new LocalizationService([new FusionLocalizationProvider(fileSystem)]);
        var service = new StoryLocalizationAuthoringService(
            remote,
            new DocumentSnapshotService(fileSystem),
            new ExternalModificationDetector(fileSystem, new DocumentSnapshotService(fileSystem)),
            new PublishedLocalizationFileWriter(fileSystem, new AtomicJsonFileWriter(fileSystem)),
            localization);
        var project = CreateProject(workspace.RootPath);
        var origin = CreateOrigin();
        var authoring = await service.BeginAsync(project, origin);
        authoring.EditSession!.SetValue(origin, "B");
        workspace.WriteFile("locales/it/dialogue/mdv.json", "[{\"dialogue\":[{\"text\":\"external\"}]}]");

        var result = await service.SaveAsync(project, authoring);

        Assert.True(result.IsConflict);
        Assert.False(remote.PatchCalled);
        Assert.Equal("[{\"dialogue\":[{\"text\":\"external\"}]}]", await File.ReadAllTextAsync(
            Path.Combine(workspace.RootPath, "locales", "it", "dialogue", "mdv.json")));
    }

    [Fact]
    public async Task Save_RemoteSuccessWithChangedMirrorStaysOutOfSyncAndRetryDoesNotOverwrite()
    {
        using var workspace = new TestWorkspace();
        const string source = """[{"dialogue":[{"text":"A"}]}]""";
        const string staged = "[{\"dialogue\":[{\"text\":\"B\"}]}]";
        workspace.WriteFile("locales/it/dialogue/mdv.json", source);
        var fileSystem = new FileSystemService();
        var remote = new StubAuthoringClient(
            CreateSnapshot(source),
            CreateSnapshot(staged),
            () => workspace.WriteFile("locales/it/dialogue/mdv.json", "[{\"dialogue\":[{\"text\":\"external\"}]}]"));
        var localization = new LocalizationService([new FusionLocalizationProvider(fileSystem)]);
        var service = new StoryLocalizationAuthoringService(
            remote,
            new DocumentSnapshotService(fileSystem),
            new ExternalModificationDetector(fileSystem, new DocumentSnapshotService(fileSystem)),
            new PublishedLocalizationFileWriter(fileSystem, new AtomicJsonFileWriter(fileSystem)),
            localization);
        var project = CreateProject(workspace.RootPath);
        var origin = CreateOrigin();
        var authoring = await service.BeginAsync(project, origin);
        authoring.EditSession!.SetValue(origin, "B");

        var save = await service.SaveAsync(project, authoring);
        var retry = await service.RetryMirrorAsync(project, authoring);

        Assert.True(save.RemoteSaved);
        Assert.False(save.LocalMirrorSynchronized);
        Assert.Equal(LocalizationAuthoringState.LocalOutOfSync, authoring.State);
        Assert.True(retry.RemoteSaved);
        Assert.False(retry.LocalMirrorSynchronized);
        Assert.Equal("[{\"dialogue\":[{\"text\":\"external\"}]}]", await File.ReadAllTextAsync(
            Path.Combine(workspace.RootPath, "locales", "it", "dialogue", "mdv.json")));
    }

    private static LocalizationReferenceOrigin CreateOrigin() => new()
    {
        Namespace = "mdv",
        Path = "0.dialogue.0.text",
        Locale = "it",
        SourceFile = "dialogue/mdv.json",
        Segments =
        [
            LocalizationPathSegment.Index(0),
            LocalizationPathSegment.Property("dialogue"),
            LocalizationPathSegment.Index(0),
            LocalizationPathSegment.Property("text"),
        ],
    };

    private static LocalizationAuthoringSnapshot CreateSnapshot(string content) => new()
    {
        ProjectId = "fusion-hexella-dive",
        Locale = "it",
        SourceFile = "dialogue/mdv.json",
        Content = content,
        CurrentVersionId = "v1",
        CurrentChecksum = "current",
        StagingChecksum = null,
        Source = LocalizationAuthoringSource.Published,
        Lock = new LocalizationLockInfo { IsOwnedByCurrentUser = true, AcquiredAt = "2026-10-04T00:00:00.000Z" },
    };

    private static StoryBlock CreateStoryBlock(
        LocalizationReferenceOrigin origin,
        StoryBlockKind kind = StoryBlockKind.Dialogue) => new()
    {
        Kind = kind,
        Title = "Polka",
        LocalizationOrigins = [origin],
    };

    private static StoryLocalizationAuthoringSession CreateDirtyAuthoringSession(
        LocalizationReferenceOrigin origin)
    {
        var editSession = new LocalizationEditSession(
            CreateSnapshot("""[{"dialogue":[{"text":"A"}]}]"""),
            new DocumentSourceSnapshot { SourcePath = "fixture.json", ContentHash = "fixture" });
        editSession.SetValue(origin, "B");
        return new StoryLocalizationAuthoringSession(
            origin,
            editSession,
            new LocalizationLockInfo { IsOwnedByCurrentUser = true });
    }

    private static ZiapProject CreateProject(string path) => new()
    {
        Id = "fusion-hexella-dive",
        Name = "Fusion: Hexella Dive",
        ProjectType = KnownProjectTypes.RpgMakerMz,
        Path = path,
        IsZiapInitialized = true,
    };

    private sealed class StubAuthoringClient(
        LocalizationAuthoringSnapshot initial,
        LocalizationAuthoringSnapshot saved,
        Action? onPatch = null) : IRemoteLocalizationAuthoringClient
    {
        public bool PatchCalled { get; private set; }

        public Task<LocalizationAuthoringSnapshot> GetAuthoringFileAsync(
            string projectId, string locale, string sourceFile, CancellationToken cancellationToken = default) =>
            Task.FromResult(initial);

        public Task<LocalizationLockInfo> ClaimLockAsync(
            string projectId, string locale, string sourceFile, CancellationToken cancellationToken = default) =>
            Task.FromResult(initial.Lock);

        public Task<LocalizationLockInfo> RenewLockAsync(
            string projectId, string locale, string sourceFile, string? acquiredAt,
            CancellationToken cancellationToken = default) => Task.FromResult(initial.Lock);

        public Task ReleaseLockAsync(
            string projectId, string locale, string sourceFile, string? acquiredAt,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LocalizationAuthoringSnapshot> PatchStagingAsync(
            string projectId, string locale, string sourceFile, string? basedOnVersionId,
            string? expectedStagingChecksum, IReadOnlyList<LocalizationScalarPatch> changes,
            CancellationToken cancellationToken = default)
        {
            PatchCalled = true;
            onPatch?.Invoke();
            return Task.FromResult(saved with
            {
                StagingChecksum = "staging",
                Source = LocalizationAuthoringSource.Staging,
            });
        }

        public Task<LocalizationAppendResult> AppendStagingEntryAsync(
            LocalizationAppendRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
