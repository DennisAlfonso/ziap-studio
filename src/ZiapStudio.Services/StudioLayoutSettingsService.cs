using System.Text.Json;

namespace ZiapStudio.Services;

public sealed record StudioLayoutSettings
{
    public const int DefaultWindowWidth = 1600;
    public const int DefaultWindowHeight = 950;
    public const double DefaultExplorerWidth = 300;
    public const double DefaultDatabaseListWidth = 620;

    public int SchemaVersion { get; init; } = 1;

    public int? WindowX { get; init; }

    public int? WindowY { get; init; }

    public int WindowWidth { get; init; } = DefaultWindowWidth;

    public int WindowHeight { get; init; } = DefaultWindowHeight;

    public bool IsMaximized { get; init; }

    public bool IsProjectPaneExpanded { get; init; }

    public double ExplorerWidth { get; init; } = DefaultExplorerWidth;

    public double DatabaseListWidth { get; init; } = DefaultDatabaseListWidth;
}

public sealed class StudioLayoutSettingsService
{
    private const int MinimumWindowWidth = 1100;
    private const int MinimumWindowHeight = 700;
    private const double MinimumExplorerWidth = 220;
    private const double MaximumExplorerWidth = 550;
    private const double MinimumDatabaseListWidth = 360;
    private const double MaximumDatabaseListWidth = 1200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly FileSystemService _fileSystem;
    private readonly string _settingsPath;

    public StudioLayoutSettingsService(FileSystemService fileSystem, string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _fileSystem = fileSystem;
        _settingsPath = settingsPath;
    }

    public async Task<StudioLayoutSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.FileExists(_settingsPath))
        {
            return new StudioLayoutSettings();
        }

        try
        {
            var json = await _fileSystem.ReadAllTextAsync(_settingsPath, cancellationToken);
            return Normalize(
                JsonSerializer.Deserialize<StudioLayoutSettings>(json, JsonOptions) ??
                new StudioLayoutSettings());
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new StudioLayoutSettings();
        }
    }

    public async Task SaveAsync(
        StudioLayoutSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(settings);
        var settingsDirectory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException(
                "Il percorso dello stato layout non ha una cartella padre.");
        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            _fileSystem.CreateDirectory(settingsDirectory);
            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            await _fileSystem.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            _fileSystem.MoveFile(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new SettingsException(
                "Impossibile salvare la disposizione di ZIAP Studio.",
                exception);
        }
        finally
        {
            if (_fileSystem.FileExists(temporaryPath))
            {
                _fileSystem.DeleteFile(temporaryPath);
            }
        }
    }

    private static StudioLayoutSettings Normalize(StudioLayoutSettings settings) =>
        settings with
        {
            WindowWidth = Math.Max(MinimumWindowWidth, settings.WindowWidth),
            WindowHeight = Math.Max(MinimumWindowHeight, settings.WindowHeight),
            ExplorerWidth = Math.Clamp(
                settings.ExplorerWidth,
                MinimumExplorerWidth,
                MaximumExplorerWidth),
            DatabaseListWidth = Math.Clamp(
                settings.DatabaseListWidth,
                MinimumDatabaseListWidth,
                MaximumDatabaseListWidth),
        };
}
