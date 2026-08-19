using System.Text.Json;
using System.Text.RegularExpressions;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Providers;

internal sealed class RpgMakerMzProjectProvider : IProjectProvider
{
    private const int MaximumPlugins = 100;
    private const int MaximumCustomDatabases = 40;
    private const int MaximumAudioAssets = 2500;

    private static readonly (string FileName, string DisplayName, string? ResourceName)[] DatabaseFiles =
    [
        ("Actors.json", "Attori", "actors"),
        ("Classes.json", "Classi", "classes"),
        ("Skills.json", "Abilità", "skills"),
        ("Items.json", "Oggetti", "items"),
        ("Weapons.json", "Armi", "weapons"),
        ("Armors.json", "Armature", "armors"),
        ("Enemies.json", "Nemici", "enemies"),
        ("Troops.json", "Truppe", "troops"),
        ("States.json", "Stati", "states"),
        ("Animations.json", "Animazioni", "animations"),
        ("Tilesets.json", "Tileset", "tilesets"),
        ("System.json", "Sistema", null),
    ];

    private static readonly HashSet<string> ReservedDataFiles = new(
        DatabaseFiles.Select(item => item.FileName)
            .Concat(["CommonEvents.json", "MapInfos.json"]),
        StringComparer.OrdinalIgnoreCase);

    private static readonly Regex MapFilePattern = new(
        "^Map\\d{3}\\.json$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly FileSystemService _fileSystem;
    private readonly FileSystemTreeBuilder _fileSystemTreeBuilder;

    public RpgMakerMzProjectProvider(
        FileSystemService fileSystem,
        FileSystemTreeBuilder fileSystemTreeBuilder)
    {
        _fileSystem = fileSystem;
        _fileSystemTreeBuilder = fileSystemTreeBuilder;
    }

    public bool CanHandle(ZiapProject project) =>
        project.ProjectType == KnownProjectTypes.RpgMakerMz;

    public async Task<IReadOnlyList<ProjectExplorerNode>> BuildExplorerAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        var nodes = new List<ProjectExplorerNode>
        {
            BuildDatabaseNode(project),
            await BuildWorldNodeAsync(project, cancellationToken),
            BuildSystemNode(project.Path),
            BuildAssetsNode(project.Path),
            _fileSystemTreeBuilder.BuildFilesRoot(project.Path),
        };

        return nodes;
    }

    private ProjectExplorerNode BuildDatabaseNode(ZiapProject project)
    {
        var dataPath = Path.Combine(project.Path, "data");
        var children = DatabaseFiles
            .Select(item => CreateFileNode(
                dataPath,
                item.FileName,
                item.DisplayName,
                ProjectExplorerNodeKind.Database,
                item.ResourceName is null
                    ? null
                    : CreateDatabaseDescriptor(project, item.DisplayName, item.ResourceName)))
            .Where(node => node is not null)
            .Cast<ProjectExplorerNode>()
            .ToList();

        if (_fileSystem.DirectoryExists(dataPath))
        {
            var customDatabases = _fileSystem.EnumerateFiles(dataPath, "*.json")
                .Where(path =>
                    !ReservedDataFiles.Contains(Path.GetFileName(path)) &&
                    !MapFilePattern.IsMatch(Path.GetFileName(path)))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Take(MaximumCustomDatabases)
                .Select(path => new ProjectExplorerNode
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Kind = ProjectExplorerNodeKind.Database,
                    Path = path,
                })
                .ToArray();

            if (customDatabases.Length > 0)
            {
                children.Add(new ProjectExplorerNode
                {
                    Name = "Dati personalizzati",
                    Kind = ProjectExplorerNodeKind.Category,
                    Path = dataPath,
                    Children = customDatabases,
                });
            }
        }

        return new ProjectExplorerNode
        {
            Name = "Database",
            Kind = ProjectExplorerNodeKind.Category,
            Path = dataPath,
            Children = children,
        };
    }

    private async Task<ProjectExplorerNode> BuildWorldNodeAsync(
        ZiapProject project,
        CancellationToken cancellationToken)
    {
        var dataPath = Path.Combine(project.Path, "data");
        var children = new List<ProjectExplorerNode>
        {
            await BuildMapsNodeAsync(dataPath, cancellationToken),
        };
        var commonEvents = CreateFileNode(
            dataPath,
            "CommonEvents.json",
            "Eventi comuni",
            ProjectExplorerNodeKind.Database,
            CreateDatabaseDescriptor(project, "Eventi comuni", "common-events"));
        if (commonEvents is not null)
        {
            children.Add(commonEvents);
        }

        return new ProjectExplorerNode
        {
            Name = "World",
            Kind = ProjectExplorerNodeKind.Category,
            Path = dataPath,
            Children = children,
        };
    }

    private async Task<ProjectExplorerNode> BuildMapsNodeAsync(
        string dataPath,
        CancellationToken cancellationToken)
    {
        var mapInfosPath = Path.Combine(dataPath, "MapInfos.json");
        if (!_fileSystem.FileExists(mapInfosPath))
        {
            return new ProjectExplorerNode
            {
                Name = "Mappe",
                Kind = ProjectExplorerNodeKind.Category,
                Path = dataPath,
            };
        }

        try
        {
            var json = await _fileSystem.ReadAllTextAsync(mapInfosPath, cancellationToken);
            var mapInfos = JsonSerializer.Deserialize<List<RpgMakerMapInfo?>>(json, JsonOptions) ?? [];
            var maps = mapInfos
                .Where(map => map is { Id: > 0 })
                .OrderBy(map => map!.Order)
                .ThenBy(map => map!.Id)
                .Select(map => new ProjectExplorerNode
                {
                    Name = $"{map!.Id:000} — {ValueOrFallback(map.Name, $"Map {map.Id:000}")}",
                    Kind = ProjectExplorerNodeKind.Map,
                    Path = Path.Combine(dataPath, $"Map{map.Id:000}.json"),
                })
                .ToArray();

            return new ProjectExplorerNode
            {
                Name = "Mappe",
                Kind = ProjectExplorerNodeKind.Category,
                Path = mapInfosPath,
                Children = maps,
            };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ProjectExplorerNode
            {
                Name = "Mappe",
                Kind = ProjectExplorerNodeKind.Category,
                Path = mapInfosPath,
                Children =
                [
                    new ProjectExplorerNode
                    {
                        Name = "MapInfos.json non leggibile",
                        Kind = ProjectExplorerNodeKind.Information,
                        Path = mapInfosPath,
                    },
                ],
            };
        }
    }

    private ProjectExplorerNode BuildSystemNode(string projectPath)
    {
        var children = new List<ProjectExplorerNode>();
        var pluginsPath = Path.Combine(projectPath, "js", "plugins");
        if (_fileSystem.DirectoryExists(pluginsPath))
        {
            var plugins = _fileSystem.EnumerateFiles(pluginsPath, "*.js")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Take(MaximumPlugins)
                .Select(path => new ProjectExplorerNode
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    Kind = ProjectExplorerNodeKind.Plugin,
                    Path = path,
                })
                .ToArray();
            children.Add(new ProjectExplorerNode
            {
                Name = "Plugin",
                Kind = ProjectExplorerNodeKind.Category,
                Path = pluginsPath,
                Children = plugins,
            });
        }

        var configurationFiles = new List<ProjectExplorerNode>();
        AddIfPresent(configurationFiles, Path.Combine(projectPath, "package.json"), "Runtime package");
        foreach (var markerPath in _fileSystem.EnumerateFiles(projectPath, "*.rmmzproject"))
        {
            configurationFiles.Add(new ProjectExplorerNode
            {
                Name = Path.GetFileName(markerPath),
                Kind = ProjectExplorerNodeKind.File,
                Path = markerPath,
            });
        }

        if (configurationFiles.Count > 0)
        {
            children.Add(new ProjectExplorerNode
            {
                Name = "Configurazione",
                Kind = ProjectExplorerNodeKind.Category,
                Path = projectPath,
                Children = configurationFiles,
            });
        }

        return new ProjectExplorerNode
        {
            Name = "System",
            Kind = ProjectExplorerNodeKind.Category,
            Path = projectPath,
            Children = children,
        };
    }

    private ProjectExplorerNode BuildAssetsNode(string projectPath)
    {
        var assets = new List<ProjectExplorerNode>();
        AddDirectoryIfPresent(assets, projectPath, "img", "Immagini");
        var audioPath = Path.Combine(projectPath, "audio");
        if (_fileSystem.DirectoryExists(audioPath))
        {
            var remaining = MaximumAudioAssets;
            assets.Add(BuildAudioNode(audioPath, "Audio", ref remaining, depth: 0));
        }
        AddDirectoryIfPresent(assets, projectPath, "fonts", "Font");

        return new ProjectExplorerNode
        {
            Name = "Assets",
            Kind = ProjectExplorerNodeKind.Category,
            Path = projectPath,
            Children = assets,
        };
    }

    private ProjectExplorerNode BuildAudioNode(
        string path,
        string displayName,
        ref int remaining,
        int depth)
    {
        if (remaining <= 0 || depth > 8)
        {
            return new ProjectExplorerNode
            {
                Name = displayName,
                Kind = depth == 0 ? ProjectExplorerNodeKind.Asset : ProjectExplorerNodeKind.Directory,
                Path = path,
            };
        }

        var children = new List<ProjectExplorerNode>();
        foreach (var directory in _fileSystem.EnumerateDirectories(path)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (remaining <= 0) break;
            children.Add(BuildAudioNode(
                directory,
                Path.GetFileName(directory).ToUpperInvariant(),
                ref remaining,
                depth + 1));
        }

        foreach (var file in _fileSystem.EnumerateFiles(path, "*.*")
            .Where(file => Path.GetExtension(file).ToLowerInvariant() is
                ".ogg" or ".m4a" or ".wav" or ".mp3")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (remaining-- <= 0) break;
            children.Add(new ProjectExplorerNode
            {
                Name = Path.GetFileName(file),
                Kind = ProjectExplorerNodeKind.Asset,
                Path = file,
            });
        }

        return new ProjectExplorerNode
        {
            Name = displayName,
            Kind = depth == 0 ? ProjectExplorerNodeKind.Asset : ProjectExplorerNodeKind.Directory,
            Path = path,
            Children = children,
        };
    }

    private ProjectExplorerNode? CreateFileNode(
        string directoryPath,
        string fileName,
        string displayName,
        ProjectExplorerNodeKind kind,
        DocumentDescriptor? document = null)
    {
        var path = Path.Combine(directoryPath, fileName);
        return _fileSystem.FileExists(path)
            ? new ProjectExplorerNode
            {
                Name = displayName,
                Kind = kind,
                Path = path,
                Document = document,
            }
            : null;
    }

    private static DocumentDescriptor CreateDatabaseDescriptor(
        ZiapProject project,
        string displayName,
        string resourceName) => new()
    {
        Id = new DocumentId($"{project.Id}:rpgmaker:database:{resourceName}"),
        DisplayName = displayName,
        Kind = DocumentKind.RpgMakerDatabase,
        ResourceId = new Uri($"rpgmaker://database/{resourceName}"),
    };

    private void AddDirectoryIfPresent(
        ICollection<ProjectExplorerNode> nodes,
        string projectPath,
        string relativePath,
        string displayName)
    {
        var path = Path.Combine(projectPath, relativePath);
        if (_fileSystem.DirectoryExists(path))
        {
            nodes.Add(new ProjectExplorerNode
            {
                Name = displayName,
                Kind = ProjectExplorerNodeKind.Asset,
                Path = path,
            });
        }
    }

    private void AddIfPresent(
        ICollection<ProjectExplorerNode> nodes,
        string path,
        string displayName)
    {
        if (_fileSystem.FileExists(path))
        {
            nodes.Add(new ProjectExplorerNode
            {
                Name = displayName,
                Kind = ProjectExplorerNodeKind.File,
                Path = path,
            });
        }
    }

    private static string ValueOrFallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private sealed class RpgMakerMapInfo
    {
        public int Id { get; init; }

        public string? Name { get; init; }

        public int Order { get; init; }
    }
}
