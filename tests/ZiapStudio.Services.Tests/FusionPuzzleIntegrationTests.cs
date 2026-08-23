using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Puzzles;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Fusion.Puzzles;
using ZiapStudio.Services.Integrations;
using ZiapStudio.Services.Providers;

namespace ZiapStudio.Services.Tests;

public sealed class FusionPuzzleIntegrationTests
{
    [Fact]
    public async Task ProjectIntegration_AddsPuzzleWorkspaceWhenCorePluginIsActive()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("data/System.json", "{\"gameTitle\":\"Puzzle Test\"}");
        WritePlugins(workspace, includeProvider: true);

        var roots = await new ProjectProviderService(new FileSystemService())
            .BuildExplorerAsync(CreateProject(workspace.RootPath));

        var node = roots.Single(root => root.Name == "System")
            .Children.Single(child => child.Name == "Plugin")
            .Children.Single(child => child.Name == "Fusion Puzzle");
        Assert.Equal(ProjectExplorerNodeKind.Integration, node.Kind);
        Assert.Equal(FusionPuzzleIntegrationProvider.ResourceUri, node.Document!.ResourceId.AbsoluteUri);
    }

    [Fact]
    public async Task Workspace_ConnectsDefinitionsMapComponentsArenaBindingsAndLifecycle()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace, includeProvider: true);
        WritePuzzleDatabase(workspace);
        workspace.WriteFile("data/MapInfos.json", "[null,{\"id\":1,\"name\":\"Sala pesi\"}]");
        workspace.WriteFile(
            "data/Map001.json",
            """
            {
              "events":[
                null,
                {"id":1,"name":"Peso sud","note":"<FusionPuzzle:weights>\n<FusionPuzzleRole:source>\n<FusionPuzzleKey:south>","pages":[]},
                {"id":2,"name":"Altare","note":"<FusionPuzzle:weights>\n<FusionPuzzleRole:goal>","pages":[]},
                {"id":3,"name":"Estensione","note":"","pages":[{"list":[{"code":108,"parameters":["<FusionPuzzle:weights>"]},{"code":408,"parameters":["<FusionPuzzleRole:extension>\n<FusionPuzzleValue:2>"]}]}]},
                {"id":4,"name":"Salto","note":"<FusionPuzzle:weights>\n<FusionPuzzleRole:jumpModifier>\n<FusionPuzzleValue:12>","pages":[]}
              ]
            }
            """);
        workspace.WriteFile(
            "data/FusionArenas.json",
            """
            {
              "arenas":{
                "queenArena":{
                  "displayName":"Regina della Covata",
                  "mapIds":[1],
                  "providers":[{"instanceId":"weightPuzzle","type":"fusionPuzzle","puzzleId":"weights","scope":"arena","required":true}],
                  "puzzleBindings":[{"id":"deliveryWipe","providerInstanceId":"weightPuzzle","signal":"puzzle:deliveryCompleted","action":["encounter.activateWipeObjective"]}]
                }
              }
            }
            """);

        var document = await CreateService().LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));

        var puzzle = Assert.Single(document.Puzzles);
        Assert.True(puzzle.IsValid);
        Assert.Equal(4, puzzle.Components.Count);
        Assert.All(puzzle.Roles, role => Assert.True(role.IsSatisfied));
        Assert.Equal("Commento evento", puzzle.Components.Single(item => item.Role == "extension").Source);
        Assert.Equal("12", puzzle.Components.Single(item => item.Role == "jumpModifier").Value);
        var usage = Assert.Single(puzzle.ArenaUsages);
        Assert.Equal("weightPuzzle", usage.ProviderInstanceId);
        var binding = Assert.Single(usage.Bindings);
        Assert.Equal("puzzle:deliveryCompleted", binding.Signal);
        Assert.Contains("encounter.activateWipeObjective", binding.Actions);
        Assert.Contains(puzzle.Calls, call => call.Api == "FusionPuzzle.prepare(id, options)");
        Assert.Contains(puzzle.Calls, call => call.Api == "puzzle:deliveryCompleted");
        Assert.Contains(puzzle.Graph.Nodes, node => node.TechnicalId == "encounter.activateWipeObjective");
        Assert.Contains(puzzle.Graph.Edges, edge => edge.Label == "dispatch");
    }

    [Fact]
    public async Task Workspace_ReportsMissingProviderAndRequiredComponents()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace, includeProvider: false);
        WritePuzzleDatabase(workspace);
        workspace.WriteFile("data/MapInfos.json", "[]");

        var document = await CreateService().LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));

        var puzzle = Assert.Single(document.Puzzles);
        Assert.False(puzzle.IsValid);
        Assert.Contains(puzzle.Diagnostics, issue => issue.Code == "role.count-invalid");
        Assert.Contains(puzzle.Diagnostics, issue => issue.Code == "source.component-missing");
        Assert.Contains(puzzle.Diagnostics, issue => issue.Code == "provider.dependency-missing");
        Assert.Contains(puzzle.Diagnostics, issue => issue.Code == "startup.not-referenced");
        Assert.Contains(puzzle.Graph.Nodes, node => node.Kind == FusionPuzzleGraphNodeKind.Missing);
    }

    [Fact]
    public async Task Workspace_UsesProviderMetadataForCustomRolesSignalsAndCompletion()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            "js/plugins.js",
            """
            var $plugins = [
              {"name":"zenkaiDevPlugins/ZDP_FusionPuzzle","status":true,"description":"","parameters":{}},
              {"name":"zenkaiDevPlugins/ZDP_FusionPuzzle_SwitchCircuit","status":true,"description":"","parameters":{}}
            ];
            """);
        workspace.WriteFile(
            "data/FusionPuzzles.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "providers":{
                "switchCircuit":{
                  "displayName":"Circuito interruttori",
                  "plugin":"ZDP_FusionPuzzle_SwitchCircuit",
                  "roles":{
                    "lever":{"displayName":"Leva","interaction":"toggleLever"},
                    "door":{"displayName":"Porta","interaction":"openDoor"}
                  },
                  "signals":[
                    {"name":"puzzle:circuitPowered","role":"lever","phase":"execution"},
                    {"name":"puzzle:circuitCompleted","role":"door","phase":"completion"}
                  ],
                  "completion":{"role":"door","signal":"puzzle:circuitCompleted","description":"La porta si apre."}
                }
              },
              "puzzles":{
                "switchRoom":{
                  "definitionVersion":1,
                  "displayName":"Stanza interruttori",
                  "type":"switchCircuit",
                  "scope":"map",
                  "roles":{"lever":{"count":1},"door":{"count":1}},
                  "reset":{"when":"mapExit"}
                }
              }
            }
            """);
        workspace.WriteFile("data/MapInfos.json", "[null,{\"id\":1,\"name\":\"Circuito\"}]");
        workspace.WriteFile(
            "data/Map001.json",
            """
            {"events":[null,
              {"id":1,"name":"Leva","note":"<FusionPuzzle:switchRoom>\n<FusionPuzzleRole:lever>","pages":[]},
              {"id":2,"name":"Porta","note":"<FusionPuzzle:switchRoom>\n<FusionPuzzleRole:door>","pages":[]}
            ]}
            """);

        var document = await CreateService().LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));

        var puzzle = Assert.Single(document.Puzzles);
        Assert.True(puzzle.IsValid);
        Assert.All(puzzle.Roles, role => Assert.True(role.IsSatisfied));
        Assert.DoesNotContain(puzzle.Diagnostics, issue => issue.Code == "provider.metadata-missing");
        Assert.Contains(document.Plugins, plugin =>
            plugin.Id == "ZDP_FusionPuzzle_SwitchCircuit" && plugin.IsActive);
        Assert.Contains(puzzle.Dependencies, dependency =>
            dependency.Id == "ZDP_FusionPuzzle_SwitchCircuit" && dependency.IsAvailable);
        Assert.Contains(puzzle.Calls, call => call.Api == "toggleLever");
        Assert.Contains(puzzle.Calls, call => call.Api == "context.complete(payload, options)");
        Assert.Contains(puzzle.Graph.Nodes, node => node.TechnicalId == "puzzle:circuitCompleted");
        Assert.Contains(puzzle.Graph.Edges, edge =>
            edge.SourceNodeId.StartsWith("signal-", StringComparison.Ordinal) &&
            edge.TargetNodeId == "completed");
        Assert.Contains(puzzle.Settings, setting =>
            setting.Group == "reset" && setting.Name == "when" && setting.Value == "mapExit");
    }

    private static FusionPuzzleWorkspaceService CreateService()
    {
        var fileSystem = new FileSystemService();
        return new FusionPuzzleWorkspaceService(
            fileSystem,
            new RpgMakerPluginRegistryService(fileSystem));
    }

    private static ZiapProject CreateProject(string path) => new()
    {
        Id = "puzzle-test",
        Name = "Puzzle Test",
        ProjectType = KnownProjectTypes.RpgMakerMz,
        Path = path,
        IsZiapInitialized = true,
    };

    private static DocumentDescriptor CreateDescriptor(string path) => new()
    {
        Id = new DocumentId("puzzle-test:integration:fusion-puzzle"),
        DisplayName = "Fusion Puzzle",
        Kind = DocumentKind.ProjectIntegration,
        ResourceId = new Uri(FusionPuzzleIntegrationProvider.ResourceUri),
        SourcePath = Path.Combine(path, "data"),
    };

    private static void WritePlugins(TestWorkspace workspace, bool includeProvider)
    {
        var providerPlugins = includeProvider
            ? """
              ,{"name":"zenkaiDevPlugins/ZDP_FusionPuzzle_Movement","status":true,"description":"","parameters":{}}
              ,{"name":"zenkaiDevPlugins/ZDP_FusionPuzzle_HexellaWeight","status":true,"description":"","parameters":{}}
              ,{"name":"zenkaiDevPlugins/ZDP_FusionArena","status":true,"description":"","parameters":{}}
              """
            : string.Empty;
        workspace.WriteFile(
            "js/plugins.js",
            """
            var $plugins = [
              {"name":"zenkaiDevPlugins/ZDP_FusionPuzzle","status":true,"description":"","parameters":{}}
              __PROVIDERS__
            ];
            """.Replace("__PROVIDERS__", providerPlugins, StringComparison.Ordinal));
    }

    private static void WritePuzzleDatabase(TestWorkspace workspace) => workspace.WriteFile(
        "data/FusionPuzzles.json",
        """
        {
          "schemaVersion":1,
          "databaseVersion":"0.2.0",
          "providers":{
            "hexellaWeight":{
              "displayName":"Peso di Hexella",
              "plugin":"ZDP_FusionPuzzle_HexellaWeight",
              "dependencies":["ZDP_FusionPuzzle_Movement"],
              "requiresSourceBudgets":true,
              "sourceRole":"source",
              "roles":{
                "source":{"interaction":"beginCarry"},
                "goal":{"interaction":"deliver"},
                "extension":{"interaction":"extendCarry"},
                "jumpModifier":{"interaction":"setJumpModifier"}
              },
              "signals":[{"name":"puzzle:deliveryCompleted","role":"goal","phase":"completion"}],
              "completion":{"role":"goal","signal":"puzzle:completed","description":"Consegna completata."}
            }
          },
          "puzzles":{
            "weights":{
              "definitionVersion":1,
              "displayName":"Peso di Hexella",
              "type":"hexellaWeight",
              "scope":"arena",
              "budgetPolicy":"playerSteps",
              "repeatable":true,
              "roles":{
                "source":{"count":1},
                "goal":{"count":1},
                "extension":{"count":1},
                "jumpModifier":{"count":1}
              },
              "sources":{"south":{"budget":12}},
              "carry":{"disableDash":true,"defaultJumpDistance":0},
              "legacyBridge":{"enabled":true,"jumpVariableId":11}
            }
          }
        }
        """);
}
