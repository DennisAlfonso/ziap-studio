using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Providers;

internal sealed class FileSystemTreeBuilder
{
    private const int MaximumDepth = 2;
    private const int MaximumItemsPerDirectory = 80;

    private static readonly HashSet<string> HiddenDirectoryNames = new(
        [".git", ".vs", "bin", "obj", "node_modules"],
        StringComparer.OrdinalIgnoreCase);

    private readonly FileSystemService _fileSystem;

    public FileSystemTreeBuilder(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public ProjectExplorerNode BuildFilesRoot(string projectPath)
    {
        return new ProjectExplorerNode
        {
            Name = "Files",
            Kind = ProjectExplorerNodeKind.Category,
            Path = projectPath,
            Children = BuildDirectoryChildren(projectPath, depth: 0),
        };
    }

    private IReadOnlyList<ProjectExplorerNode> BuildDirectoryChildren(string path, int depth)
    {
        if (depth > MaximumDepth)
        {
            return [];
        }

        try
        {
            var directories = _fileSystem.EnumerateDirectories(path)
                .Where(directory => !HiddenDirectoryNames.Contains(Path.GetFileName(directory)))
                .Take(MaximumItemsPerDirectory + 1)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var remainingCapacity = Math.Max(
                0,
                MaximumItemsPerDirectory + 1 - directories.Length);
            var files = remainingCapacity == 0
                ? []
                : _fileSystem.EnumerateFiles(path, "*")
                    .Take(remainingCapacity)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            var visibleEntries = directories.Cast<string>().Concat(files).ToArray();
            var nodes = new List<ProjectExplorerNode>(
                Math.Min(visibleEntries.Length, MaximumItemsPerDirectory) + 1);

            foreach (var entry in visibleEntries.Take(MaximumItemsPerDirectory))
            {
                var isDirectory = _fileSystem.DirectoryExists(entry);
                nodes.Add(new ProjectExplorerNode
                {
                    Name = Path.GetFileName(entry),
                    Kind = isDirectory
                        ? ProjectExplorerNodeKind.Directory
                        : ProjectExplorerNodeKind.File,
                    Path = entry,
                    Children = isDirectory && depth < MaximumDepth
                        ? BuildDirectoryChildren(entry, depth + 1)
                        : [],
                });
            }

            if (visibleEntries.Length > MaximumItemsPerDirectory)
            {
                nodes.Add(new ProjectExplorerNode
                {
                    Name = "… altri elementi",
                    Kind = ProjectExplorerNodeKind.Information,
                });
            }

            return nodes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return
            [
                new ProjectExplorerNode
                {
                    Name = "Accesso non disponibile",
                    Kind = ProjectExplorerNodeKind.Information,
                    Path = path,
                },
            ];
        }
    }
}
