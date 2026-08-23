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
        Assert.Equal(12, session.JumpValue);
        Assert.Equal(12, session.VariableOverrides[11]);
    }

    [Fact]
    public void CreateSession_UsesConfiguredLoadoutAndJumpOverride()
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
            JumpValue = 21,
            Loadout = new FusionArenaPlaytestLoadout
            {
                ActorId = 3,
                ActorName = "Kael",
                Level = 63,
                WeaponId = 9,
                ArmorIds = [5, 6, 7, 8, 9, 10, 11, 12],
                SkillIds = [2, 4, 8, 16],
            },
        };

        var session = service.CreateSession("organizationChief", profile);

        Assert.Equal(3, session.Loadout.ActorId);
        Assert.Equal(63, session.Loadout.Level);
        Assert.Equal(9, session.Loadout.WeaponId);
        Assert.Equal([5, 6, 7, 8, 9, 10, 11, 12], session.Loadout.ArmorIds);
        Assert.Equal([2, 4, 8, 16], session.Loadout.SkillIds);
        Assert.Equal(21, session.JumpValue);
        Assert.Equal(21, session.VariableOverrides[11]);
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
        var loadedProfile = loaded.Profiles[profile.ArenaId];
        Assert.Equal(profile.ArenaId, loadedProfile.ArenaId);
        Assert.Equal(profile.Mode, loadedProfile.Mode);
        Assert.Equal(profile.MapId, loadedProfile.MapId);
        Assert.Equal(profile.PlayerX, loadedProfile.PlayerX);
        Assert.Equal(profile.PlayerY, loadedProfile.PlayerY);
        Assert.Equal(profile.Direction, loadedProfile.Direction);
        Assert.Equal(profile.CommonEventId, loadedProfile.CommonEventId);
        Assert.Equal(profile.JumpValue, loadedProfile.JumpValue);
        Assert.Equal(profile.Loadout.ActorId, loadedProfile.Loadout.ActorId);
        Assert.Equal(profile.Loadout.ActorName, loadedProfile.Loadout.ActorName);
        Assert.Equal(profile.Loadout.Level, loadedProfile.Loadout.Level);
        Assert.Equal(profile.Loadout.WeaponId, loadedProfile.Loadout.WeaponId);
        Assert.Equal(profile.Loadout.ArmorIds, loadedProfile.Loadout.ArmorIds);
        Assert.Equal(profile.Loadout.SkillIds, loadedProfile.Loadout.SkillIds);
    }

    [Fact]
    public async Task LoadCatalogAsync_ReadsRealRpgMakerOptions()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "project/data/Actors.json",
            """
            [
              null,
              { "id": 1, "name": "Yato" },
              { "id": 3, "name": "Kara" }
            ]
            """);
        workspace.WriteFile(
            "project/data/Weapons.json",
            """
            [
              null,
              { "id": 9, "name": "Lama" }
            ]
            """);
        workspace.WriteFile(
            "project/data/Armors.json",
            """
            [
              null,
              { "id": 5, "name": "Corazza" }
            ]
            """);
        workspace.WriteFile(
            "project/data/Skills.json",
            """
            [
              null,
              { "id": 7, "name": "Taglio" }
            ]
            """);

        var service = new FusionArenaPlaytestService(_temporaryRoot);

        var catalog = await service.LoadCatalogAsync(Path.Combine(workspace.RootPath, "project"));

        Assert.Equal(["#001 · Yato", "#003 · Kara"], catalog.Actors.Select(option => option.DisplayName));
        Assert.Equal(["#009 · Lama"], catalog.Weapons.Select(option => option.DisplayName));
        Assert.Equal(["#005 · Corazza"], catalog.Armors.Select(option => option.DisplayName));
        Assert.Equal(["#007 · Taglio"], catalog.Skills.Select(option => option.DisplayName));
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
        Assert.Equal(
            12,
            document.RootElement
                .GetProperty("variableOverrides")
                .GetProperty("11")
                .GetInt32());
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
