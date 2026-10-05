using ZiapStudio.Core.Editing;

namespace ZiapStudio.Services.ProjectSafety;

public enum ProjectWriteOperation
{
    ReplaceExisting,
    CreateNew,
}

public enum ProjectWriteFailure
{
    RpgMakerActive,
    ExternalModification,
    RevalidationRequired,
    ConcurrentStudioWriter,
    InvalidProjectPath,
    AtomicWriteFailed,
    ValidationFailed,
}

public sealed class ProjectWriteException : IOException
{
    public ProjectWriteException(ProjectWriteFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public ProjectWriteFailure Failure { get; }
}

public sealed record ProjectWriteRequest
{
    public required string ProjectRoot { get; init; }

    public required string TargetPath { get; init; }

    public required ProjectWriteOperation Operation { get; init; }

    public required ReadOnlyMemory<byte> Contents { get; init; }

    public string? ExpectedContentHash { get; init; }

    public bool ValidateJson { get; init; } = true;

    public Func<ReadOnlyMemory<byte>, CancellationToken, Task>? Validator { get; init; }
}

public sealed record ProjectWriteResult(
    string TargetPath,
    ProjectFileOwnership Ownership,
    DocumentSourceSnapshot Snapshot);

public enum ProjectSafetyState
{
    Safe,
    ProtectedCoexistence,
    RevalidationRequired,
}
