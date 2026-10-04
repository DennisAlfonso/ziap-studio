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
