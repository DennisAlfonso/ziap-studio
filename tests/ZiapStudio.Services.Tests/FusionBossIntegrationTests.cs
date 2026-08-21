using System.Text.Json.Nodes;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Bosses;
using ZiapStudio.Core.Models;
using ZiapStudio.Core.Preflight;
using ZiapStudio.Services;
using ZiapStudio.Services.Fusion.Bosses;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.Fusion.Preflight.Bosses;
using ZiapStudio.Services.Integrations;
using ZiapStudio.Services.Providers;

namespace ZiapStudio.Services.Tests;

public sealed class FusionBossIntegrationTests
{
    [Fact]
    public async Task ProjectIntegration_AddsBossWorkspaceWhenCorePluginIsActive()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile("data/System.json", "{\"gameTitle\":\"Test\"}");
        WritePlugins(workspace);
        var service = new ProjectProviderService(new FileSystemService());

        var roots = await service.BuildExplorerAsync(CreateProject(workspace.RootPath));

        var documentNode = roots.Single(node => node.Name == "System")
            .Children.Single(node => node.Name == "Plugin")
            .Children.Single(node => node.Name == "Fusion Boss Battle");
        Assert.Equal(ProjectExplorerNodeKind.Integration, documentNode.Kind);
        Assert.Equal("fusionboss://workspace/", documentNode.Document!.ResourceId.AbsoluteUri);
        Assert.Equal(
            Path.Combine(workspace.RootPath, "data"),
            documentNode.Path);
    }

    [Fact]
    public async Task Workspace_LoadsLinkedDatabasesAndValidatesMapAnchors()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);
        var service = CreateWorkspaceService();

        var document = await service.LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));

        Assert.True(document.IsValid);
        Assert.Empty(document.Diagnostics);
        Assert.Equal(1, document.GetRecordCount("bosses"));
        Assert.Equal(1, document.GetRecordCount("encounters"));
        Assert.Equal(1, document.GetRecordCount("arenas"));
        Assert.All(document.Databases, database => Assert.True(database.Exists));
    }

    [Fact]
    public async Task Workspace_ReportsBrokenCrossReferencesAndMissingAnchors()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);
        workspace.WriteFile(
            "data/FusionArenas.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "arenas":{
                "testArena":{
                  "encounterId":"missingEncounter",
                  "mapIds":[1],
                  "bindings":{"boss":{"entityId":"missingEntity"}},
                  "anchors":{"bossSpawn":{"count":2}},
                  "completionProfile":"missingCompletion"
                }
              },
              "completionProfiles":{"complete":{}}
            }
            """);

        var document = await CreateWorkspaceService().LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));

        Assert.Contains(document.Diagnostics, issue => issue.Code == "arena.encounter-missing");
        Assert.Contains(document.Diagnostics, issue => issue.Code == "arena.entity-missing");
        Assert.Contains(document.Diagnostics, issue => issue.Code == "arena.completion-profile-missing");
        Assert.Contains(document.Diagnostics, issue => issue.Code == "arena.anchor-count");
        Assert.False(document.IsValid);
    }

    [Fact]
    public async Task Workspace_KeepsInvalidEncounterSourceReadOnly()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);
        workspace.WriteFile("data/FusionEncounters.json", "{ invalid json");

        var document = await CreateWorkspaceService().LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));

        Assert.Null(document.EncounterSourceRoot);
        Assert.Null(document.EncounterSourceSnapshot);
        Assert.Contains(document.Diagnostics, issue => issue.Code == "database.json-invalid");
    }

    [Fact]
    public async Task Workspace_ProjectsPhaseGraphAndTimelineWithDynamicTiming()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);
        workspace.WriteFile(
            "data/FusionEncounters.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "encounters":{
                "testEncounter":{
                  "displayName":"Test Boss",
                  "boss":"testBoss",
                  "initialPhase":"intro",
                  "phases":{
                    "intro":{
                      "transitions":[{"when":["signal","engage"],"to":"combat"}]
                    },
                    "combat":{
                      "displayName":"Duello di prova",
                      "summary":"Verifica il ciclo semantico.",
                      "playerGoal":"Evitare l'impatto.",
                      "designerIntent":"Rendere leggibile la dipendenza dal bersaglio catturato.",
                      "mechanics":[["shield",{"role":"boss"}]],
                      "onEnter":[
                        ["cleanupDamageWindow"],
                        ["addAltarCharge",1],
                        ["combat.changeRoleHp",{"role":"boss","amount":-350}],
                        ["alphaAbsChangeRoleHp",{"role":"boss","amount":-100}],
                        ["completeWipeCycle"]
                      ],
                      "onExit":[["emit","combat:end"]],
                      "sequences":[{
                        "id":"attackCycle",
                        "displayName":"Ciclo di attacco",
                        "summary":"Cattura e colpisce la posizione del giocatore.",
                        "scope":"phase",
                        "steps":[
                          ["combat.captureTarget",{"key":"impact"}],
                          ["wait",60],
                          ["combat.cast",{"skillId":95,"casterRole":"boss","target":{"type":"captured","key":"impact"}}],
                          {"waitUntil":["roleMovementComplete","boss"],"timeoutFrames":180},
                          ["presentation.cue","impact"]
                        ]
                      }]
                    }
                  }
                }
              }
            }
            """);
        workspace.WriteFile(
            "data/FusionActionCatalog.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "actions":{
                "presentation.cue":{
                  "displayName":"Mostra il cue personalizzato",
                  "category":"Regia",
                  "icon":"!",
                  "descriptionTemplate":"Segnala {cue} al giocatore."
                }
              }
            }
            """);

        var document = await CreateWorkspaceService().LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));

        var encounter = Assert.Single(document.Encounters);
        Assert.Equal("Test Boss", encounter.DisplayName);
        Assert.Equal("combat", Assert.Single(encounter.Phases[0].Transitions).TargetPhaseId);
        var combat = Assert.Single(encounter.Phases, phase => phase.Id == "combat");
        Assert.Equal("Duello di prova", combat.DisplayName);
        Assert.Equal("Verifica il ciclo semantico.", combat.Summary);
        Assert.Equal("Evitare l'impatto.", combat.PlayerGoal);
        Assert.Equal("shield", Assert.Single(combat.Mechanics));
        Assert.Collection(
            combat.OnEnterSteps,
            step => Assert.Equal("Ripulisce la finestra di danno", step.Label),
            step =>
            {
                Assert.Equal("Aumenta la carica dell'altare", step.Label);
                Assert.Contains("1", step.Detail);
                Assert.Contains("meccanica:altarCharge", step.Writes);
            },
            step =>
            {
                Assert.Equal("Modifica gli HP di un ruolo", step.Label);
                Assert.Contains("-350", step.Detail);
                Assert.Contains("hp:boss", step.Writes);
            },
            step =>
            {
                Assert.Contains("Alpha ABS", step.Detail);
                Assert.Contains("hp:boss", step.Writes);
            },
            step => Assert.Equal("Completa il ciclo wipe", step.Label));
        Assert.Equal("Invia un segnale", Assert.Single(combat.OnExitSteps).Label);
        var sequence = Assert.Single(combat.Sequences);
        Assert.Equal("Ciclo di attacco", sequence.DisplayName);
        Assert.Equal("Cattura e colpisce la posizione del giocatore.", sequence.Summary);
        Assert.Equal(60, sequence.MinimumDurationFrames);
        Assert.True(sequence.HasDynamicTiming);
        Assert.Equal(FusionBossTimelineStepKind.Action, sequence.Steps[0].Kind);
        Assert.Equal("Memorizza un bersaglio", sequence.Steps[0].Label);
        Assert.Equal("combat.captureTarget", sequence.Steps[0].TechnicalId);
        Assert.Contains("bersaglio:impact", sequence.Steps[0].Writes);
        Assert.Equal(60, sequence.Steps[1].DurationFrames);
        Assert.Equal(60, sequence.Steps[2].EarliestStartFrame);
        Assert.Equal("Esegue un attacco", sequence.Steps[2].Label);
        Assert.Contains("Skill #95", sequence.Steps[2].Detail);
        Assert.Contains("skillId=95", sequence.Steps[2].TechnicalDetail);
        Assert.Contains("bersaglio:impact", sequence.Steps[2].Reads);
        var attack = Assert.IsType<FusionBossAttackGeometry>(sequence.Steps[2].AttackGeometry);
        Assert.Equal(95, attack.SkillId);
        Assert.Equal("Impatto stordente", attack.SkillName);
        Assert.Equal(FusionBossAttackGeometryKind.InstantCircle, attack.Kind);
        Assert.Equal(1, attack.RadiusTiles);
        Assert.Equal(-0.5, attack.TelegraphCenterOffsetYTiles);
        Assert.Equal(-0.5, attack.RuntimeCenterOffsetYTiles);
        Assert.Equal(60, attack.TelegraphDurationFrames);
        Assert.Equal(60, attack.ExecutionDelayFrames);
        Assert.Equal(1, attack.HitRepeatCount);
        Assert.Equal(1, attack.RepeatOnUseCount);
        Assert.Equal(
            FusionBossAttackGeometryComparison.ExactPrimaryCollider,
            attack.Comparison);
        var attackTarget = Assert.IsType<FusionBossAttackTarget>(
            sequence.Steps[2].AttackTarget);
        Assert.Equal("captured", attackTarget.TargetType);
        Assert.Equal("impact", attackTarget.TargetKey);
        Assert.True(attackTarget.IsDynamic);
        Assert.Equal(FusionBossTimelineStepKind.WaitUntil, sequence.Steps[3].Kind);
        Assert.Contains("completi il movimento", sequence.Steps[3].Detail);
        Assert.Equal(180, sequence.Steps[3].TimeoutFrames);
        Assert.False(sequence.Steps[4].IsStartExact);
        Assert.Equal("Mostra il cue personalizzato", sequence.Steps[4].Label);
        Assert.Equal("Regia", sequence.Steps[4].Category);
        Assert.Equal("Segnala impact al giocatore.", sequence.Steps[4].Detail);
    }

    [Fact]
    public async Task Workspace_ProjectsRuntimeAttackScheduleAndRoleDestinations()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);
        workspace.WriteFile(
            "data/FusionEncounters.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "encounters":{
                "testEncounter":{
                  "displayName":"Test Boss",
                  "boss":"testBoss",
                  "initialPhase":"combat",
                  "phases":{"combat":{"sequences":[{
                    "id":"attackCycle",
                    "steps":[
                      ["combat.captureTarget",{"key":"impact","target":{"type":"playerPosition"}}],
                      ["wait",60],
                      ["combat.moveTo",{"role":"boss","target":{"type":"captured","key":"impact"}}],
                      {"waitUntil":["roleMovementComplete","boss"],"timeoutFrames":180},
                      ["combat.cast",{"skillId":95,"casterRole":"boss","target":{"type":"captured","key":"impact"}}],
                      ["wait",90],
                      ["combat.moveTo",{"role":"boss","target":{"type":"anchor","anchor":"bossCenter"}}],
                      {"waitUntil":["roleMovementComplete","boss"],"timeoutFrames":180},
                      ["combat.captureTarget",{"key":"volleyTarget","target":{"type":"playerPosition"}}],
                      ["combat.cast",{
                        "mode":"projectileFromPoint",
                        "skillId":90,
                        "casterRole":"boss",
                        "origin":{"type":"role","role":"boss"},
                        "target":{"type":"captured","key":"volleyTarget"}
                      }],
                      ["combat.castVolley",{
                        "skillId":95,
                        "casterRole":"boss",
                        "targets":[
                          {"type":"anchor","anchor":"bossCenter","index":0},
                          {"type":"anchor","anchor":"bossCenter","index":1}
                        ]
                      }]
                    ]
                  }]}}
                }
              }
            }
            """);

        var document = await CreateWorkspaceService().LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));

        var sequence = Assert.Single(Assert.Single(document.Encounters).Phases[0].Sequences);
        var impact = Assert.IsType<FusionBossAttackTarget>(sequence.Steps[4].AttackTarget);
        Assert.Equal("captured", impact.OriginType);
        Assert.Equal("impact", impact.OriginKey);
        Assert.True(impact.IsOriginDynamic);

        var projectile = Assert.IsType<FusionBossAttackGeometry>(
            sequence.Steps[9].AttackGeometry);
        Assert.Equal(FusionBossAttackGeometryKind.ProjectileCorridor, projectile.Kind);
        Assert.Equal(10, projectile.RangeTiles);
        Assert.Equal(8, projectile.Speed);
        Assert.Equal(0, projectile.DirectionMode);
        Assert.Equal(60, projectile.ExecutionDelayFrames);
        Assert.Equal(4, projectile.RepeatOnUseCount);
        Assert.Equal(1, projectile.HitRepeatCount);
        Assert.Equal(90, projectile.RepeatDelayMilliseconds);
        Assert.Equal(FusionBossAttackGeometryComparison.Partial, projectile.Comparison);

        var volley = Assert.IsType<FusionBossAttackTarget>(sequence.Steps[9].AttackTarget);
        Assert.Equal("captured", volley.TargetType);
        Assert.Equal("volleyTarget", volley.TargetKey);
        Assert.Equal("anchor", volley.OriginType);
        Assert.Equal("bossCenter", volley.OriginAnchor);
        Assert.False(volley.IsOriginDynamic);

        var volleyTargets = sequence.Steps[10].AttackTargets;
        Assert.Equal(2, volleyTargets.Count);
        Assert.Equal(0, volleyTargets[0].TargetIndex);
        Assert.Equal(1, volleyTargets[1].TargetIndex);
        Assert.All(volleyTargets, target =>
        {
            Assert.Equal("anchor", target.TargetType);
            Assert.Equal("bossCenter", target.TargetAnchor);
        });
    }

    [Fact]
    public async Task Workspace_ProjectsShieldedOpeningHighlightsAtExpectedFrames()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);
        workspace.WriteFile(
            "data/FusionEncounters.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "encounters":{
                "testEncounter":{
                  "boss":"testBoss",
                  "initialPhase":"shielded",
                  "phases":{"shielded":{"sequences":[{
                    "id":"shieldedOpening",
                    "steps":[
                      ["wait",30],
                      ["presentation.cue","shieldedPoseActive"],
                      ["wait",300],
                      ["combat.captureTarget",{"key":"impact","target":{"type":"playerPosition"}}],
                      ["wait",60],
                      ["combat.castVolley",{"skillId":104,"targets":[
                        {"type":"captured","key":"impact"},
                        {"type":"point","x":25,"y":43},
                        {"type":"point","x":43,"y":38}
                      ]}],
                      ["wait",20],
                      ["combat.castVolley",{"skillId":105,"targets":[
                        {"type":"captured","key":"impact"},
                        {"type":"point","x":25,"y":43},
                        {"type":"point","x":43,"y":38}
                      ]}],
                      ["wait",330],
                      ["presentation.cue","shieldedPoseRelease"]
                    ]
                  }]}}
                }
              }
            }
            """);

        var document = await CreateWorkspaceService().LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));

        var sequence = Assert.Single(Assert.Single(document.Encounters).Phases[0].Sequences);
        Assert.Equal(740, sequence.MinimumDurationFrames);
        Assert.Equal(390, sequence.Steps[5].EarliestStartFrame);
        Assert.Equal(410, sequence.Steps[7].EarliestStartFrame);
        Assert.Equal(740, sequence.Steps[9].EarliestStartFrame);
        Assert.Equal(3, sequence.Steps[5].AttackTargets.Count);
        Assert.Equal(3, sequence.Steps[7].AttackTargets.Count);
        Assert.Equal(60, sequence.Steps[5].AttackGeometry?.ExecutionDelayFrames);
        Assert.Equal(60, sequence.Steps[7].AttackGeometry?.ExecutionDelayFrames);
    }

    [Fact]
    public async Task Workspace_ProjectsArenaSceneFromMapAndTilesetData()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);

        var document = await CreateWorkspaceService().LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));

        Assert.Equal(workspace.RootPath, document.ProjectPath);
        var arena = Assert.Single(document.Arenas);
        Assert.Equal("testArena", arena.Id);
        Assert.Equal("Training Arena", arena.DisplayName);
        Assert.Equal("testEncounter", arena.EncounterId);

        var map = Assert.Single(arena.Maps);
        Assert.Equal(1, map.MapId);
        Assert.Equal("Test Arena Map", map.DisplayName);
        Assert.Equal(2, map.Width);
        Assert.Equal(2, map.Height);
        Assert.Equal(1, map.TilesetId);
        Assert.Equal("Test Tileset", map.TilesetName);
        Assert.Equal(9, map.TilesetNames.Count);
        Assert.Equal(24, map.MapData.Count);
        Assert.Equal([0, 16, 32], map.TilesetFlags);

        var anchor = Assert.Single(map.Markers, marker =>
            marker.Kind == FusionBossMapMarkerKind.Anchor);
        Assert.Equal("bossSpawn", anchor.Id);
        Assert.Equal(3, anchor.X);
        Assert.Equal(4, anchor.Y);

        var role = Assert.Single(map.Markers, marker =>
            marker.Kind == FusionBossMapMarkerKind.Role);
        Assert.Equal("boss", role.Id);
        Assert.Equal(5, role.X);
        Assert.Equal(6, role.Y);
    }

    [Fact]
    public async Task Authoring_RoundTripsUnknownFieldsAndSupportsUndoRedo()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);
        workspace.WriteFile(
            "data/FusionEncounters.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "futureRoot":{"keep":true},
              "encounters":{
                "testEncounter":{
                  "boss":"testBoss",
                  "initialPhase":"combat",
                  "futureEncounter":{"token":"unchanged"},
                  "phases":{
                    "combat":{
                      "futurePhase":[1,2,3],
                      "sequences":[{
                        "id":"main",
                        "futureSequence":{"enabled":true},
                        "steps":[["wait",30,{"futureStep":"keep"}]]
                      }]
                    }
                  }
                }
              }
            }
            """);
        var document = await CreateWorkspaceService().LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));
        var session = new FusionBossEditSession(document);
        var step = Assert.IsType<JsonArray>(session.GetValue(
            "/encounters/testEncounter/phases/combat/sequences/0/steps/0"));
        step[1] = 45;

        Assert.True(session.SetValues(
        [
            ("/encounters/testEncounter/phases/combat/summary", JsonValue.Create("Nuovo riassunto")),
            ("/encounters/testEncounter/phases/combat/sequences/0/steps/0", step),
        ]));
        Assert.True(session.IsDirty);
        Assert.True(session.Undo());
        Assert.False(session.IsDirty);
        Assert.True(session.Redo());

        var result = await CreateAuthoringService().SaveAsync(session);

        Assert.Equal(DocumentSaveStatus.Saved, result.Status);
        Assert.False(session.IsDirty);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            workspace.RootPath,
            "data",
            "FusionEncounters.json")))!;
        Assert.True(saved["futureRoot"]!["keep"]!.GetValue<bool>());
        Assert.Equal(
            "unchanged",
            saved["encounters"]!["testEncounter"]!["futureEncounter"]!["token"]!.GetValue<string>());
        Assert.Equal(
            3,
            saved["encounters"]!["testEncounter"]!["phases"]!["combat"]!["futurePhase"]!.AsArray().Count);
        Assert.True(saved["encounters"]!["testEncounter"]!["phases"]!["combat"]!["sequences"]![0]!["futureSequence"]!["enabled"]!.GetValue<bool>());
        var savedStep = saved["encounters"]!["testEncounter"]!["phases"]!["combat"]!["sequences"]![0]!["steps"]![0]!.AsArray();
        Assert.Equal(45, savedStep[1]!.GetValue<int>());
        Assert.Equal("keep", savedStep[2]!["futureStep"]!.GetValue<string>());
    }

    [Fact]
    public async Task Authoring_BlocksInvalidStepBeforeWriting()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);
        workspace.WriteFile(
            "data/FusionEncounters.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "encounters":{
                "testEncounter":{
                  "boss":"testBoss",
                  "initialPhase":"combat",
                  "phases":{"combat":{"sequences":[{"id":"main","steps":[["wait",30]]}]}}
                }
              }
            }
            """);
        var sourcePath = Path.Combine(workspace.RootPath, "data", "FusionEncounters.json");
        var original = await File.ReadAllTextAsync(sourcePath);
        var document = await CreateWorkspaceService().LoadAsync(
            CreateProject(workspace.RootPath),
            CreateDescriptor(workspace.RootPath));
        var session = new FusionBossEditSession(document);
        session.SetValue(
            "/encounters/testEncounter/phases/combat/sequences/0/steps/0",
            JsonNode.Parse("[\"wait\",-1]"));

        var result = await CreateAuthoringService().SaveAsync(session);

        Assert.Equal(DocumentSaveStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Validation.Issues, issue => issue.Code == "invalid-wait");
        Assert.Equal(original, await File.ReadAllTextAsync(sourcePath));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public async Task Preflight_MapsBossDiagnosticsToNavigableIssues()
    {
        using var workspace = new TestWorkspace();
        WritePlugins(workspace);
        WriteValidWorkspace(workspace);
        workspace.WriteFile(
            "data/FusionEncounters.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "encounters":{
                "testEncounter":{
                  "boss":"missingBoss",
                  "initialPhase":"intro",
                  "phases":{"intro":{}}
                }
              }
            }
            """);
        var fileSystem = new FileSystemService();
        var registry = new RpgMakerPluginRegistryService(fileSystem);
        var service = new FusionBossWorkspaceService(fileSystem, registry);
        var provider = new FusionBossPreflightProvider(
            service,
            new FusionBossIntegrationProvider(registry));

        var issues = await provider.ScanAsync(CreateProject(workspace.RootPath));

        var issue = Assert.Single(issues, issue =>
            issue.RuleId == "fusion-boss.encounter.boss-missing");
        Assert.Equal("FusionBoss", issue.Scope);
        Assert.Equal(PreflightSeverity.Error, issue.Severity);
        Assert.StartsWith("fusionboss://workspace/", issue.NavigationTarget!.AbsoluteUri);
    }

    private static FusionBossWorkspaceService CreateWorkspaceService()
    {
        var fileSystem = new FileSystemService();
        return new FusionBossWorkspaceService(
            fileSystem,
            new RpgMakerPluginRegistryService(fileSystem));
    }

    private static FusionBossAuthoringService CreateAuthoringService()
    {
        var fileSystem = new FileSystemService();
        var snapshots = new DocumentSnapshotService(fileSystem);
        return new FusionBossAuthoringService(
            new ExternalModificationDetector(fileSystem, snapshots),
            new AtomicJsonFileWriter(fileSystem),
            snapshots);
    }

    private static DocumentDescriptor CreateDescriptor(string projectPath) => new()
    {
        Id = new DocumentId("test:integration:fusion-boss"),
        DisplayName = "Fusion Boss Battle",
        Kind = DocumentKind.ProjectIntegration,
        ResourceId = new Uri(FusionBossIntegrationProvider.ResourceUri),
        SourcePath = Path.Combine(projectPath, "data"),
    };

    private static void WritePlugins(TestWorkspace workspace) => workspace.WriteFile(
        "js/plugins.js",
        """
        var $plugins = [
          {"name":"zenkaiDevPlugins/ZDP_FusionCombat","status":true,"parameters":{}},
          {"name":"zenkaiDevPlugins/ZDP_FusionEncounter","status":true,"parameters":{}},
          {"name":"zenkaiDevPlugins/ZDP_FusionArena","status":true,"parameters":{}},
          {"name":"zenkaiDevPlugins/FHD_EnemyAttackTelegraph","status":true,"parameters":{"BaseWarningFrames":"60","MinimumDuration":"45"}}
        ];
        """);

    private static void WriteValidWorkspace(TestWorkspace workspace)
    {
        workspace.WriteFile(
            "data/FusionCombat.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "scalingProfiles":{"story":{}},
              "rewardRules":{},
              "enemies":{"bossEntity":{}},
              "bosses":{"testBoss":{"enemy":"bossEntity","scaling":{"profile":"story"}}}
            }
            """);
        workspace.WriteFile(
            "data/FusionEncounters.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "encounters":{
                "testEncounter":{
                  "boss":"testBoss",
                  "initialPhase":"intro",
                  "phases":{"intro":{}}
                }
              }
            }
            """);
        workspace.WriteFile(
            "data/FusionArenas.json",
            """
            {
              "schemaVersion":1,
              "databaseVersion":"1.0.0",
              "arenas":{
                "testArena":{
                  "displayName":"Training Arena",
                  "encounterId":"testEncounter",
                  "mapIds":[1],
                  "bindings":{"boss":{"entityId":"bossEntity"}},
                  "anchors":{"bossSpawn":{"count":1}},
                  "completionProfile":"complete"
                }
              },
              "completionProfiles":{"complete":{}}
            }
            """);
        workspace.WriteFile(
            "data/FusionPuzzles.json",
            """
            {"schemaVersion":1,"databaseVersion":"1.0.0","puzzles":{}}
            """);
        workspace.WriteFile(
            "data/Skills.json",
            """
            [{
              "id":90,
              "name":"Proiettile Perforrante",
              "note":"<ABS>\nrepeatOnUse:4\nrepeatDelay:90\nradius:1\nrange:10\nnoContact:1\nspeed:8\n</ABS>"
            },{
              "id":95,
              "name":"Impatto stordente",
              "note":"<ABS>\nradius:1\nrange:0\ndirection:0\nnoContact:1\nspeed:0\nimpulseJump:1\nimpulse:1\n</ABS>"
            },{
              "id":104,
              "name":"Fulmini Umbra",
              "note":"<ABS>\nradius:3\nrange:0\ndirection:0\nnoContact:1\nspeed:0\n</ABS>"
            },{
              "id":105,
              "name":"Scosse Umbra",
              "note":"<ABS>\nradius:3\nrange:0\ndirection:0\nnoContact:1\nspeed:0\n</ABS>"
            }]
            """);
        workspace.WriteFile(
            "data/MapInfos.json",
            """
            [null,{"id":1,"name":"Test Arena Map"}]
            """);
        workspace.WriteFile(
            "data/Tilesets.json",
            """
            [null,{
              "id":1,
              "name":"Test Tileset",
              "tilesetNames":["A1","A2","A3","A4","A5","B","C","D","E"],
              "flags":[0,16,32]
            }]
            """);
        workspace.WriteFile(
            "data/Map001.json",
            """
            {
              "width":2,
              "height":2,
              "tilesetId":1,
              "scrollType":0,
              "data":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
              "parallaxName":"",
              "parallaxLoopX":false,
              "parallaxLoopY":false,
              "parallaxSx":0,
              "parallaxSy":0,
              "events":[
                null,
                {"id":1,"name":"Boss Spawn","note":"<FusionArena:testArena>\n<FusionAnchor:bossSpawn>","x":3,"y":4},
                {"id":2,"name":"Boss Role","note":"<FusionArena:testArena>\n<FusionRole:boss>","x":5,"y":6}
              ]
            }
            """);
    }

    private static ZiapProject CreateProject(string path) => new()
    {
        Id = "test",
        Name = "Test",
        ProjectType = KnownProjectTypes.RpgMakerMz,
        Path = path,
        IsZiapInitialized = true,
    };
}
