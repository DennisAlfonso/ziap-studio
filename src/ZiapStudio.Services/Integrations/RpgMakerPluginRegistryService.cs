using System.Text.Json;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services.Integrations;

public sealed class RpgMakerPluginRegistryService
{
    private readonly FileSystemService _fileSystem;

    public RpgMakerPluginRegistryService(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<IReadOnlyList<RpgMakerPluginRegistration>> LoadAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var path = Path.Combine(project.Path, "js", "plugins.js");
        if (!_fileSystem.FileExists(path))
        {
            return [];
        }

        var source = await _fileSystem.ReadAllTextAsync(path, cancellationToken);
        var assignment = source.IndexOf("var $plugins", StringComparison.Ordinal);
        var arrayStart = assignment < 0 ? -1 : source.IndexOf('[', assignment);
        var arrayEnd = source.LastIndexOf(']');
        if (arrayStart < 0 || arrayEnd <= arrayStart)
        {
            throw new JsonException("js/plugins.js non contiene il registry $plugins atteso.");
        }

        using var document = JsonDocument.Parse(source[arrayStart..(arrayEnd + 1)]);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Il registry $plugins non è un array.");
        }

        return document.RootElement.EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.Object)
            .Select(element => new RpgMakerPluginRegistration
            {
                Name = ReadString(element, "name"),
                Description = ReadString(element, "description"),
                IsActive = element.TryGetProperty("status", out var status) &&
                    status.ValueKind == JsonValueKind.True,
            })
            .Where(plugin => !string.IsNullOrWhiteSpace(plugin.Name))
            .ToArray();
    }

    public async Task<bool> IsActiveAsync(
        ZiapProject project,
        string pluginName,
        CancellationToken cancellationToken = default)
    {
        var plugins = await LoadAsync(project, cancellationToken);
        return plugins.Any(plugin => plugin.IsActive && PluginNameEquals(plugin.Name, pluginName));
    }

    public static bool PluginNameEquals(string registeredName, string requestedName)
    {
        var normalized = registeredName.Replace('/', Path.DirectorySeparatorChar);
        var leafName = Path.GetFileNameWithoutExtension(normalized);
        return leafName.Equals(requestedName, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
}

public sealed record RpgMakerPluginRegistration
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}
