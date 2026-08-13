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
        DocumentEditSession? editSession = null)
    {
        Descriptor = descriptor;
        Database = database;
        EditSession = editSession;
        if (EditSession is not null)
        {
            EditSession.PropertyChanged += EditSession_PropertyChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DocumentDescriptor Descriptor { get; }

    public string DisplayName => Descriptor.DisplayName;

    public string Header => IsDirty ? $"{DisplayName} *" : DisplayName;

    public bool IsClosable => Descriptor.Kind != DocumentKind.ProjectOverview;

    public bool IsProjectOverview => Descriptor.Kind == DocumentKind.ProjectOverview;

    public RpgMakerDatabaseDocumentViewModel? Database { get; }

    public DocumentEditSession? EditSession { get; }

    public bool IsDirty => EditSession?.IsDirty == true;

    public bool CanUndo => EditSession?.CanUndo == true;

    public bool CanRedo => EditSession?.CanRedo == true;

    public static DocumentTabViewModel CreateProjectOverview(string projectId) => new(
        new DocumentDescriptor
        {
            Id = new DocumentId($"{projectId}:overview"),
            DisplayName = "Overview",
            Kind = DocumentKind.ProjectOverview,
            ResourceId = new Uri("ziap://project/overview"),
        },
        database: null);

    public static DocumentTabViewModel Create(
        RpgMakerDatabaseDocument document,
        IReadOnlyDictionary<AssetPreviewKey, AssetPreviewResult> assetPreviews,
        DocumentEditSession editSession) =>
        new(
            document.Descriptor,
            new RpgMakerDatabaseDocumentViewModel(document, assetPreviews, editSession),
            editSession);

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
