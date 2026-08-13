using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Providers;

internal sealed class GenericProjectProvider : IProjectProvider
{
    private readonly FileSystemTreeBuilder _fileSystemTreeBuilder;

    public GenericProjectProvider(FileSystemTreeBuilder fileSystemTreeBuilder)
    {
        _fileSystemTreeBuilder = fileSystemTreeBuilder;
    }

    public bool CanHandle(ZiapProject project) => true;

    public Task<IReadOnlyList<ProjectExplorerNode>> BuildExplorerAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectExplorerNode>>(
            [_fileSystemTreeBuilder.BuildFilesRoot(project.Path)]);
}
