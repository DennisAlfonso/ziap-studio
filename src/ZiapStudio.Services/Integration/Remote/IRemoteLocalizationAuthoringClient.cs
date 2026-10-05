using ZiapStudio.Core.Localization;

namespace ZiapStudio.Services.Integration.Remote;

public interface IRemoteLocalizationAuthoringClient
{
    Task<LocalizationAuthoringSnapshot> GetAuthoringFileAsync(
        string projectId,
        string locale,
        string sourceFile,
        CancellationToken cancellationToken = default);

    Task<LocalizationLockInfo> ClaimLockAsync(
        string projectId,
        string locale,
        string sourceFile,
        CancellationToken cancellationToken = default);

    Task<LocalizationLockInfo> RenewLockAsync(
        string projectId,
        string locale,
        string sourceFile,
        string? acquiredAt,
        CancellationToken cancellationToken = default);

    Task ReleaseLockAsync(
        string projectId,
        string locale,
        string sourceFile,
        string? acquiredAt,
        CancellationToken cancellationToken = default);

    Task<LocalizationAuthoringSnapshot> PatchStagingAsync(
        string projectId,
        string locale,
        string sourceFile,
        string? basedOnVersionId,
        string? expectedStagingChecksum,
        IReadOnlyList<LocalizationScalarPatch> changes,
        CancellationToken cancellationToken = default);

    Task<LocalizationAppendResult> AppendStagingEntryAsync(
        LocalizationAppendRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record LocalizationAppendRequest
{
    public required string ProjectId { get; init; }
    public required string Locale { get; init; }
    public required string SourceFile { get; init; }
    public required string OperationId { get; init; }
    public required string BasedOnVersionId { get; init; }
    public string? ExpectedStagingChecksum { get; init; }
    public required int ExpectedArrayLength { get; init; }
    public required IReadOnlyList<LocalizationPathSegment> BranchPath { get; init; }
    public required string Template { get; init; }
    public required string Text { get; init; }
    public string Speaker { get; init; } = string.Empty;
}

public sealed record LocalizationAppendResult
{
    public required string OperationId { get; init; }
    public required int AssignedIndex { get; init; }
    public required IReadOnlyList<LocalizationPathSegment> EntryPath { get; init; }
    public required LocalizationAuthoringSnapshot Snapshot { get; init; }
    public bool IsIdempotentReplay { get; init; }
}

public sealed class RemoteLocalizationAuthoringException : Exception
{
    public RemoteLocalizationAuthoringException(
        RemoteLocalizationAuthoringFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public RemoteLocalizationAuthoringFailure Failure { get; }
}

public enum RemoteLocalizationAuthoringFailure
{
    Unauthorized,
    LockedByOther,
    Conflict,
    Network,
    InvalidRequest,
    Unknown,
}
