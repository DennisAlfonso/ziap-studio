using System.Text.RegularExpressions;
using ZiapStudio.Core.Fusion.Bosses;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Integrations;

namespace ZiapStudio.Services.Fusion.Bosses;

public sealed partial class FusionBossActionApiService
{
    private readonly FileSystemService _fileSystem;
    private readonly RpgMakerPluginRegistryService _pluginRegistry;

    public FusionBossActionApiService(
        FileSystemService fileSystem,
        RpgMakerPluginRegistryService pluginRegistry)
    {
        _fileSystem = fileSystem;
        _pluginRegistry = pluginRegistry;
    }

    public async Task<FusionBossDocumentation> LoadAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var project = new ZiapProject
        {
            Id = "fusion-boss-docs",
            Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(projectPath)),
            ProjectType = KnownProjectTypes.RpgMakerMz,
            Path = projectPath,
        };
        var registrations = await _pluginRegistry.LoadAsync(project, cancellationToken);
        var catalog = await FusionBossActionCatalog.LoadAsync(
            _fileSystem,
            projectPath,
            cancellationToken);
        var actions = new Dictionary<string, FusionBossActionApiEntry>(
            StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();

        foreach (var registration in registrations.Where(registration => registration.IsActive))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = ResolvePluginPath(projectPath, registration.Name);
            if (!_fileSystem.FileExists(sourcePath))
            {
                continue;
            }
            var source = await _fileSystem.ReadAllTextAsync(sourcePath, cancellationToken);
            foreach (Match match in ActionRegistrationRegex().Matches(source))
            {
                var actionId = match.Groups["id"].Value;
                if (string.IsNullOrWhiteSpace(actionId))
                {
                    continue;
                }
                var parameters = HandlerParameters(match);
                var semantics = catalog.Describe(actionId, [], attackGeometry: null);
                var entry = new FusionBossActionApiEntry
                {
                    Id = actionId,
                    DisplayName = semantics.DisplayName,
                    Category = semantics.Category,
                    IconGlyph = semantics.IconGlyph,
                    Signature = ActionSignature(actionId, parameters),
                    Provider = ProviderName(registration.Name),
                    SourcePath = Path.GetRelativePath(projectPath, sourcePath)
                        .Replace(Path.DirectorySeparatorChar, '/'),
                    SourceLine = SourceLine(source, match.Index),
                };
                if (!actions.TryAdd(actionId, entry))
                {
                    diagnostics.Add(
                        $"L'azione “{actionId}” è registrata anche da {entry.Provider}; " +
                        $"la documentazione usa la prima registrazione attiva.");
                }
            }
        }

        return new FusionBossDocumentation
        {
            ProjectPath = projectPath,
            Actions = actions.Values
                .OrderBy(action => action.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Diagnostics = diagnostics,
        };
    }

    private static string ResolvePluginPath(string projectPath, string registeredName)
    {
        var relativeName = registeredName.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        if (!relativeName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            relativeName += ".js";
        }
        return Path.Combine(projectPath, "js", "plugins", relativeName);
    }

    private static IReadOnlyList<string> HandlerParameters(Match match)
    {
        var raw = match.Groups["parameters"].Success
            ? match.Groups["parameters"].Value
            : match.Groups["singleParameter"].Value;
        var parameters = raw.Split(',', StringSplitOptions.TrimEntries |
            StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parameters.Count > 0 && parameters[0].Equals(
            "context",
            StringComparison.OrdinalIgnoreCase))
        {
            parameters.RemoveAt(0);
        }
        return parameters;
    }

    private static string ActionSignature(string actionId, IReadOnlyList<string> parameters) =>
        parameters.Count == 0
            ? $"[\"{actionId}\"]"
            : $"[\"{actionId}\", {string.Join(", ", parameters)}]";

    private static int SourceLine(string source, int index)
    {
        var line = 1;
        for (var position = 0; position < index; position++)
        {
            if (source[position] == '\n')
            {
                line++;
            }
        }
        return line;
    }

    private static string ProviderName(string registeredName)
    {
        var leaf = Path.GetFileNameWithoutExtension(
            registeredName.Replace('/', Path.DirectorySeparatorChar));
        return leaf.StartsWith("ZDP_", StringComparison.OrdinalIgnoreCase)
            ? leaf[4..]
            : leaf;
    }

    [GeneratedRegex(
        "FusionEncounter\\.registerAction\\(\\s*[\\\"'](?<id>[^\\\"']+)[\\\"']\\s*,\\s*(?:\\((?<parameters>[^)]*)\\)|(?<singleParameter>[A-Za-z_$][\\w$]*))\\s*=>",
        RegexOptions.CultureInvariant)]
    private static partial Regex ActionRegistrationRegex();
}
