using System.Text.Json;
using ZiapStudio.Services.Fusion.Bosses;

namespace ZiapStudio.Services.Tests;

public sealed class FusionArenaPlaytestServiceTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ziap-playtest-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateSession_UsesFixedYatoLoadoutAndArenaEntry()
    {
        var service = new FusionArenaPlaytestService(_temporaryRoot);
        var profile = new FusionArenaPlaytestProfile
        {
            ArenaId = "organizationChiefStory",
            Mode = FusionArenaPlaytestLaunchMode.Direct,
            MapId = 74,
            PlayerX = 17,
            PlayerY = 40,
            Direction = 8,
        };

        var session = service.CreateSession("organizationChief", profile);

        Assert.Equal("organizationChiefStory", session.ArenaId);
        Assert.Equal("organizationChief", session.EncounterId);
        Assert.Equal(74, session.MapId);
        Assert.Equal(1, session.Loadout.ActorId);
        Assert.Equal(50, session.Loadout.Level);
        Assert.Equal(132, session.Loadout.WeaponId);
        Assert.Equal([1, 2, 3, 4, 26, 24, 24, 28], session.Loadout.ArmorIds);
        Assert.Equal([7, 15, 61], session.Loadout.SkillIds);
    }

    [Fact]
    public async Task Settings_RoundTripPerProject()
    {
        var service = new FusionArenaPlaytestService(_temporaryRoot);
        var projectPath = Path.Combine(_temporaryRoot, "project");
        var profile = new FusionArenaPlaytestProfile
        {
            ArenaId = "broodQueenTrial",
            Mode = FusionArenaPlaytestLaunchMode.MapOnly,
            MapId = 87,
            PlayerX = 27,
            PlayerY = 37,
        };
        var settings = new FusionArenaPlaytestSettings
        {
            RuntimeExecutable = @"C:\RPG Maker MZ\nw.exe",
            Profiles = new Dictionary<string, FusionArenaPlaytestProfile>
            {
                [profile.ArenaId] = profile,
            },
        };

        await service.SaveSettingsAsync(projectPath, settings);
        var loaded = await service.LoadSettingsAsync(projectPath);

        Assert.Equal(settings.RuntimeExecutable, loaded.RuntimeExecutable);
        Assert.Equal(profile, loaded.Profiles[profile.ArenaId]);
    }

    [Fact]
    public void CreateStartInfo_LoadsProjectAsPlaytestAndPassesSessionEnvironment()
    {
        var service = new FusionArenaPlaytestService(_temporaryRoot);
        var projectPath = Path.Combine(_temporaryRoot, "project");
        var runtimePath = Path.Combine(_temporaryRoot, "runtime", "nw.exe");
        var sessionPath = Path.Combine(_temporaryRoot, "session.json");

        var startInfo = service.CreateStartInfo(runtimePath, projectPath, sessionPath);

        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(Path.GetFullPath(projectPath), startInfo.WorkingDirectory);
        Assert.Equal([Path.GetFullPath(projectPath), "test"], startInfo.ArgumentList);
        Assert.Equal(
            Path.GetFullPath(sessionPath),
            startInfo.Environment[FusionArenaPlaytestService.SessionEnvironmentVariable]);
    }

    [Fact]
    public void CreateStartInfo_EmbedsSessionPayloadWithoutDependingOnSessionFile()
    {
        var service = new FusionArenaPlaytestService(_temporaryRoot);
        var session = service.CreateSession("organizationChief", new FusionArenaPlaytestProfile
        {
            ArenaId = "organizationChiefStory",
            Mode = FusionArenaPlaytestLaunchMode.Direct,
            MapId = 74,
            PlayerX = 17,
            PlayerY = 40,
            Direction = 2,
        });

        var startInfo = service.CreateStartInfo(
            Path.Combine(_temporaryRoot, "nw.exe"),
            Path.Combine(_temporaryRoot, "project"),
            Path.Combine(_temporaryRoot, "missing-session.json"),
            session);

        var payload = startInfo.Environment[
            FusionArenaPlaytestService.SessionPayloadEnvironmentVariable];
        Assert.False(string.IsNullOrWhiteSpace(payload));
        using var document = JsonDocument.Parse(Convert.FromBase64String(payload!));
        Assert.Equal(
            "organizationChiefStory",
            document.RootElement.GetProperty("arenaId").GetString());
        Assert.Equal(74, document.RootElement.GetProperty("mapId").GetInt32());
    }

    [Fact]
    public async Task Settings_UsesCamelCaseAndStringLaunchMode()
    {
        var service = new FusionArenaPlaytestService(_temporaryRoot);
        var projectPath = Path.Combine(_temporaryRoot, "project");
        await service.SaveSettingsAsync(projectPath, new FusionArenaPlaytestSettings
        {
            Profiles = new Dictionary<string, FusionArenaPlaytestProfile>
            {
                ["arena"] = new()
                {
                    ArenaId = "arena",
                    Mode = FusionArenaPlaytestLaunchMode.Narrative,
                    MapId = 12,
                },
            },
        });

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(
            service.GetSettingsPath(projectPath)));

        Assert.Equal(
            "narrative",
            document.RootElement.GetProperty("profiles").GetProperty("arena").GetProperty("mode").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }
}
