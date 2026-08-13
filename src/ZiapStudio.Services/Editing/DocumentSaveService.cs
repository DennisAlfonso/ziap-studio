using System.Text.Json;
using ZiapStudio.Core.Editing;

namespace ZiapStudio.Services.Editing;

public sealed class DocumentSaveService
{
    private readonly DocumentValidationService _validationService;
    private readonly ExternalModificationDetector _externalModificationDetector;
    private readonly AtomicJsonFileWriter _writer;
    private readonly DocumentSnapshotService _snapshotService;
    private readonly JsonTextPatchSerializer _serializer;

    public DocumentSaveService(
        DocumentValidationService validationService,
        ExternalModificationDetector externalModificationDetector,
        AtomicJsonFileWriter writer,
        DocumentSnapshotService snapshotService,
        JsonTextPatchSerializer serializer)
    {
        _validationService = validationService;
        _externalModificationDetector = externalModificationDetector;
        _writer = writer;
        _snapshotService = snapshotService;
        _serializer = serializer;
    }

    public async Task<DocumentSaveResult> SaveAsync(
        DocumentEditSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsDirty)
        {
            return new DocumentSaveResult { Status = DocumentSaveStatus.NoChanges };
        }

        var validation = _validationService.Validate(session);
        if (!validation.CanSave)
        {
            return new DocumentSaveResult
            {
                Status = DocumentSaveStatus.ValidationFailed,
                Validation = validation,
                Message = "Il documento contiene errori che impediscono il salvataggio.",
            };
        }

        try
        {
            if (await _externalModificationDetector.HasChangedAsync(
                session.SourceSnapshot,
                cancellationToken))
            {
                return new DocumentSaveResult
                {
                    Status = DocumentSaveStatus.ExternalModification,
                    Validation = validation,
                    Message = $"{Path.GetFileName(session.SourceSnapshot.SourcePath)} è stato modificato esternamente.",
                };
            }

            var bytes = await _serializer.SerializeAsync(session, cancellationToken);
            await _writer.WriteAsync(
                session.SourceSnapshot.SourcePath,
                bytes,
                session.SourceSnapshot.ContentHash,
                cancellationToken);
            var savedSnapshot = await _snapshotService.CaptureAsync(
                session.SourceSnapshot.SourcePath,
                cancellationToken);
            session.AcceptSavedSnapshot(savedSnapshot);
            return new DocumentSaveResult
            {
                Status = DocumentSaveStatus.Saved,
                Validation = validation,
            };
        }
        catch (ExternalDocumentModificationException exception)
        {
            return new DocumentSaveResult
            {
                Status = DocumentSaveStatus.ExternalModification,
                Validation = validation,
                Message = exception.Message,
                Exception = exception,
            };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new DocumentSaveResult
            {
                Status = DocumentSaveStatus.Failed,
                Validation = validation,
                Message = "Il salvataggio atomico non è riuscito; il documento resta modificato.",
                Exception = exception,
            };
        }
    }
}

public sealed record DocumentSaveResult
{
    public DocumentSaveStatus Status { get; init; }

    public DocumentValidationResult Validation { get; init; } = new();

    public string? Message { get; init; }

    public Exception? Exception { get; init; }

    public bool IsSuccess => Status is DocumentSaveStatus.Saved or DocumentSaveStatus.NoChanges;
}

public enum DocumentSaveStatus
{
    NoChanges,
    Saved,
    ValidationFailed,
    ExternalModification,
    Failed,
}
