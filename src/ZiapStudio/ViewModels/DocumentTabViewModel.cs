using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Assets;
using ZiapStudio.Core.Editing;

namespace ZiapStudio.ViewModels;

public sealed class DocumentTabViewModel : INotifyPropertyChanged
{
    private DocumentTabViewModel(
        DocumentDescriptor descriptor,
        RpgMakerDatabaseDocumentViewModel? database,
        RemoteLocalizationDocumentViewModel? remoteLocalization = null,
        PreflightDocumentViewModel? preflight = null,
        FusionAudioDocumentViewModel? fusionAudio = null,
        FusionBossDocumentViewModel? fusionBoss = null,
        DocumentEditSession? editSession = null,
        FusionBossEditSession? fusionBossEditSession = null)
    {
        Descriptor = descriptor;
        Database = database;
        RemoteLocalization = remoteLocalization;
        Preflight = preflight;
        FusionAudio = fusionAudio;
        FusionBoss = fusionBoss;
        EditSession = editSession;
        FusionBossEditSession = fusionBossEditSession;
        if (EditSession is not null)
        {
            EditSession.PropertyChanged += EditSession_PropertyChanged;
        }
        if (FusionAudio is not null)
        {
            FusionAudio.PropertyChanged += FusionAudio_PropertyChanged;
        }
        if (FusionBossEditSession is not null)
        {
            FusionBossEditSession.PropertyChanged += FusionBossEditSession_PropertyChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DocumentDescriptor Descriptor { get; }

    public string DisplayName => Descriptor.DisplayName;

    public string Header => IsDirty ? $"{DisplayName} *" : DisplayName;

    public bool IsClosable => Descriptor.Kind != DocumentKind.ProjectOverview;

    public bool IsProjectOverview => Descriptor.Kind == DocumentKind.ProjectOverview;

    public bool IsRemoteLocalization => RemoteLocalization is not null;

    public bool IsPreflight => Preflight is not null;

    public bool IsFusionAudio => FusionAudio is not null;

    public bool IsFusionBoss => FusionBoss is not null;

    public RpgMakerDatabaseDocumentViewModel? Database { get; }

    public RemoteLocalizationDocumentViewModel? RemoteLocalization { get; }

    public PreflightDocumentViewModel? Preflight { get; }

    public FusionAudioDocumentViewModel? FusionAudio { get; }

    public FusionBossDocumentViewModel? FusionBoss { get; private set; }

    public DocumentEditSession? EditSession { get; }

    public FusionBossEditSession? FusionBossEditSession { get; private set; }

    public bool IsDirty => EditSession?.IsDirty == true ||
        FusionAudio?.IsDirty == true ||
        FusionBossEditSession?.IsDirty == true;

    public bool CanUndo => EditSession?.CanUndo == true ||
        FusionBossEditSession?.CanUndo == true;

    public bool CanRedo => EditSession?.CanRedo == true ||
        FusionBossEditSession?.CanRedo == true;

    public static DocumentTabViewModel CreateProjectOverview(string projectId) => new(
        new DocumentDescriptor
        {
            Id = new DocumentId($"{projectId}:overview"),
            DisplayName = "Overview",
            Kind = DocumentKind.ProjectOverview,
            ResourceId = new Uri("ziap://project/overview"),
        },
        database: null);

    public static DocumentTabViewModel CreateRemoteLocalization(
        string projectId,
        RemoteLocalizationDocumentViewModel remoteLocalization) => new(
        new DocumentDescriptor
        {
            Id = new DocumentId($"{projectId}:remote-localization"),
            DisplayName = "Remote Localization",
            Kind = DocumentKind.Tool,
            ResourceId = new Uri("ziap://project/remote-localization"),
        },
        database: null,
        remoteLocalization: remoteLocalization);

    public static DocumentTabViewModel CreatePreflight(
        string projectId,
        PreflightDocumentViewModel preflight) => new(
        new DocumentDescriptor
        {
            Id = new DocumentId($"{projectId}:preflight"),
            DisplayName = "Pre-Flight",
            Kind = DocumentKind.Tool,
            ResourceId = new Uri("ziap://project/preflight"),
        },
        database: null,
        preflight: preflight);

    public static DocumentTabViewModel CreateFusionAudio(
        FusionAudioDocument document,
        FusionAudioDocumentViewModel fusionAudio) => new(
        document.Descriptor,
        database: null,
        fusionAudio: fusionAudio);

    public static DocumentTabViewModel CreateFusionBoss(
        FusionBossWorkspaceDocument document,
        FusionBossDocumentViewModel fusionBoss,
        FusionBossEditSession? editSession) => new(
        document.Descriptor,
        database: null,
        fusionBoss: fusionBoss,
        fusionBossEditSession: editSession);

    public void ReplaceFusionBoss(
        FusionBossDocumentViewModel fusionBoss,
        FusionBossEditSession editSession)
    {
        ArgumentNullException.ThrowIfNull(fusionBoss);
        ArgumentNullException.ThrowIfNull(editSession);
        FusionBoss?.CloseExternalSurfaces();
        if (FusionBossEditSession is not null)
        {
            FusionBossEditSession.PropertyChanged -= FusionBossEditSession_PropertyChanged;
        }
        FusionBoss = fusionBoss;
        FusionBossEditSession = editSession;
        FusionBossEditSession.PropertyChanged += FusionBossEditSession_PropertyChanged;
        OnPropertyChanged(nameof(FusionBoss));
        OnPropertyChanged(nameof(FusionBossEditSession));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public static DocumentTabViewModel Create(
        RpgMakerDatabaseDocument document,
        IReadOnlyDictionary<AssetPreviewKey, AssetPreviewResult> assetPreviews,
        DocumentEditSession editSession) =>
        new(
            document.Descriptor,
            new RpgMakerDatabaseDocumentViewModel(document, assetPreviews, editSession),
            editSession: editSession);

    public void Dispose()
    {
        if (EditSession is not null)
        {
            EditSession.PropertyChanged -= EditSession_PropertyChanged;
        }
        if (FusionAudio is not null)
        {
            FusionAudio.PropertyChanged -= FusionAudio_PropertyChanged;
        }
        if (FusionBossEditSession is not null)
        {
            FusionBossEditSession.PropertyChanged -= FusionBossEditSession_PropertyChanged;
        }
        FusionBoss?.CloseExternalSurfaces();
    }

    private void EditSession_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DocumentEditSession.IsDirty))
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Header));
        }

        if (args.PropertyName == nameof(DocumentEditSession.CanUndo))
        {
            OnPropertyChanged(nameof(CanUndo));
        }

        if (args.PropertyName == nameof(DocumentEditSession.CanRedo))
        {
            OnPropertyChanged(nameof(CanRedo));
        }
    }

    private void FusionAudio_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FusionAudioDocumentViewModel.IsDirty))
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Header));
        }
    }

    private void FusionBossEditSession_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FusionBossEditSession.IsDirty))
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Header));
        }
        if (args.PropertyName == nameof(FusionBossEditSession.CanUndo))
        {
            OnPropertyChanged(nameof(CanUndo));
        }
        if (args.PropertyName == nameof(FusionBossEditSession.CanRedo))
        {
            OnPropertyChanged(nameof(CanRedo));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
