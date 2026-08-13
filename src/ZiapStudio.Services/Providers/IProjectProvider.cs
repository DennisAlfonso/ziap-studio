using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Providers;

public interface IProjectProvider
{
    bool CanHandle(ZiapProject project);

    Task<IReadOnlyList<ProjectExplorerNode>> BuildExplorerAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default);
}
