using ZiapStudio.Core.Models;
using ZiapStudio.Core.Preflight;

namespace ZiapStudio.Services.Fusion.Preflight;

public sealed class PreflightScanner
{
    private readonly IReadOnlyList<IPreflightProvider> _providers;

    public PreflightScanner(IEnumerable<IPreflightProvider> providers)
    {
        _providers = providers.ToArray();
    }

    public async Task<PreflightScanResult> ScanAsync(
        ZiapProject project,
        IReadOnlyList<PreflightSuppression> suppressions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(suppressions);

        var issues = new List<PreflightIssue>();
        foreach (var provider in _providers)
        {
            issues.AddRange(await provider.ScanAsync(project, cancellationToken));
        }

        var orderedIssues = issues
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.RecordId)
            .ThenBy(issue => issue.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var active = new List<PreflightIssue>();
        var ignored = new List<SuppressedPreflightIssue>();
        foreach (var issue in orderedIssues)
        {
            var suppression = suppressions.FirstOrDefault(candidate =>
                candidate.Identity.Matches(issue.Identity));
            if (suppression is null)
            {
                active.Add(issue);
            }
            else
            {
                ignored.Add(new SuppressedPreflightIssue(issue, suppression));
            }
        }

        var obsolete = suppressions
            .Where(suppression => !orderedIssues.Any(issue =>
                suppression.Identity.Matches(issue.Identity)))
            .OrderByDescending(suppression => suppression.IgnoredAt)
            .ToArray();

        return new PreflightScanResult
        {
            ActiveIssues = active,
            IgnoredIssues = ignored,
            ObsoleteSuppressions = obsolete,
            ScannedAt = DateTimeOffset.Now,
        };
    }
}
