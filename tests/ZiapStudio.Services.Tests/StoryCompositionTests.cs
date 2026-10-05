using System.Text.Json;
using System.Text.Json.Nodes;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Models;
using ZiapStudio.Services;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Fusion.Story;
using ZiapStudio.Services.Integration.Remote;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Tests;

public sealed class StoryCompositionTests
{
    [Fact]
    public async Task Planner_InfersStructuredBranchAndInheritedPresentation()
    {
        using var workspace = new TestWorkspace();
        WriteMap(workspace);
        var snapshotService = new DocumentSnapshotService(new FileSystemService());
        var plan = await new StoryCompositionPlanner(snapshotService).CreateAsync(
            StoryCompositionOperationType.AddDialogue,
            MapTarget(),
            CreateAnchor(),
            workspace.RootPath,
            "Ilan",
            "E allora da dove è iniziato tutto?");

        Assert.Equal(2, plan.InsertionCommandIndex);
        Assert.Equal(0, plan.AnchorStartCommandIndex);
        Assert.Equal(1, plan.AnchorEndCommandIndex);
        Assert.Equal("Ilan", plan.Values.Speaker);
        Assert.Equal("Actor1", plan.Presentation.FaceName);
        Assert.Equal(2, plan.Presentation.PositionType);
        Assert.True(plan.Presentation.IsInherited);
        Assert.Equal(
            ["[0]", "DestinyOfBirth", "[0]", "newPrologoStory"],
            plan.LocalizationBranchPath.Select(DisplaySegment).ToArray());
        Assert.NotEqual(Guid.Empty, Guid.Parse(plan.OperationId));
    }

    [Fact]
    public async Task Writer_InsertsBeforeTerminalPreservesUnrelatedDataAndUnlinksOnlyBlock()
    {
        using var workspace = new TestWorkspace();
        var mapPath = WriteMap(workspace);
        var snapshots = new DocumentSnapshotService(new FileSystemService());
        var sourceSnapshot = await snapshots.CaptureAsync(mapPath);
        var plan = CreatePlan(sourceSnapshot);
        var writer = CreateWriter();
        LocalizationPathSegment[] entryPath = [.. plan.LocalizationBranchPath, LocalizationPathSegment.Index(1)];

        await writer.InsertAsync(workspace.RootPath, plan,
            StoryRpgMakerCommandGenerator.CreateCommittedCommands(plan, entryPath));

        var root = JsonNode.Parse(await File.ReadAllTextAsync(mapPath))!.AsObject();
        Assert.Equal("unchanged", root["customMapProperty"]!.GetValue<string>());
        var list = GetMapList(root);
        Assert.Equal([101, 401, 101, 401, 0], list.Select(command => command!["code"]!.GetValue<int>()).ToArray());
        Assert.Equal("{mdv[0].DestinyOfBirth[0].newPrologoStory[1].name}",
            list[2]! ["parameters"]![4]!.GetValue<string>());
        Assert.Equal("{mdv[0].DestinyOfBirth[0].newPrologoStory[1].text}",
            list[3]! ["parameters"]![0]!.GetValue<string>());
        Assert.Equal(1, list.Count(command => command!["code"]!.GetValue<int>() == 0));

        var afterInsert = await snapshots.CaptureAsync(mapPath);
        var insertedBlock = new StoryBlock
        {
            Kind = StoryBlockKind.Dialogue,
            Title = "Ilan",
            CommandStartIndex = 2,
            CommandEndIndex = 3,
            Indent = 0,
            SourceCommands =
            [
                new StoryRawCommand {Code = 101, Indent = 0, Parameters = "[\"Actor1\",0,0,2,\"{mdv[0].DestinyOfBirth[0].newPrologoStory[1].name}\"]"},
                new StoryRawCommand {Code = 401, Indent = 0, Parameters = "[\"{mdv[0].DestinyOfBirth[0].newPrologoStory[1].text}\"]"},
            ],
        };
        await writer.RemoveAsync(workspace.RootPath, MapTarget(), afterInsert, insertedBlock);

        var afterRemove = GetMapList(JsonNode.Parse(await File.ReadAllTextAsync(mapPath))!.AsObject());
        Assert.Equal([101, 401, 0], afterRemove.Select(command => command!["code"]!.GetValue<int>()).ToArray());
        Assert.Equal(1, afterRemove.Count(command => command!["code"]!.GetValue<int>() == 0));
    }

    [Fact]
    public async Task Writer_RejectsChangedSourceAndSupportsCommonEvents()
    {
        using var workspace = new TestWorkspace();
        WriteMap(workspace);
        var commonPath = workspace.WriteFile("data/CommonEvents.json", """
            [null,{"id":2,"name":"Epilogo","trigger":0,"switchId":0,"list":[
              {"code":101,"indent":0,"parameters":["Actor1",0,0,2,"{mdv[0].DestinyOfBirth[0].newPrologoStory[0].name}"]},
              {"code":401,"indent":0,"parameters":["{mdv[0].DestinyOfBirth[0].newPrologoStory[0].text}"]},
              {"code":0,"indent":0,"parameters":[]}]}]
            """);
        var snapshots = new DocumentSnapshotService(new FileSystemService());
        var commonSnapshot = await snapshots.CaptureAsync(commonPath);
        var plan = CreatePlan(commonSnapshot, CommonTarget());
        await CreateWriter().InsertAsync(workspace.RootPath, plan,
            StoryRpgMakerCommandGenerator.CreateCommittedCommands(
                plan, [.. plan.LocalizationBranchPath, LocalizationPathSegment.Index(1)]));
        var common = JsonNode.Parse(await File.ReadAllTextAsync(commonPath))!.AsArray()[1]!.AsObject()["list"]!.AsArray();
        Assert.Equal([101, 401, 101, 401, 0], common.Select(command => command!["code"]!.GetValue<int>()).ToArray());

        var mapPath = Path.Combine(workspace.RootPath, "data", "Map001.json");
        var stale = await snapshots.CaptureAsync(mapPath);
        await File.WriteAllTextAsync(mapPath, "{\"events\":[]}");
        await Assert.ThrowsAsync<StoryLocalSourceConflictException>(() => CreateWriter().RemoveAsync(
            workspace.RootPath, MapTarget(), stale, CreateAnchor()));
    }

    [Fact]
    public async Task Commit_RemoteAppendThenMapConflictRetainsRecoveryAndReusesAssignedPath()
    {
        using var workspace = new TestWorkspace();
        var mapPath = WriteMap(workspace);
        workspace.WriteFile("locales/it/dialogue/mdv.json", InitialMdv);
        var fileSystem = new FileSystemService();
        var snapshots = new DocumentSnapshotService(fileSystem);
        var initialPlan = await new StoryCompositionPlanner(snapshots).CreateAsync(
            StoryCompositionOperationType.AddDialogue, MapTarget(), CreateAnchor(), workspace.RootPath,
            "Ilan", "E allora da dove è iniziato tutto?");
        var remote = new FakeAppendClient(UpdatedMdv, afterAppend: () =>
            File.WriteAllText(mapPath, File.ReadAllText(mapPath).Replace("\"customMapProperty\":\"unchanged\"", "\"customMapProperty\":\"changed externally\"", StringComparison.Ordinal)));
        var recovery = new StoryCompositionRecoveryStore(fileSystem, Path.Combine(workspace.RootPath, "journal.json"));
        var service = CreateCompositionService(fileSystem, snapshots, remote, recovery);

        var result = await service.CommitAsync(CreateProject(workspace.RootPath), initialPlan);

        Assert.True(result.RequiresRecovery);
        Assert.Equal(1, remote.AppendCalls);
        var savedRecovery = Assert.Single(await service.FindRecoveriesAsync(CreateProject(workspace.RootPath)));
        Assert.Equal(StoryCompositionState.SourceConflict, savedRecovery.State);
        Assert.Contains("Ilan", await File.ReadAllTextAsync(Path.Combine(workspace.RootPath, "locales/it/dialogue/mdv.json")));

        var rebasedPlan = await new StoryCompositionPlanner(snapshots).CreateAsync(
            StoryCompositionOperationType.AddDialogue, MapTarget(), CreateAnchor(), workspace.RootPath,
            "Ilan", "E allora da dove è iniziato tutto?");
        var completed = await service.CompleteRecoveryAsync(CreateProject(workspace.RootPath), savedRecovery, rebasedPlan);

        Assert.True(completed.IsSuccess);
        Assert.Equal(1, remote.AppendCalls);
        var list = GetMapList(JsonNode.Parse(await File.ReadAllTextAsync(mapPath))!.AsObject());
        Assert.Equal([101, 401, 101, 401, 0], list.Select(command => command!["code"]!.GetValue<int>()).ToArray());
        Assert.Contains("newPrologoStory[1].name", list[2]! ["parameters"]![4]!.GetValue<string>());
        Assert.Empty(await service.FindRecoveriesAsync(CreateProject(workspace.RootPath)));
    }

    [Fact]
    public async Task Commit_SuccessSynchronizesMirrorInsertsMapAndClearsRecovery()
    {
        using var workspace = new TestWorkspace();
        WriteMap(workspace);
        workspace.WriteFile("locales/it/dialogue/mdv.json", InitialMdv);
        var fileSystem = new FileSystemService();
        var snapshots = new DocumentSnapshotService(fileSystem);
        var plan = await new StoryCompositionPlanner(snapshots).CreateAsync(
            StoryCompositionOperationType.AddNarration, MapTarget(), CreateAnchor(), workspace.RootPath,
            string.Empty, "Un tempo.");
        var service = CreateCompositionService(fileSystem, snapshots, new FakeAppendClient(UpdatedMdv),
            new StoryCompositionRecoveryStore(fileSystem, Path.Combine(workspace.RootPath, "journal.json")));

        var result = await service.CommitAsync(CreateProject(workspace.RootPath), plan);

        Assert.True(result.IsSuccess);
        var list = GetMapList(JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(workspace.RootPath, "data", "Map001.json")))!.AsObject());
        Assert.Equal([101, 401, 101, 401, 0], list.Select(command => command!["code"]!.GetValue<int>()).ToArray());
        Assert.Equal(string.Empty, list[2]!["parameters"]![4]!.GetValue<string>());
        Assert.Contains("Ilan", await File.ReadAllTextAsync(Path.Combine(workspace.RootPath, "locales/it/dialogue/mdv.json")));
        Assert.Empty(await service.FindRecoveriesAsync(CreateProject(workspace.RootPath)));
    }

    [Fact]
    public async Task Recovery_ResponseLostAfterRemoteAppend_ReusesOperationIdWithoutDuplicateEntry()
    {
        using var workspace = new TestWorkspace();
        WriteMap(workspace);
        workspace.WriteFile("locales/it/dialogue/mdv.json", InitialMdv);
        var fileSystem = new FileSystemService();
        var snapshots = new DocumentSnapshotService(fileSystem);
        var plan = await new StoryCompositionPlanner(snapshots).CreateAsync(
            StoryCompositionOperationType.AddDialogue, MapTarget(), CreateAnchor(), workspace.RootPath,
            "Ilan", "E allora da dove è iniziato tutto?");
        var remote = new FakeAppendClient(UpdatedMdv, loseFirstResponse: true);
        var service = CreateCompositionService(fileSystem, snapshots, remote,
            new StoryCompositionRecoveryStore(fileSystem, Path.Combine(workspace.RootPath, "journal.json")));

        var first = await service.CommitAsync(CreateProject(workspace.RootPath), plan);
        var pending = Assert.Single(await service.FindRecoveriesAsync(CreateProject(workspace.RootPath)));
        var second = await service.CompleteRecoveryAsync(CreateProject(workspace.RootPath), pending);

        Assert.True(first.RequiresRecovery);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, remote.AppendCalls);
        Assert.Single(remote.OperationIds.Distinct());
        Assert.Equal(plan.OperationId, remote.OperationIds[0]);
        Assert.Empty(await service.FindRecoveriesAsync(CreateProject(workspace.RootPath)));
    }

    private static StoryCompositionService CreateCompositionService(
        FileSystemService fileSystem,
        DocumentSnapshotService snapshots,
        IRemoteLocalizationAuthoringClient remote,
        StoryCompositionRecoveryStore recovery) => new(
        remote,
        snapshots,
        new PublishedLocalizationFileWriter(fileSystem, new AtomicJsonFileWriter(fileSystem)),
        new LocalizationService([new FusionLocalizationProvider(fileSystem)]),
        new StoryCompositionPlanner(snapshots),
        CreateWriter(fileSystem),
        recovery);

    private static StoryCommandListWriter CreateWriter(FileSystemService? fileSystem = null)
    {
        fileSystem ??= new FileSystemService();
        return new StoryCommandListWriter(fileSystem, new AtomicJsonFileWriter(fileSystem));
    }

    private static StoryCompositionPlan CreatePlan(DocumentSourceSnapshot snapshot, StoryCommandListTarget? target = null) => new()
    {
        OperationId = "b6f0300c-7791-4c34-b2d4-7df4c7afb7da",
        OperationType = StoryCompositionOperationType.AddDialogue,
        Target = target ?? MapTarget(),
        InsertionCommandIndex = 2,
        AnchorStartCommandIndex = 0,
        AnchorEndCommandIndex = 1,
        Indent = 0,
        AnchorCommands = CreateAnchor().SourceCommands.Select(command => new StoryCommandSignature
        {
            Code = command.Code, Indent = command.Indent, ParametersJson = command.Parameters,
        }).ToArray(),
        ExpectedSourceSnapshot = snapshot,
        AnchorOrigin = CreateAnchor().LocalizationOrigins.Last(),
        LocalizationBranchPath =
        [
            LocalizationPathSegment.Index(0), LocalizationPathSegment.Property("DestinyOfBirth"),
            LocalizationPathSegment.Index(0), LocalizationPathSegment.Property("newPrologoStory"),
        ],
        Values = new StoryCompositionValues {Speaker = "Ilan", Text = "E allora da dove è iniziato tutto?"},
        Presentation = new StoryDialoguePresentation {FaceName = "Actor1", FaceIndex = 0, Background = 0, PositionType = 2, IsInherited = true},
    };

    private static StoryBlock CreateAnchor() => new()
    {
        Kind = StoryBlockKind.Dialogue,
        Title = "Polka",
        DisplayText = "Non c'erano dèi.",
        CommandStartIndex = 0,
        CommandEndIndex = 1,
        Indent = 0,
        SourceCommands =
        [
            new StoryRawCommand {Code = 101, Indent = 0, Parameters = "[\"Actor1\",0,0,2,\"{mdv[0].DestinyOfBirth[0].newPrologoStory[0].name}\"]"},
            new StoryRawCommand {Code = 401, Indent = 0, Parameters = "[\"{mdv[0].DestinyOfBirth[0].newPrologoStory[0].text}\"]"},
        ],
        LocalizationOrigins =
        [
            new LocalizationReferenceOrigin
            {
                Namespace = "mdv", Locale = "it", SourceFile = "dialogue/mdv.json",
                Path = "mdv[0].DestinyOfBirth[0].newPrologoStory[0].name",
                Segments = [LocalizationPathSegment.Index(0), LocalizationPathSegment.Property("DestinyOfBirth"), LocalizationPathSegment.Index(0), LocalizationPathSegment.Property("newPrologoStory"), LocalizationPathSegment.Index(0), LocalizationPathSegment.Property("name")],
                ResolvedValue = "Polka",
            },
            new LocalizationReferenceOrigin
            {
                Namespace = "mdv", Locale = "it", SourceFile = "dialogue/mdv.json",
                Path = "mdv[0].DestinyOfBirth[0].newPrologoStory[0].text",
                Segments = [LocalizationPathSegment.Index(0), LocalizationPathSegment.Property("DestinyOfBirth"), LocalizationPathSegment.Index(0), LocalizationPathSegment.Property("newPrologoStory"), LocalizationPathSegment.Index(0), LocalizationPathSegment.Property("text")],
                ResolvedValue = "Non c'erano dèi.",
            },
        ],
    };

    private static StoryCommandListTarget MapTarget() => new()
    {
        Kind = StoryCommandListKind.MapPage, SourceFile = "data/Map001.json", MapId = 1, EventId = 1, PageNumber = 1,
    };

    private static StoryCommandListTarget CommonTarget() => new()
    {
        Kind = StoryCommandListKind.CommonEvent, SourceFile = "data/CommonEvents.json", EventId = 2,
    };

    private static JsonArray GetMapList(JsonObject root) => root["events"]!.AsArray()[1]!.AsObject()["pages"]!.AsArray()[0]!.AsObject()["list"]!.AsArray();
    private static string DisplaySegment(LocalizationPathSegment segment) => segment.PropertyName ?? $"[{segment.ArrayIndex}]";

    private static string WriteMap(TestWorkspace workspace) => workspace.WriteFile("data/Map001.json", """
        {"customMapProperty":"unchanged","events":[null,{"id":1,"name":"Prologo","x":1,"y":2,"pages":[{"list":[
          {"code":101,"indent":0,"parameters":["Actor1",0,0,2,"{mdv[0].DestinyOfBirth[0].newPrologoStory[0].name}"]},
          {"code":401,"indent":0,"parameters":["{mdv[0].DestinyOfBirth[0].newPrologoStory[0].text}"]},
          {"code":0,"indent":0,"parameters":[]}]}]},{"id":2,"name":"Non toccare","pages":[{"list":[{"code":0,"indent":0,"parameters":[]}]}]}]}
        """);

    private static ZiapProject CreateProject(string path) => new()
    {
        Id = "fusion-hexella-dive", Name = "Fusion: Hexella Dive", Path = path,
        ProjectType = KnownProjectTypes.RpgMakerMz, IsZiapInitialized = true,
    };

    private const string InitialMdv = "[{\"DestinyOfBirth\":[{\"newPrologoStory\":[{\"name\":\"Polka\",\"text\":\"Non c'erano dèi.\"}]}]}]";
    private const string UpdatedMdv = """
    [
      {"DestinyOfBirth":[{"newPrologoStory":[
        {"name":"Polka","text":"Non c'erano dèi."},
        {"name":"Ilan","text":"E allora da dove è iniziato tutto?"}
      ]}]}
    ]
    """;

    private sealed class FakeAppendClient(
        string appendedContent,
        Action? afterAppend = null,
        bool loseFirstResponse = false) : IRemoteLocalizationAuthoringClient
    {
        public int AppendCalls { get; private set; }
        public List<string> OperationIds { get; } = [];
        private bool _responseLost;
        private readonly LocalizationLockInfo _lock = new()
        {
            Owner = "Test", AcquiredAt = "2026-10-04T00:00:00.000Z", ExpiresAt = "2099-10-04T00:02:00.000Z", IsOwnedByCurrentUser = true,
        };

        public Task<LocalizationAuthoringSnapshot> GetAuthoringFileAsync(string projectId, string locale, string sourceFile, CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot(InitialMdv));
        public Task<LocalizationLockInfo> ClaimLockAsync(string projectId, string locale, string sourceFile, CancellationToken cancellationToken = default) => Task.FromResult(_lock);
        public Task<LocalizationLockInfo> RenewLockAsync(string projectId, string locale, string sourceFile, string? acquiredAt, CancellationToken cancellationToken = default) => Task.FromResult(_lock);
        public Task ReleaseLockAsync(string projectId, string locale, string sourceFile, string? acquiredAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LocalizationAuthoringSnapshot> PatchStagingAsync(string projectId, string locale, string sourceFile, string? basedOnVersionId, string? expectedStagingChecksum, IReadOnlyList<LocalizationScalarPatch> changes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LocalizationAppendResult> AppendStagingEntryAsync(LocalizationAppendRequest request, CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            OperationIds.Add(request.OperationId);
            afterAppend?.Invoke();
            if (loseFirstResponse && !_responseLost)
            {
                _responseLost = true;
                throw new RemoteLocalizationAuthoringException(
                    RemoteLocalizationAuthoringFailure.Network, "Risposta remota persa dopo l'append.");
            }
            return Task.FromResult(new LocalizationAppendResult
            {
                OperationId = request.OperationId, AssignedIndex = 1,
                EntryPath = [.. request.BranchPath, LocalizationPathSegment.Index(1)],
                Snapshot = Snapshot(appendedContent),
            });
        }

        private LocalizationAuthoringSnapshot Snapshot(string content) => new()
        {
            ProjectId = "fusion-hexella-dive", Locale = "it", SourceFile = "dialogue/mdv.json", Content = content,
            CurrentVersionId = "v1", CurrentChecksum = "current", StagingChecksum = null,
            Source = LocalizationAuthoringSource.Published, Lock = _lock,
        };
    }
}
