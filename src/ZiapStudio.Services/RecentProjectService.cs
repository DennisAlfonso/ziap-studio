using System.Text.Json;
using ZiapStudio.Core.Models;

namespace ZiapStudio.Services;

public sealed class RecentProjectService : IRecentProjectService
{
    public const int MaximumRecentProjects = 10;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly FileSystemService _fileSystem;
    private readonly string _settingsPath;

    public RecentProjectService(FileSystemService fileSystem, string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        _fileSystem = fileSystem;
        _settingsPath = settingsPath;
    }

    public async Task<IReadOnlyList<ZiapProject>> GetRecentProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.FileExists(_settingsPath))
        {
            return Array.Empty<ZiapProject>();
        }

        try
        {
            var json = await _fileSystem.ReadAllTextAsync(_settingsPath, cancellationToken);
            var settings = JsonSerializer.Deserialize<StudioSettings>(json, JsonOptions);

            return settings?.RecentProjects?
                .Where(IsUsable)
                .Take(MaximumRecentProjects)
                .ToArray() ?? Array.Empty<ZiapProject>();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new SettingsException("Impossibile leggere l'elenco dei progetti recenti.", exception);
        }
    }

    public async Task<IReadOnlyList<ZiapProject>> AddAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var recentProjects = await GetRecentProjectsAsync(cancellationToken);
        var updatedProjects = recentProjects
            .Where(recent => !string.Equals(recent.Path, project.Path, StringComparison.OrdinalIgnoreCase))
            .Prepend(project)
            .Take(MaximumRecentProjects)
            .ToArray();

        await SaveAsync(updatedProjects, cancellationToken);
        return updatedProjects;
    }

    private async Task SaveAsync(
        IReadOnlyList<ZiapProject> recentProjects,
        CancellationToken cancellationToken)
    {
        var settingsDirectory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("Il percorso delle impostazioni non ha una cartella padre.");
        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            _fileSystem.CreateDirectory(settingsDirectory);
            var json = JsonSerializer.Serialize(
                new StudioSettings { RecentProjects = recentProjects.ToList() },
                JsonOptions);
            await _fileSystem.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            _fileSystem.MoveFile(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SettingsException("Impossibile salvare l'elenco dei progetti recenti.", exception);
        }
        finally
        {
            if (_fileSystem.FileExists(temporaryPath))
            {
                _fileSystem.DeleteFile(temporaryPath);
            }
        }
    }

    private static bool IsUsable(ZiapProject project) =>
        !string.IsNullOrWhiteSpace(project.Name) && !string.IsNullOrWhiteSpace(project.Path);

    private sealed class StudioSettings
    {
        public int SchemaVersion { get; init; } = 1;

        public List<ZiapProject> RecentProjects { get; init; } = [];
    }
}
