using ZiapStudio.Services;
using ZiapStudio.Services.Fusion.Bosses;
using ZiapStudio.Services.Integrations;

namespace ZiapStudio.Services.Tests;

public sealed class FusionBossActionApiServiceTests
{
    [Fact]
    public async Task LoadAsync_IndexesOnlyActionsRegisteredByActivePlugins()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "js/plugins.js",
            """
            var $plugins = [
              {"name":"zenkaiDevPlugins/ZDP_TestActions","status":true,"description":"","parameters":{}},
              {"name":"zenkaiDevPlugins/ZDP_InactiveActions","status":false,"description":"","parameters":{}}
            ];
            """);
        workspace.WriteFile(
            "js/plugins/zenkaiDevPlugins/ZDP_TestActions.js",
            """
            FusionEncounter.registerAction("showBossUI", context => context.showBossUi());
            FusionEncounter.registerAction("combat.cast", (context, config) => cast(config));
            """);
        workspace.WriteFile(
            "js/plugins/zenkaiDevPlugins/ZDP_InactiveActions.js",
            """
            FusionEncounter.registerAction("hidden.action", (context, value) => value);
            """);

        var documentation = await CreateService().LoadAsync(workspace.RootPath);

        Assert.Equal(2, documentation.Actions.Count);
        var showBossUi = Assert.Single(documentation.Actions, action => action.Id == "showBossUI");
        Assert.Equal("[\"showBossUI\"]", showBossUi.Signature);
        Assert.Equal("TestActions", showBossUi.Provider);
        Assert.Equal(1, showBossUi.SourceLine);
        var cast = Assert.Single(documentation.Actions, action => action.Id == "combat.cast");
        Assert.Equal("[\"combat.cast\", config]", cast.Signature);
        Assert.DoesNotContain(documentation.Actions, action => action.Id == "hidden.action");
    }

    [Fact]
    public async Task LoadAsync_UsesEditorialMetadataWithoutRequiringDescriptions()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "js/plugins.js",
            """
            var $plugins = [
              {"name":"ZDP_CustomActions","status":true,"description":"","parameters":{}}
            ];
            """);
        workspace.WriteFile(
            "js/plugins/ZDP_CustomActions.js",
            "FusionEncounter.registerAction(\"custom.flash\", (context, options) => flash(options));");
        workspace.WriteFile(
            "data/FusionActionCatalog.json",
            """
            {
              "actions": {
                "custom.flash": {
                  "displayName": "Flash custom",
                  "category": "Presentazione",
                  "icon": "✦"
                }
              }
            }
            """);

        var documentation = await CreateService().LoadAsync(workspace.RootPath);

        var action = Assert.Single(documentation.Actions);
        Assert.Equal("Flash custom", action.DisplayName);
        Assert.Equal("Presentazione", action.Category);
        Assert.Equal("✦", action.IconGlyph);
        Assert.Equal("[\"custom.flash\", options]", action.Signature);
    }

    private static FusionBossActionApiService CreateService()
    {
        var fileSystem = new FileSystemService();
        return new FusionBossActionApiService(
            fileSystem,
            new RpgMakerPluginRegistryService(fileSystem));
    }
}
