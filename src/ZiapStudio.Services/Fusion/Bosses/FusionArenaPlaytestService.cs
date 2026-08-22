using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZiapStudio.Services.Fusion.Bosses;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FusionArenaPlaytestLaunchMode
{
    Direct,
    Narrative,
    MapOnly,
}

public sealed record FusionArenaPlaytestProfile
{
    public string ArenaId { get; init; } = string.Empty;

    public FusionArenaPlaytestLaunchMode Mode { get; init; } =
        FusionArenaPlaytestLaunchMode.Direct;

    public int MapId { get; init; }

    public int PlayerX { get; init; }

    public int PlayerY { get; init; }

    public int Direction { get; init; } = 2;

    public int CommonEventId { get; init; }
}

public sealed record FusionArenaPlaytestSettings
{
    public int SchemaVersion { get; init; } = 1;

    public string RuntimeExecutable { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, FusionArenaPlaytestProfile> Profiles { get; init; } =
        new Dictionary<string, FusionArenaPlaytestProfile>(StringComparer.OrdinalIgnoreCase);
}

public sealed record FusionArenaPlaytestLoadout
{
    public int ActorId { get; init; } = 1;

    public string ActorName { get; init; } = "Yato";

    public int Level { get; init; } = 50;

    public int WeaponId { get; init; } = 132;

    public IReadOnlyList<int> ArmorIds { get; init; } = [1, 2, 3, 4, 26, 24, 24, 28];

    public IReadOnlyList<int> SkillIds { get; init; } = [7, 15, 61];
}

public sealed record FusionArenaPlaytestSession
{
    public int SchemaVersion { get; init; } = 1;

    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.UtcNow.AddMinutes(15);

    public string ArenaId { get; init; } = string.Empty;

    public string EncounterId { get; init; } = string.Empty;

    public FusionArenaPlaytestLaunchMode Mode { get; init; }

    public int MapId { get; init; }

    public int PlayerX { get; init; }

    public int PlayerY { get; init; }

    public int Direction { get; init; }

    public int CommonEventId { get; init; }

    public FusionArenaPlaytestLoadout Loadout { get; init; } = new();
}

public sealed record FusionArenaPlaytestLaunchResult
{
    public required int ProcessId { get; init; }

    public required string SessionPath { get; init; }

    public required FusionArenaPlaytestSession Session { get; init; }
}

public sealed class FusionArenaPlaytestService
{
    public const string SessionEnvironmentVariable = "ZIAP_ARENA_PLAYTEST";
    public const string SessionPayloadEnvironmentVariable = "ZIAP_ARENA_PLAYTEST_PAYLOAD";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _storageRoot;

    public FusionArenaPlaytestService(string? storageRoot = null)
    {
        _storageRoot = storageRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZIAP Studio",
            "fusion-arena-playtest");
    }

    public async Task<FusionArenaPlaytestSettings> LoadSettingsAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var settingsPath = GetSettingsPath(projectPath);
        if (!File.Exists(settingsPath))
        {
            return new FusionArenaPlaytestSettings();
        }

        await using var stream = File.OpenRead(settingsPath);
        return await JsonSerializer.DeserializeAsync<FusionArenaPlaytestSettings>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? new FusionArenaPlaytestSettings();
    }

    public async Task SaveSettingsAsync(
        string projectPath,
        FusionArenaPlaytestSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(_storageRoot);
        var settingsPath = GetSettingsPath(projectPath);
        await using var stream = File.Create(settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }

    public IReadOnlyList<string> DiscoverRuntimeExecutables()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRuntimeCandidate(candidates, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam", "steamapps", "common", "RPG Maker MZ", "nwjs-win", "nw.exe"));
        AddRuntimeCandidate(candidates, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Steam", "steamapps", "common", "RPG Maker MZ", "nwjs-win", "nw.exe"));

        foreach (var drive in DriveInfo.GetDrives().Where(candidate =>
                     candidate.DriveType == DriveType.Fixed && candidate.IsReady))
        {
            AddRuntimeCandidate(candidates, Path.Combine(
                drive.RootDirectory.FullName,
                "SteamLibrary", "steamapps", "common", "RPG Maker MZ", "nwjs-win", "nw.exe"));
        }

        return candidates.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<FusionArenaPlaytestLaunchResult> LaunchAsync(
        string projectPath,
        string encounterId,
        FusionArenaPlaytestSettings settings,
        FusionArenaPlaytestProfile profile,
        CancellationToken cancellationToken = default)
    {
        ValidateLaunch(projectPath, settings.RuntimeExecutable, profile);
        var session = CreateSession(encounterId, profile);
        var sessionPath = await WriteSessionAsync(session, cancellationToken);
        var startInfo = CreateStartInfo(
            settings.RuntimeExecutable,
            projectPath,
            sessionPath,
            session);
        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Il runtime RPG Maker MZ non ha restituito un processo.");
        return new FusionArenaPlaytestLaunchResult
        {
            ProcessId = process.Id,
            SessionPath = sessionPath,
            Session = session,
        };
    }

    public FusionArenaPlaytestSession CreateSession(
        string encounterId,
        FusionArenaPlaytestProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new FusionArenaPlaytestSession
        {
            ArenaId = profile.ArenaId,
            EncounterId = encounterId.Trim(),
            Mode = profile.Mode,
            MapId = profile.MapId,
            PlayerX = profile.PlayerX,
            PlayerY = profile.PlayerY,
            Direction = profile.Direction,
            CommonEventId = profile.CommonEventId,
        };
    }

    public ProcessStartInfo CreateStartInfo(
        string runtimeExecutable,
        string projectPath,
        string sessionPath,
        FusionArenaPlaytestSession? session = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(runtimeExecutable),
            WorkingDirectory = Path.GetFullPath(projectPath),
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.GetFullPath(projectPath));
        startInfo.ArgumentList.Add("test");
        startInfo.Environment[SessionEnvironmentVariable] = Path.GetFullPath(sessionPath);
        if (session is not null)
        {
            var payload = Convert.ToBase64String(
                JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions));
            if (payload.Length >= 30_000)
            {
                throw new InvalidOperationException("La sessione playtest supera il limite dell'ambiente Windows.");
            }
            startInfo.Environment[SessionPayloadEnvironmentVariable] = payload;
        }
        return startInfo;
    }

    public string GetSettingsPath(string projectPath)
    {
        var normalized = Path.GetFullPath(projectPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
        return Path.Combine(_storageRoot, $"{hash}.settings.json");
    }

    private async Task<string> WriteSessionAsync(
        FusionArenaPlaytestSession session,
        CancellationToken cancellationToken)
    {
        var sessionsPath = Path.Combine(_storageRoot, "sessions");
        Directory.CreateDirectory(sessionsPath);
        CleanupExpiredSessions(sessionsPath);
        var sessionPath = Path.Combine(sessionsPath, $"{session.SessionId}.json");
        await using var stream = File.Create(sessionPath);
        await JsonSerializer.SerializeAsync(stream, session, JsonOptions, cancellationToken);
        return sessionPath;
    }

    private static void ValidateLaunch(
        string projectPath,
        string runtimeExecutable,
        FusionArenaPlaytestProfile profile)
    {
        if (!Directory.Exists(projectPath) ||
            !File.Exists(Path.Combine(projectPath, "package.json")))
        {
            throw new DirectoryNotFoundException("Il progetto RPG Maker MZ non è valido.");
        }
        if (!File.Exists(runtimeExecutable))
        {
            throw new FileNotFoundException("Seleziona un runtime nw.exe valido.", runtimeExecutable);
        }
        if (string.IsNullOrWhiteSpace(profile.ArenaId) || profile.MapId <= 0)
        {
            throw new InvalidOperationException("Il profilo playtest dell'arena è incompleto.");
        }
        if (profile.PlayerX < 0 || profile.PlayerY < 0 ||
            profile.Direction is not (2 or 4 or 6 or 8))
        {
            throw new InvalidOperationException("Posizione o direzione del giocatore non valida.");
        }
    }

    private static void AddRuntimeCandidate(ISet<string> candidates, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            candidates.Add(Path.GetFullPath(path));
        }
    }

    private static void CleanupExpiredSessions(string sessionsPath)
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        foreach (var file in Directory.EnumerateFiles(sessionsPath, "*.json"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
