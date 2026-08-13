using ZiapStudio.Core.Models;

namespace ZiapStudio.Services;

public interface IRecentProjectService
{
    Task<IReadOnlyList<ZiapProject>> GetRecentProjectsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ZiapProject>> AddAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default);
}
