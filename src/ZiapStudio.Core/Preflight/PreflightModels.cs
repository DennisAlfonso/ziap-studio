namespace ZiapStudio.Core.Preflight;

public enum PreflightSeverity
{
    Warning,
    Error,
}

public sealed record PreflightIssue
{
    public required string RuleId { get; init; }
    public required string Scope { get; init; }
    public required int RecordId { get; init; }
    public required string RecordName { get; init; }
    public required PreflightSeverity Severity { get; init; }
    public required string Message { get; init; }
    public string? Details { get; init; }
    public Uri? NavigationTarget { get; init; }
    public PreflightIssueIdentity Identity => new(RuleId, Scope, RecordId);
}

public readonly record struct PreflightIssueIdentity(string RuleId, string Scope, int RecordId)
{
    public bool Matches(PreflightIssueIdentity other) =>
        RecordId == other.RecordId &&
        RuleId.Equals(other.RuleId, StringComparison.OrdinalIgnoreCase) &&
        Scope.Equals(other.Scope, StringComparison.OrdinalIgnoreCase);
}

public sealed record PreflightSuppression
{
    public required string RuleId { get; init; }
    public required string Scope { get; init; }
    public required int RecordId { get; init; }
    public required DateTimeOffset IgnoredAt { get; init; }
    public string? Reason { get; init; }
    public PreflightIssueIdentity Identity => new(RuleId, Scope, RecordId);
}

public sealed record SuppressedPreflightIssue(PreflightIssue Issue, PreflightSuppression Suppression);

public sealed record PreflightScanResult
{
    public static PreflightScanResult Empty { get; } = new();
    public IReadOnlyList<PreflightIssue> ActiveIssues { get; init; } = [];
    public IReadOnlyList<SuppressedPreflightIssue> IgnoredIssues { get; init; } = [];
    public IReadOnlyList<PreflightSuppression> ObsoleteSuppressions { get; init; } = [];
    public DateTimeOffset? ScannedAt { get; init; }
    public int ErrorCount => ActiveIssues.Count(issue => issue.Severity == PreflightSeverity.Error);
    public int WarningCount => ActiveIssues.Count(issue => issue.Severity == PreflightSeverity.Warning);
    public int IgnoredCount => IgnoredIssues.Count;
}

public interface IPreflightRule<in TContext>
{
    string RuleId { get; }
    IEnumerable<PreflightIssue> Evaluate(TContext context);
}
