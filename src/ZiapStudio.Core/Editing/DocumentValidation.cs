namespace ZiapStudio.Core.Editing;

public sealed record DocumentValidationIssue
{
    public DocumentValidationSeverity Severity { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? Target { get; init; }

    public string? PropertyPath { get; init; }
}

public enum DocumentValidationSeverity
{
    Warning,
    Error,
}

public sealed record DocumentValidationResult
{
    public IReadOnlyList<DocumentValidationIssue> Issues { get; init; } = [];

    public bool HasErrors => Issues.Any(issue =>
        issue.Severity == DocumentValidationSeverity.Error);

    public bool CanSave => !HasErrors;
}
