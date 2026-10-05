using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Core.Localization;
using ZiapStudio.Core.Models;
using ZiapStudio.Services;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Fusion.Story;
using ZiapStudio.Services.Integration.Remote;
using ZiapStudio.Services.Localization;
using ZiapStudio.ViewModels;

namespace ZiapStudio.ViewModels.Tests;

public sealed class FusionStoryDocumentViewModelTests
{
    [Fact]
    public async Task Typing_PreservesFieldCollectionBlockIdentityAndEditorCommands()
    {
        using var fixture = new StoryEditorFixture();
        using var viewModel = fixture.CreateViewModel();
        await viewModel.BeginEditingAsync();

        var fields = viewModel.LocalizationFields;
        var field = Assert.Single(fields);
        var blocks = viewModel.Blocks;
        var selectedBlock = Assert.IsType<StoryBlock>(viewModel.SelectedBlock);

        field.Value = "Edited";

        Assert.Same(fields, viewModel.LocalizationFields);
        Assert.Same(field, Assert.Single(viewModel.LocalizationFields));
        Assert.Same(blocks, viewModel.Blocks);
        Assert.Same(selectedBlock, viewModel.SelectedBlock);
        Assert.True(viewModel.CanSaveToStaging);
        Assert.True(viewModel.CanUndoLocalization);

        viewModel.UndoLocalization();
        Assert.Equal("First", Assert.Single(viewModel.LocalizationFields).Value);
        Assert.True(viewModel.CanRedoLocalization);

        viewModel.RedoLocalization();
        Assert.Equal("Edited", Assert.Single(viewModel.LocalizationFields).Value);

        await viewModel.SaveToStagingAsync();
        Assert.Equal("Canonical saved", Assert.Single(viewModel.LocalizationFields).Value);
        Assert.False(viewModel.CanSaveToStaging);

        viewModel.LocalizationFields[0].Value = "Temporary";
        viewModel.DiscardLocalization();
        Assert.Equal("Canonical saved", Assert.Single(viewModel.LocalizationFields).Value);
        Assert.False(viewModel.CanSaveToStaging);
    }

    [Fact]
    public async Task SelectionChange_RebuildsFieldsOnlyForTheNewEditorContext()
    {
        using var fixture = new StoryEditorFixture();
        using var viewModel = fixture.CreateViewModel();
        await viewModel.BeginEditingAsync();

        var fields = viewModel.LocalizationFields;
        var firstField = Assert.Single(fields);

        viewModel.SelectedBlock = viewModel.Blocks[1];

        Assert.Same(fields, viewModel.LocalizationFields);
        var secondField = Assert.Single(viewModel.LocalizationFields);
        Assert.NotSame(firstField, secondField);
        Assert.Equal("Second", secondField.Value);
    }

    private sealed class StoryEditorFixture : IDisposable
    {
        private const string Content = """[{"dialogue":[{"text":"First"},{"text":"Second"}]}]""";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ziap-story-vm-" + Guid.NewGuid().ToString("N"));
        private readonly LocalizationReferenceOrigin _firstOrigin = CreateOrigin(0, "First");
        private readonly LocalizationReferenceOrigin _secondOrigin = CreateOrigin(1, "Second");

        public StoryEditorFixture()
        {
            Directory.CreateDirectory(Path.Combine(_root, "locales", "it", "dialogue"));
            File.WriteAllText(Path.Combine(_root, "locales", "it", "dialogue", "mdv.json"), Content);
        }

        public FusionStoryDocumentViewModel CreateViewModel()
        {
            var fileSystem = new FileSystemService();
            var snapshots = new DocumentSnapshotService(fileSystem);
            var remote = new OwnedLockAuthoringClient(CreateSnapshot());
            var localization = new LocalizationService([new FusionLocalizationProvider(fileSystem)]);
            var atomicWriter = new AtomicJsonFileWriter(fileSystem);
            var mirrorWriter = new PublishedLocalizationFileWriter(fileSystem, atomicWriter);
            var authoring = new StoryLocalizationAuthoringService(
                remote,
                snapshots,
                new ExternalModificationDetector(fileSystem, snapshots),
                mirrorWriter,
                localization);
            var composition = new StoryCompositionService(
                remote,
                snapshots,
                mirrorWriter,
                localization,
                new StoryCompositionPlanner(snapshots),
                new StoryCommandListWriter(fileSystem, atomicWriter),
                new StoryCompositionRecoveryStore(fileSystem, Path.Combine(_root, "recovery.json")));
            return new FusionStoryDocumentViewModel(
                new FusionStoryWorkspaceDocument
                {
                    ProjectPath = _root,
                    Workspace = new StoryWorkspace
                    {
                        Maps =
                        [
                            new StoryMap
                            {
                                Id = 1,
                                Name = "Test map",
                                SourcePath = "data/Map001.json",
                                Events =
                                [
                                    new StoryEvent
                                    {
                                        Id = 1,
                                        Name = "Test event",
                                        Pages =
                                        [
                                            new StoryPage
                                            {
                                                Number = 1,
                                                Trigger = 0,
                                                Blocks = [CreateBlock(_firstOrigin, "First"), CreateBlock(_secondOrigin, "Second")],
                                            },
                                        ],
                                    },
                                ],
                            },
                        ],
                    },
                },
                new ZiapProject
                {
                    Id = "fusion-hexella-dive",
                    Name = "Fusion: Hexella Dive",
                    ProjectType = KnownProjectTypes.RpgMakerMz,
                    Path = _root,
                    IsZiapInitialized = true,
                },
                authoring,
                composition,
                () => Task.FromResult(new FusionStoryWorkspaceDocument()));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static StoryBlock CreateBlock(LocalizationReferenceOrigin origin, string text) => new()
        {
            Kind = StoryBlockKind.Dialogue,
            Title = "Polka",
            DisplayText = text,
            LocalizationOrigins = [origin],
        };

        private static LocalizationReferenceOrigin CreateOrigin(int index, string value) => new()
        {
            Namespace = "mdv",
            Path = $"0.dialogue.{index}.text",
            Locale = "it",
            SourceFile = "dialogue/mdv.json",
            ResolvedValue = value,
            Segments =
            [
                LocalizationPathSegment.Index(0),
                LocalizationPathSegment.Property("dialogue"),
                LocalizationPathSegment.Index(index),
                LocalizationPathSegment.Property("text"),
            ],
        };

        private static LocalizationAuthoringSnapshot CreateSnapshot() => new()
        {
            ProjectId = "fusion-hexella-dive",
            Locale = "it",
            SourceFile = "dialogue/mdv.json",
            Content = Content,
            CurrentVersionId = "v1",
            CurrentChecksum = "checksum",
            Source = LocalizationAuthoringSource.Published,
            Lock = new LocalizationLockInfo { IsOwnedByCurrentUser = true, AcquiredAt = "2026-10-05T00:00:00.000Z" },
        };
    }

    private sealed class OwnedLockAuthoringClient(LocalizationAuthoringSnapshot snapshot) : IRemoteLocalizationAuthoringClient
    {
        public Task<LocalizationAuthoringSnapshot> GetAuthoringFileAsync(
            string projectId, string locale, string sourceFile, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task<LocalizationLockInfo> ClaimLockAsync(
            string projectId, string locale, string sourceFile, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot.Lock);

        public Task<LocalizationLockInfo> RenewLockAsync(
            string projectId, string locale, string sourceFile, string? acquiredAt,
            CancellationToken cancellationToken = default) => Task.FromResult(snapshot.Lock);

        public Task ReleaseLockAsync(
            string projectId, string locale, string sourceFile, string? acquiredAt,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LocalizationAuthoringSnapshot> PatchStagingAsync(
            string projectId, string locale, string sourceFile, string? basedOnVersionId,
            string? expectedStagingChecksum, IReadOnlyList<LocalizationScalarPatch> changes,
            CancellationToken cancellationToken = default) => Task.FromResult(snapshot with
            {
                Content = """[{"dialogue":[{"text":"Canonical saved"},{"text":"Second"}]}]""",
                Source = LocalizationAuthoringSource.Staging,
                StagingChecksum = "staging",
            });

        public Task<LocalizationAppendResult> AppendStagingEntryAsync(
            LocalizationAppendRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
