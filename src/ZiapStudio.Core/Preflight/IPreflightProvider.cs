using ZiapStudio.Core.Models;

namespace ZiapStudio.Core.Preflight;

public interface IPreflightProvider
{
    string Scope { get; }

    Task<IReadOnlyList<PreflightIssue>> ScanAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default);
}
