using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Fusion.Bosses;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Integrations;

namespace ZiapStudio.Services.Fusion.Bosses;

public sealed partial class FusionBossWorkspaceService
{
    private static readonly DatabaseSpec[] DatabaseSpecs =
    [
        new(
            FusionBossDatabaseKind.Combat,
            "Combat",
            "FusionCombat.json",
            FusionBossIntegrationProvider.CombatPluginName,
            ["scalingProfiles", "rewardRules", "enemies", "bosses"]),
        new(
            FusionBossDatabaseKind.Encounters,
            "Encounters",
            "FusionEncounters.json",
            FusionBossIntegrationProvider.EncounterPluginName,
            ["encounters"]),
        new(
            FusionBossDatabaseKind.Arenas,
            "Arenas",
            "FusionArenas.json",
            FusionBossIntegrationProvider.ArenaPluginName,
            ["arenas", "completionProfiles"]),
        new(
            FusionBossDatabaseKind.Puzzles,
            "Puzzles",
            "FusionPuzzles.json",
            FusionBossIntegrationProvider.PuzzlePluginName,
            ["puzzles"]),
    ];

    private static readonly PluginSpec[] PluginSpecs =
    [
        new(FusionBossIntegrationProvider.CombatPluginName, "Combat", true),
        new(FusionBossIntegrationProvider.EncounterPluginName, "Encounter", true),
        new(FusionBossIntegrationProvider.ArenaPluginName, "Arena", true),
        new(FusionBossIntegrationProvider.PuzzlePluginName, "Puzzle", false),
        new("ZDP_FusionEncounter_Movement", "Movement", false),
        new("ZDP_FusionEncounter_AlphaABS", "Alpha ABS", false),
        new("ZDP_FusionEncounter_Presentation", "Presentation", false),
        new(FusionBossIntegrationProvider.TelegraphPluginName, "Attack Telegraph", false),
    ];

    private readonly FileSystemService _fileSystem;
    private readonly RpgMakerPluginRegistryService _pluginRegistry;

    public FusionBossWorkspaceService(
        FileSystemService fileSystem,
        RpgMakerPluginRegistryService pluginRegistry)
    {
        _fileSystem = fileSystem;
        _pluginRegistry = pluginRegistry;
    }

    public async Task<FusionBossWorkspaceDocument> LoadAsync(
        ZiapProject project,
        DocumentDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(descriptor);

        var registrations = await _pluginRegistry.LoadAsync(project, cancellationToken);
        var plugins = PluginSpecs.Select(spec => new FusionBossPluginStatus
        {
            Id = spec.Id,
            DisplayName = spec.DisplayName,
            IsRequired = spec.IsRequired,
            IsActive = IsPluginActive(registrations, spec.Id),
        }).ToArray();
        var diagnostics = new List<FusionBossDiagnostic>();
        foreach (var plugin in plugins.Where(plugin => plugin.IsRequired && !plugin.IsActive))
        {
            diagnostics.Add(Error(
                "plugin.required-inactive",
                $"Il plugin richiesto {plugin.Id} non è attivo.",
                details: $"La capability Boss Battle richiede il nucleo {plugin.DisplayName}."));
        }

        var parsedDatabases = new Dictionary<FusionBossDatabaseKind, ParsedDatabase>();
        var summaries = new List<FusionBossDatabaseSummary>();
        foreach (var spec in DatabaseSpecs)
        {
            var pluginIsActive = IsPluginActive(registrations, spec.PluginId);
            var result = await LoadDatabaseAsync(
                project,
                spec,
                pluginIsActive,
                diagnostics,
                cancellationToken);
            summaries.Add(result.Summary);
            if (result.Parsed is not null)
            {
                parsedDatabases[spec.Kind] = result.Parsed;
            }
        }

        await ValidateReferencesAsync(
            project,
            parsedDatabases,
            plugins,
            diagnostics,
            cancellationToken);
        var attackGeometries = await BuildAttackGeometriesAsync(
            project,
            registrations,
            cancellationToken);
        var attackGeometriesBySkill = attackGeometries.ToDictionary(
            geometry => geometry.SkillId);
        var actionCatalog = await FusionBossActionCatalog.LoadAsync(
            _fileSystem,
            project.Path,
            cancellationToken);
        var encounterDefinitions = BuildEncounterDefinitions(
            Records(parsedDatabases, FusionBossDatabaseKind.Encounters, "encounters"),
            attackGeometriesBySkill,
            actionCatalog);
        var arenaDefinitions = await BuildArenaDefinitionsAsync(
            project,
            Records(parsedDatabases, FusionBossDatabaseKind.Arenas, "arenas"),
            cancellationToken);
        return new FusionBossWorkspaceDocument
        {
            Descriptor = descriptor,
            ProjectPath = project.Path,
            Plugins = plugins,
            Databases = summaries,
            Encounters = encounterDefinitions,
            Arenas = arenaDefinitions,
            AttackGeometries = attackGeometries,
            Diagnostics = diagnostics
                .OrderByDescending(diagnostic => diagnostic.Severity)
                .ThenBy(diagnostic => diagnostic.Database)
                .ThenBy(diagnostic => diagnostic.RecordId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    private async Task<IReadOnlyList<FusionBossArenaDefinition>> BuildArenaDefinitionsAsync(
        ZiapProject project,
        IReadOnlyDictionary<string, JsonElement> arenas,
        CancellationToken cancellationToken)
    {
        var tilesets = await LoadIndexedArrayAsync(
            Path.Combine(project.Path, "data", "Tilesets.json"),
            cancellationToken);
        var mapInfos = await LoadIndexedArrayAsync(
            Path.Combine(project.Path, "data", "MapInfos.json"),
            cancellationToken);
        var result = new List<FusionBossArenaDefinition>();
        foreach (var (arenaId, arena) in arenas)
        {
            var maps = new List<FusionBossMapScene>();
            if (arena.TryGetProperty("mapIds", out var mapIds) &&
                mapIds.ValueKind == JsonValueKind.Array)
            {
                foreach (var mapIdElement in mapIds.EnumerateArray())
                {
                    if (!mapIdElement.TryGetInt32(out var mapId) || mapId <= 0)
                    {
                        continue;
                    }
                    var scene = await LoadMapSceneAsync(
                        project,
                        arenaId,
                        mapId,
                        tilesets,
                        mapInfos,
                        cancellationToken);
                    if (scene is not null)
                    {
                        maps.Add(scene);
                    }
                }
            }
            result.Add(new FusionBossArenaDefinition
            {
                Id = arenaId,
                DisplayName = ReadString(arena, "displayName") ?? arenaId,
                EncounterId = ReadString(arena, "encounterId") ?? string.Empty,
                Maps = maps,
            });
        }
        return result
            .OrderBy(arena => arena.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private async Task<FusionBossMapScene?> LoadMapSceneAsync(
        ZiapProject project,
        string arenaId,
        int mapId,
        IReadOnlyDictionary<int, JsonElement> tilesets,
        IReadOnlyDictionary<int, JsonElement> mapInfos,
        CancellationToken cancellationToken)
    {
        var mapPath = Path.Combine(project.Path, "data", $"Map{mapId:000}.json");
        if (!_fileSystem.FileExists(mapPath))
        {
            return null;
        }
        try
        {
            using var mapDocument = JsonDocument.Parse(
                await _fileSystem.ReadAllBytesAsync(mapPath, cancellationToken));
            var map = mapDocument.RootElement;
            if (map.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            var tilesetId = ReadInt(map, "tilesetId") ?? 0;
            tilesets.TryGetValue(tilesetId, out var tileset);
            mapInfos.TryGetValue(mapId, out var mapInfo);
            return new FusionBossMapScene
            {
                MapId = mapId,
                DisplayName = ReadString(mapInfo, "name") ??
                    ReadString(map, "displayName") ??
                    $"Map {mapId:000}",
                SourcePath = mapPath,
                Width = ReadInt(map, "width") ?? 0,
                Height = ReadInt(map, "height") ?? 0,
                ScrollType = ReadInt(map, "scrollType") ?? 0,
                TilesetId = tilesetId,
                TilesetName = ReadString(tileset, "name") ?? $"Tileset {tilesetId}",
                TilesetNames = ReadStringArray(tileset, "tilesetNames"),
                TilesetFlags = ReadIntArray(tileset, "flags"),
                MapData = ReadIntArray(map, "data"),
                ParallaxName = ReadString(map, "parallaxName") ?? string.Empty,
                ParallaxLoopX = ReadBoolean(map, "parallaxLoopX"),
                ParallaxLoopY = ReadBoolean(map, "parallaxLoopY"),
                ParallaxSx = ReadDouble(map, "parallaxSx"),
                ParallaxSy = ReadDouble(map, "parallaxSy"),
                Markers = ReadMapMarkers(map, arenaId),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<int, JsonElement>> LoadIndexedArrayAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(path))
        {
            return new Dictionary<int, JsonElement>();
        }
        try
        {
            using var document = JsonDocument.Parse(
                await _fileSystem.ReadAllBytesAsync(path, cancellationToken));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new Dictionary<int, JsonElement>();
            }
            var result = new Dictionary<int, JsonElement>();
            var index = 0;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var id = ReadInt(item, "id") ?? index;
                    if (id > 0)
                    {
                        result[id] = item.Clone();
                    }
                }
                index++;
            }
            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<int, JsonElement>();
        }
    }

    private static IReadOnlyList<FusionBossMapMarker> ReadMapMarkers(
        JsonElement map,
        string arenaId)
    {
        var result = new List<FusionBossMapMarker>();
        if (!map.TryGetProperty("events", out var events) ||
            events.ValueKind != JsonValueKind.Array)
        {
            return result;
        }
        foreach (var mapEvent in events.EnumerateArray())
        {
            if (mapEvent.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            var note = ReadString(mapEvent, "note");
            if (string.IsNullOrWhiteSpace(note) || !ArenaTagRegex().Matches(note)
                .Any(match => match.Groups[1].Value.Trim().Equals(
                    arenaId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var eventId = ReadInt(mapEvent, "id") ?? 0;
            var eventName = ReadString(mapEvent, "name") ?? $"Event {eventId}";
            var x = ReadDouble(mapEvent, "x");
            var y = ReadDouble(mapEvent, "y");
            foreach (Match match in AnchorTagRegex().Matches(note))
            {
                result.Add(new FusionBossMapMarker
                {
                    Kind = FusionBossMapMarkerKind.Anchor,
                    Id = match.Groups[1].Value.Trim(),
                    EventId = eventId,
                    EventName = eventName,
                    X = x,
                    Y = y,
                });
            }
            foreach (Match match in RoleTagRegex().Matches(note))
            {
                result.Add(new FusionBossMapMarker
                {
                    Kind = FusionBossMapMarkerKind.Role,
                    Id = match.Groups[1].Value.Trim(),
                    EventId = eventId,
                    EventName = eventName,
                    X = x,
                    Y = y,
                });
            }
        }
        return result
            .OrderBy(marker => marker.Kind)
            .ThenBy(marker => marker.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.EventId)
            .ToArray();
    }

    private async Task<IReadOnlyList<FusionBossAttackGeometry>> BuildAttackGeometriesAsync(
        ZiapProject project,
        IReadOnlyList<RpgMakerPluginRegistration> registrations,
        CancellationToken cancellationToken)
    {
        var skills = await LoadIndexedArrayAsync(
            Path.Combine(project.Path, "data", "Skills.json"),
            cancellationToken);
        var telegraph = registrations.FirstOrDefault(registration =>
            registration.IsActive && RpgMakerPluginRegistryService.PluginNameEquals(
                registration.Name,
                FusionBossIntegrationProvider.TelegraphPluginName));
        var telegraphEnabled = telegraph is not null;
        var baseWarningFrames = ReadPluginInteger(telegraph, "BaseWarningFrames", 60, 0);
        var minimumDuration = ReadPluginInteger(telegraph, "MinimumDuration", 45, 1);
        var result = new List<FusionBossAttackGeometry>();
        foreach (var (skillId, skill) in skills.OrderBy(pair => pair.Key))
        {
            var note = ReadString(skill, "note") ?? string.Empty;
            var parameters = ReadAbsSkillParameters(note);
            if (parameters.Count == 0)
            {
                continue;
            }

            var radius = Math.Max(0.5, ReadAbsDouble(parameters, "radius", 1));
            var range = Math.Max(0, ReadAbsDouble(parameters, "range", 1));
            var speed = Math.Max(0, ReadAbsDouble(parameters, "speed", 0));
            var direction = ReadAbsInteger(parameters, "direction", 0);
            var castingFrames = Math.Max(
                0,
                (int)Math.Ceiling(ReadAbsDouble(parameters, "castingTime", 0) * 60));
            var actionStartDelay = Math.Max(
                0,
                (int)Math.Ceiling(ReadAbsDouble(parameters, "actionStartDelay", 0)));
            var isProjectile = speed > 0;
            var skillTelegraphEnabled = telegraphEnabled && !note.Contains(
                "<fhdNoTelegraph>",
                StringComparison.OrdinalIgnoreCase);
            var executionDelayFrames = skillTelegraphEnabled
                ? baseWarningFrames + castingFrames + actionStartDelay
                : 0;
            var extraHurtboxes = ReadExtraHurtboxes(parameters);
            var comparison = !skillTelegraphEnabled || extraHurtboxes.Count > 0 || isProjectile
                ? FusionBossAttackGeometryComparison.Partial
                : FusionBossAttackGeometryComparison.ExactPrimaryCollider;
            var comparisonText = !skillTelegraphEnabled
                ? telegraphEnabled
                    ? "Il telegraph è disabilitato per questa skill."
                    : "Il plugin FHD_EnemyAttackTelegraph non è attivo."
                : isProjectile
                    ? "Il corridoio usa il diametro del collider; la hitbox runtime è un cerchio mobile."
                : extraHurtboxes.Count > 0
                    ? "Il telegraph coincide con il collider primario, ma non mostra le hurtbox aggiuntive."
                    : "Telegraph e collider primario usano la stessa geometria.";
            var limitationText = isProjectile
                ? "La traiettoria mostra il corridoio annunciato; ostacoli, collisioni e homing restano dipendenti dal runtime."
                : "Il contatto effettivo dipende anche dalla hitbox del bersaglio, non inclusa nel raggio visuale.";

            result.Add(new FusionBossAttackGeometry
            {
                SkillId = skillId,
                SkillName = ReadString(skill, "name") ?? $"Skill {skillId}",
                Kind = isProjectile
                    ? FusionBossAttackGeometryKind.ProjectileCorridor
                    : FusionBossAttackGeometryKind.InstantCircle,
                RadiusTiles = radius,
                RangeTiles = range,
                Speed = speed,
                DirectionMode = direction,
                ProjectileColliderRadiusPixels = Math.Max(
                    1,
                    ReadAbsDouble(parameters, "colliderRadius", 8)),
                TelegraphCenterOffsetYTiles = isProjectile ? 0 : -0.5,
                RuntimeCenterOffsetYTiles = isProjectile ? 0 : -0.5,
                TelegraphEnabled = skillTelegraphEnabled,
                BaseWarningFrames = skillTelegraphEnabled ? baseWarningFrames : 0,
                CastingFrames = castingFrames,
                ActionStartDelayFrames = actionStartDelay,
                TelegraphDurationFrames = skillTelegraphEnabled
                    ? Math.Max(minimumDuration, executionDelayFrames)
                    : 0,
                ExecutionDelayFrames = executionDelayFrames,
                HitRepeatCount = Math.Max(1, ReadAbsInteger(parameters, "repeat", 1)),
                RepeatOnUseCount = Math.Max(
                    1,
                    ReadAbsInteger(parameters, "repeatOnUse", 1)),
                RepeatDelayMilliseconds = Math.Max(
                    0,
                    ReadAbsInteger(parameters, "repeatDelay", 120)),
                ExtraHurtboxes = extraHurtboxes,
                Comparison = comparison,
                ComparisonText = comparisonText,
                LimitationText = limitationText,
            });
        }
        return result;
    }

    private static int ReadPluginInteger(
        RpgMakerPluginRegistration? registration,
        string key,
        int fallback,
        int minimum)
    {
        if (registration?.Parameters.TryGetValue(key, out var raw) == true &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return Math.Max(minimum, value);
        }
        return Math.Max(minimum, fallback);
    }

    private static IReadOnlyDictionary<string, string> ReadAbsSkillParameters(string note)
    {
        var match = AbsBlockRegex().Match(note);
        if (!match.Success)
        {
            return new Dictionary<string, string>();
        }
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in match.Groups[1].Value.Split(['\r', '\n']))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }
            result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    private static double ReadAbsDouble(
        IReadOnlyDictionary<string, string> parameters,
        string key,
        double fallback) =>
        parameters.TryGetValue(key, out var raw) &&
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static int ReadAbsInteger(
        IReadOnlyDictionary<string, string> parameters,
        string key,
        int fallback) =>
        (int)Math.Round(ReadAbsDouble(parameters, key, fallback));

    private static IReadOnlyList<FusionBossAttackCollider> ReadExtraHurtboxes(
        IReadOnlyDictionary<string, string> parameters)
    {
        var result = new List<FusionBossAttackCollider>();
        foreach (var key in new[]
        {
            "extraHurtbox",
            "extraHurtbox2",
            "extraHurtbox3",
            "extraHurtbox4",
        })
        {
            if (!parameters.TryGetValue(key, out var raw))
            {
                continue;
            }
            var values = raw.Split(',').Select(value => value.Trim()).ToArray();
            if (values.Length < 4 ||
                !TryParseInvariant(values[1], out var offsetX) ||
                !TryParseInvariant(values[2], out var offsetY) ||
                !TryParseInvariant(values[3], out var size))
            {
                continue;
            }
            var isCircle = values[0].Equals("c", StringComparison.OrdinalIgnoreCase) ||
                values[0].Equals("circle", StringComparison.OrdinalIgnoreCase);
            if (!isCircle &&
                !values[0].Equals("b", StringComparison.OrdinalIgnoreCase) &&
                !values[0].Equals("box", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var height = size;
            if (!isCircle && (values.Length < 5 ||
                !TryParseInvariant(values[4], out height)))
            {
                continue;
            }
            result.Add(new FusionBossAttackCollider
            {
                Kind = isCircle
                    ? FusionBossAttackColliderKind.Circle
                    : FusionBossAttackColliderKind.Box,
                OffsetXTiles = offsetX,
                OffsetYTiles = offsetY,
                RadiusTiles = isCircle ? size : 0,
                WidthTiles = isCircle ? 0 : size,
                HeightTiles = isCircle ? 0 : height,
            });
        }
        return result;
    }

    private static bool TryParseInvariant(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static IReadOnlyList<FusionBossEncounterDefinition> BuildEncounterDefinitions(
        IReadOnlyDictionary<string, JsonElement> encounters,
        IReadOnlyDictionary<int, FusionBossAttackGeometry> attackGeometries,
        FusionBossActionCatalog actionCatalog) => encounters
        .Select(pair => BuildEncounterDefinition(pair.Key, pair.Value, attackGeometries, actionCatalog))
        .OrderBy(encounter => encounter.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    private static FusionBossEncounterDefinition BuildEncounterDefinition(
        string encounterId,
        JsonElement encounter,
        IReadOnlyDictionary<int, FusionBossAttackGeometry> attackGeometries,
        FusionBossActionCatalog actionCatalog)
    {
        var initialPhaseId = ReadString(encounter, "initialPhase") ?? string.Empty;
        var phases = new List<FusionBossPhaseDefinition>();
        if (TryGetObject(encounter, "phases", out var phaseDefinitions))
        {
            foreach (var phase in phaseDefinitions.EnumerateObject())
            {
                phases.Add(BuildPhaseDefinition(
                    phase.Name,
                    phase.Value,
                    initialPhaseId,
                    attackGeometries,
                    actionCatalog));
            }
        }
        return new FusionBossEncounterDefinition
        {
            Id = encounterId,
            DisplayName = ReadString(encounter, "displayName") ?? encounterId,
            BossId = ReadString(encounter, "boss") ?? string.Empty,
            InitialPhaseId = initialPhaseId,
            Phases = phases,
        };
    }

    private static FusionBossPhaseDefinition BuildPhaseDefinition(
        string phaseId,
        JsonElement phase,
        string initialPhaseId,
        IReadOnlyDictionary<int, FusionBossAttackGeometry> attackGeometries,
        FusionBossActionCatalog actionCatalog)
    {
        var mechanics = new List<string>();
        if (phase.TryGetProperty("mechanics", out var mechanicDefinitions) &&
            mechanicDefinitions.ValueKind == JsonValueKind.Array)
        {
            foreach (var mechanic in mechanicDefinitions.EnumerateArray())
            {
                mechanics.Add(mechanic.ValueKind == JsonValueKind.Array &&
                    mechanic.GetArrayLength() > 0 &&
                    mechanic.EnumerateArray().First().ValueKind == JsonValueKind.String
                        ? mechanic.EnumerateArray().First().GetString() ?? "mechanic"
                        : SummarizeJson(mechanic));
            }
        }

        var transitions = new List<FusionBossTransitionDefinition>();
        if (phase.TryGetProperty("transitions", out var transitionDefinitions) &&
            transitionDefinitions.ValueKind == JsonValueKind.Array)
        {
            foreach (var transition in transitionDefinitions.EnumerateArray())
            {
                transitions.Add(new FusionBossTransitionDefinition
                {
                    TargetPhaseId = ReadString(transition, "to") ?? string.Empty,
                    ConditionSummary = transition.TryGetProperty("when", out var condition)
                        ? SummarizeCondition(condition)
                        : "sempre",
                });
            }
        }

        var sequences = new List<FusionBossSequenceDefinition>();
        if (phase.TryGetProperty("sequences", out var sequenceDefinitions) &&
            sequenceDefinitions.ValueKind == JsonValueKind.Array)
        {
            foreach (var sequence in sequenceDefinitions.EnumerateArray())
            {
                if (sequence.ValueKind == JsonValueKind.Object)
                {
                    sequences.Add(BuildSequenceDefinition(sequence, attackGeometries, actionCatalog));
                }
            }
        }

        var onEnterSteps = BuildTimelineSteps(
            phase,
            "onEnter",
            attackGeometries,
            actionCatalog);
        var onExitSteps = BuildTimelineSteps(
            phase,
            "onExit",
            attackGeometries,
            actionCatalog);

        return new FusionBossPhaseDefinition
        {
            Id = phaseId,
            DisplayName = ReadString(phase, "displayName") ?? phaseId,
            Summary = ReadString(phase, "summary") ?? string.Empty,
            PlayerGoal = ReadString(phase, "playerGoal") ?? string.Empty,
            DesignerIntent = ReadString(phase, "designerIntent") ?? string.Empty,
            IsInitial = phaseId.Equals(initialPhaseId, StringComparison.OrdinalIgnoreCase),
            Mechanics = mechanics,
            OnEnterSteps = onEnterSteps,
            OnExitSteps = onExitSteps,
            Transitions = transitions,
            Sequences = sequences,
        };
    }

    private static FusionBossSequenceDefinition BuildSequenceDefinition(
        JsonElement sequence,
        IReadOnlyDictionary<int, FusionBossAttackGeometry> attackGeometries,
        FusionBossActionCatalog actionCatalog)
    {
        var steps = BuildTimelineSteps(
            sequence,
            "steps",
            attackGeometries,
            actionCatalog);
        return new FusionBossSequenceDefinition
        {
            Id = ReadString(sequence, "id") ?? "sequence",
            DisplayName = ReadString(sequence, "displayName") ??
                ReadString(sequence, "id") ?? "sequence",
            Summary = ReadString(sequence, "summary") ?? string.Empty,
            PlayerGoal = ReadString(sequence, "playerGoal") ?? string.Empty,
            DesignerIntent = ReadString(sequence, "designerIntent") ?? string.Empty,
            Scope = ReadString(sequence, "scope") ?? "phase",
            AutoStart = !sequence.TryGetProperty("autoStart", out var autoStart) ||
                autoStart.ValueKind != JsonValueKind.False,
            SourceKind = FusionBossTimelineSourceKind.Sequence,
            Steps = steps,
        };
    }

    private static IReadOnlyList<FusionBossTimelineStep> BuildTimelineSteps(
        JsonElement owner,
        string propertyName,
        IReadOnlyDictionary<int, FusionBossAttackGeometry> attackGeometries,
        FusionBossActionCatalog actionCatalog)
    {
        var steps = new List<FusionBossTimelineStep>();
        var rolePositions = new Dictionary<string, TimelinePointReference>(
            StringComparer.OrdinalIgnoreCase);
        var pendingRoleMoves = new Dictionary<string, TimelinePointReference>(
            StringComparer.OrdinalIgnoreCase);
        var earliestFrame = 0;
        var isExact = true;
        if (owner.TryGetProperty(propertyName, out var stepDefinitions) &&
            stepDefinitions.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var rawStep in stepDefinitions.EnumerateArray())
            {
                var step = BuildTimelineStep(
                    index++,
                    rawStep,
                    earliestFrame,
                    isExact,
                    attackGeometries,
                    rolePositions,
                    actionCatalog);
                steps.Add(step);
                earliestFrame += step.DurationFrames ?? 0;
                UpdateTimelineState(rawStep, rolePositions, pendingRoleMoves);
                if (step.Kind is FusionBossTimelineStepKind.WaitUntil or
                    FusionBossTimelineStepKind.Sequence or
                    FusionBossTimelineStepKind.RepeatSequence)
                {
                    isExact = false;
                }
            }
        }
        return steps;
    }

    private static FusionBossTimelineStep BuildTimelineStep(
        int index,
        JsonElement step,
        int earliestFrame,
        bool isExact,
        IReadOnlyDictionary<int, FusionBossAttackGeometry> attackGeometries,
        IReadOnlyDictionary<string, TimelinePointReference> rolePositions,
        FusionBossActionCatalog actionCatalog)
    {
        if (step.ValueKind == JsonValueKind.Array && step.GetArrayLength() > 0)
        {
            var elements = step.EnumerateArray().ToArray();
            var actionId = elements[0].ValueKind == JsonValueKind.String
                ? elements[0].GetString() ?? "action"
                : "action";
            if (actionId.Equals("wait", StringComparison.OrdinalIgnoreCase))
            {
                var duration = elements.Length > 1 && elements[1].TryGetInt32(out var frames)
                    ? Math.Max(0, frames)
                    : 0;
                return TimelineStep(
                    index,
                    FusionBossTimelineStepKind.Wait,
                    "Pausa temporizzata",
                    $"Lascia trascorrere {duration} frame ({duration / 60d:0.##} s).",
                    earliestFrame,
                    isExact,
                    duration,
                    technicalId: "wait",
                    technicalDetail: duration.ToString(CultureInfo.InvariantCulture),
                    category: "Tempo",
                    iconGlyph: "◷");
            }

            var kind = actionId.Equals("completeIf", StringComparison.OrdinalIgnoreCase)
                ? FusionBossTimelineStepKind.Guard
                : FusionBossTimelineStepKind.Action;
            var details = step.EnumerateArray().Skip(1).Select(SummarizeJson);
            FusionBossAttackGeometry? attackGeometry = null;
            FusionBossAttackTarget? attackTarget = null;
            IReadOnlyList<FusionBossAttackTarget> attackTargets = [];
            if (elements.Length > 1 &&
                elements[1].ValueKind == JsonValueKind.Object &&
                IsAttackAction(actionId) &&
                ReadInt(elements[1], "skillId") is { } skillId)
            {
                attackGeometries.TryGetValue(skillId, out attackGeometry);
                if (attackGeometry is not null)
                {
                    attackTargets = BuildAttackTargets(elements[1], rolePositions);
                    attackTarget = attackTargets.FirstOrDefault();
                }
            }
            var semantics = actionCatalog.Describe(actionId, elements, attackGeometry);
            return TimelineStep(
                index,
                kind,
                semantics.DisplayName,
                semantics.Description,
                earliestFrame,
                isExact,
                technicalId: actionId,
                technicalDetail: string.Join(" · ", details),
                category: semantics.Category,
                iconGlyph: semantics.IconGlyph,
                reads: semantics.Reads,
                writes: semantics.Writes,
                attackGeometry: attackGeometry,
                attackTarget: attackTarget,
                attackTargets: attackTargets);
        }

        if (step.ValueKind == JsonValueKind.Object)
        {
            if (step.TryGetProperty("waitUntil", out var waitUntil))
            {
                return TimelineStep(
                    index,
                    FusionBossTimelineStepKind.WaitUntil,
                    "Attendi una condizione",
                    actionCatalog.DescribeCondition(waitUntil),
                    earliestFrame,
                    isExact,
                    timeoutFrames: ReadInt(step, "timeoutFrames"),
                    technicalId: "waitUntil",
                    technicalDetail: SummarizeCondition(waitUntil),
                    category: "Sincronizzazione",
                    iconGlyph: "⌛");
            }
            if (ReadString(step, "sequence") is { } sequenceId)
            {
                return TimelineStep(
                    index,
                    FusionBossTimelineStepKind.Sequence,
                    "Esegui sequenza",
                    $"Esegue la sequenza “{sequenceId}” e attende che termini.",
                    earliestFrame,
                    isExact,
                    timeoutFrames: ReadInt(step, "timeoutFrames"),
                    referencedSequenceId: sequenceId,
                    technicalId: "sequence",
                    technicalDetail: ControlDetails(step),
                    category: "Flusso",
                    iconGlyph: "▶",
                    reads: [$"sequenza:{sequenceId}"]);
            }
            if (ReadString(step, "repeatSequence") is { } repeatedSequenceId)
            {
                return TimelineStep(
                    index,
                    FusionBossTimelineStepKind.RepeatSequence,
                    "Ripeti sequenza",
                    $"Ripete la sequenza “{repeatedSequenceId}” secondo i limiti configurati.",
                    earliestFrame,
                    isExact,
                    timeoutFrames: ReadInt(step, "timeoutFrames"),
                    referencedSequenceId: repeatedSequenceId,
                    technicalId: "repeatSequence",
                    technicalDetail: ControlDetails(step),
                    category: "Flusso",
                    iconGlyph: "↻",
                    reads: [$"sequenza:{repeatedSequenceId}"]);
            }
        }

        return TimelineStep(
            index,
            FusionBossTimelineStepKind.Unknown,
            "Step non riconosciuto",
            SummarizeJson(step),
            earliestFrame,
            isExact,
            technicalId: "unknown",
            technicalDetail: SummarizeJson(step),
            category: "Altro",
            iconGlyph: "?");
    }

    private static FusionBossTimelineStep TimelineStep(
        int index,
        FusionBossTimelineStepKind kind,
        string label,
        string detail,
        int earliestFrame,
        bool isExact,
        int? durationFrames = null,
        int? timeoutFrames = null,
        string? referencedSequenceId = null,
        string technicalId = "",
        string technicalDetail = "",
        string category = "",
        string iconGlyph = "",
        IReadOnlyList<string>? reads = null,
        IReadOnlyList<string>? writes = null,
        FusionBossAttackGeometry? attackGeometry = null,
        FusionBossAttackTarget? attackTarget = null,
        IReadOnlyList<FusionBossAttackTarget>? attackTargets = null) => new()
        {
            Index = index,
            Kind = kind,
            Label = label,
            Detail = Truncate(detail, 240),
            TechnicalId = technicalId,
            TechnicalDetail = Truncate(technicalDetail, 240),
            Category = category,
            IconGlyph = iconGlyph,
            Reads = reads ?? [],
            Writes = writes ?? [],
            EarliestStartFrame = earliestFrame,
            IsStartExact = isExact,
            DurationFrames = durationFrames,
            TimeoutFrames = timeoutFrames,
            ReferencedSequenceId = referencedSequenceId,
            AttackGeometry = attackGeometry,
            AttackTarget = attackTarget,
            AttackTargets = attackTargets ?? [],
        };

    private static bool IsAttackAction(string actionId) => actionId.Equals(
        "combat.cast",
        StringComparison.OrdinalIgnoreCase) || actionId.Equals(
        "alphaAbsMapSkill",
        StringComparison.OrdinalIgnoreCase) || actionId.Equals(
        "combat.castVolley",
        StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<FusionBossAttackTarget> BuildAttackTargets(
        JsonElement config,
        IReadOnlyDictionary<string, TimelinePointReference> rolePositions)
    {
        if (config.TryGetProperty("targets", out var targets) &&
            targets.ValueKind == JsonValueKind.Array)
        {
            return targets.EnumerateArray()
                .Where(target => target.ValueKind == JsonValueKind.Object)
                .Select(target => BuildAttackTarget(config, rolePositions, target))
                .ToArray();
        }
        return [BuildAttackTarget(config, rolePositions)];
    }

    private static FusionBossAttackTarget BuildAttackTarget(
        JsonElement config,
        IReadOnlyDictionary<string, TimelinePointReference> rolePositions,
        JsonElement? targetOverride = null)
    {
        var target = targetOverride ?? (config.TryGetProperty("target", out var targetValue) &&
            targetValue.ValueKind == JsonValueKind.Object
                ? targetValue
                : default);
        var origin = config.TryGetProperty("origin", out var originValue) &&
            originValue.ValueKind == JsonValueKind.Object
                ? originValue
                : default;
        var targetType = ReadString(target, "type") ?? string.Empty;
        var casterRole = ReadString(config, "casterRole") ??
            ReadString(config, "sourceRole") ?? string.Empty;
        var targetKey = ReadString(target, "key") ?? string.Empty;
        var targetAnchor = ReadString(target, "anchor") ?? string.Empty;
        var targetRole = ReadString(target, "role") ?? string.Empty;
        var originType = ReadString(origin, "type") ?? string.Empty;
        var originRole = ReadString(origin, "role") ?? string.Empty;
        var originAnchor = ReadString(origin, "anchor") ?? string.Empty;
        var requestedOriginRole = originType.Equals("role", StringComparison.OrdinalIgnoreCase)
            ? originRole
            : string.IsNullOrWhiteSpace(originType) ? casterRole : string.Empty;
        rolePositions.TryGetValue(requestedOriginRole, out var projectedOrigin);
        var displayText = targetType.ToLowerInvariant() switch
        {
            "captured" => $"target catturato: {targetKey}",
            "playerposition" => "posizione corrente del giocatore",
            "anchor" => $"anchor: {targetAnchor}",
            "role" => $"ruolo: {targetRole}",
            "point" => $"punto ({ReadDoubleNullable(target, "x")}, {ReadDoubleNullable(target, "y")})",
            _ => string.IsNullOrWhiteSpace(targetType) ? "target runtime" : targetType,
        };
        return new FusionBossAttackTarget
        {
            CastMode = ReadString(config, "mode") ??
                ReadString(config, "castMode") ?? "mapPoint",
            CasterRole = casterRole,
            TargetType = targetType,
            TargetKey = targetKey,
            TargetAnchor = targetAnchor,
            TargetRole = targetRole,
            TargetIndex = ReadInt(target, "index"),
            TargetX = ReadDoubleNullable(target, "x"),
            TargetY = ReadDoubleNullable(target, "y"),
            OriginType = projectedOrigin?.Type ?? originType,
            OriginKey = projectedOrigin?.Key ?? string.Empty,
            OriginAnchor = projectedOrigin?.Anchor ?? originAnchor,
            OriginRole = projectedOrigin?.Role ??
                (string.IsNullOrWhiteSpace(originRole) &&
                    (ReadString(config, "mode") ?? string.Empty).Equals(
                        "projectileFromPoint",
                        StringComparison.OrdinalIgnoreCase)
                        ? casterRole
                        : originRole),
            OriginX = projectedOrigin?.X ?? ReadDoubleNullable(origin, "x"),
            OriginY = projectedOrigin?.Y ?? ReadDoubleNullable(origin, "y"),
            IsDynamic = targetType.Equals("captured", StringComparison.OrdinalIgnoreCase) ||
                targetType.Equals("playerPosition", StringComparison.OrdinalIgnoreCase) ||
                targetType.Equals("role", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(targetType),
            IsOriginDynamic = projectedOrigin?.IsDynamic ??
                (originType.Equals("role", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(originType)),
            DisplayText = displayText,
        };
    }

    private static void UpdateTimelineState(
        JsonElement step,
        IDictionary<string, TimelinePointReference> rolePositions,
        IDictionary<string, TimelinePointReference> pendingRoleMoves)
    {
        if (step.ValueKind == JsonValueKind.Array && step.GetArrayLength() > 1)
        {
            var elements = step.EnumerateArray().ToArray();
            var actionId = elements[0].ValueKind == JsonValueKind.String
                ? elements[0].GetString() ?? string.Empty
                : string.Empty;
            if (actionId.Equals("combat.moveTo", StringComparison.OrdinalIgnoreCase) &&
                elements[1].ValueKind == JsonValueKind.Object)
            {
                var role = ReadString(elements[1], "role") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(role) &&
                    elements[1].TryGetProperty("target", out var target) &&
                    target.ValueKind == JsonValueKind.Object)
                {
                    pendingRoleMoves[role] = ReadTimelinePointReference(target);
                }
            }
            return;
        }

        if (step.ValueKind != JsonValueKind.Object ||
            !step.TryGetProperty("waitUntil", out var waitUntil) ||
            waitUntil.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        var condition = waitUntil.EnumerateArray().ToArray();
        if (condition.Length < 2 ||
            condition[0].ValueKind != JsonValueKind.String ||
            !string.Equals(
                condition[0].GetString(),
                "roleMovementComplete",
                StringComparison.OrdinalIgnoreCase) ||
            condition[1].ValueKind != JsonValueKind.String)
        {
            return;
        }
        var completedRole = condition[1].GetString() ?? string.Empty;
        if (pendingRoleMoves.Remove(completedRole, out var destination))
        {
            rolePositions[completedRole] = destination;
        }
    }

    private static TimelinePointReference ReadTimelinePointReference(JsonElement point)
    {
        var type = ReadString(point, "type") ?? string.Empty;
        return new TimelinePointReference(
            type,
            ReadString(point, "key") ?? string.Empty,
            ReadString(point, "anchor") ?? string.Empty,
            ReadString(point, "role") ?? string.Empty,
            ReadDoubleNullable(point, "x"),
            ReadDoubleNullable(point, "y"),
            type.Equals("captured", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("playerPosition", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("role", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(type));
    }

    private sealed record TimelinePointReference(
        string Type,
        string Key,
        string Anchor,
        string Role,
        double? X,
        double? Y,
        bool IsDynamic);

    private static string ControlDetails(JsonElement control)
    {
        var details = new List<string>();
        if (ReadString(control, "sequence") is { } sequence) details.Add(sequence);
        if (ReadString(control, "repeatSequence") is { } repeat) details.Add(repeat);
        if (ReadInt(control, "maxIterations") is { } iterations) details.Add($"max {iterations}×");
        if (control.TryGetProperty("until", out var until))
        {
            details.Add($"finché {SummarizeCondition(until)}");
        }
        if (control.TryGetProperty("when", out var when))
        {
            details.Add($"se {SummarizeCondition(when)}");
        }
        if (control.TryGetProperty("await", out var awaitValue) &&
            awaitValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            details.Add(awaitValue.GetBoolean() ? "await" : "non bloccante");
        }
        return string.Join(" · ", details);
    }

    private static string SummarizeCondition(JsonElement condition)
    {
        if (condition.ValueKind == JsonValueKind.Array && condition.GetArrayLength() > 0)
        {
            var parts = condition.EnumerateArray().Select(SummarizeJson).ToArray();
            return parts.Length == 1
                ? parts[0]
                : $"{parts[0]}({string.Join(", ", parts.Skip(1))})";
        }
        return SummarizeJson(condition);
    }

    private static string SummarizeJson(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Array => $"[{string.Join(", ", value.EnumerateArray().Take(5).Select(SummarizeJson))}{(value.GetArrayLength() > 5 ? ", …" : string.Empty)}]",
            JsonValueKind.Object => SummarizeObject(value),
            _ => value.GetRawText(),
        };
    }

    private static string SummarizeObject(JsonElement value)
    {
        var properties = value.EnumerateObject().ToArray();
        var visible = properties.Take(6).Select(property =>
            $"{property.Name}={SummarizeJson(property.Value)}");
        return $"{string.Join(", ", visible)}{(properties.Length > 6 ? ", …" : string.Empty)}";
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : $"{value[..(maximumLength - 1)]}…";

    private async Task<DatabaseLoadResult> LoadDatabaseAsync(
        ZiapProject project,
        DatabaseSpec spec,
        bool pluginIsActive,
        ICollection<FusionBossDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(project.Path, "data", spec.FileName);
        if (!_fileSystem.FileExists(sourcePath))
        {
            if (pluginIsActive)
            {
                diagnostics.Add(Error(
                    "database.missing",
                    $"{spec.FileName} non esiste, ma {spec.PluginId} è attivo.",
                    spec.Kind));
            }
            return new DatabaseLoadResult(new FusionBossDatabaseSummary
            {
                Kind = spec.Kind,
                DisplayName = spec.DisplayName,
                SourcePath = sourcePath,
            });
        }

        try
        {
            var bytes = await _fileSystem.ReadAllBytesAsync(sourcePath, cancellationToken);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Error(
                    "database.root",
                    $"{spec.FileName} deve contenere un oggetto JSON alla radice.",
                    spec.Kind));
                return new DatabaseLoadResult(MissingStructureSummary(spec, sourcePath));
            }

            var root = document.RootElement;
            var schemaVersion = ReadInt(root, "schemaVersion");
            var databaseVersion = ReadString(root, "databaseVersion");
            if (schemaVersion is null or <= 0)
            {
                diagnostics.Add(Error(
                    "database.schema-version",
                    $"{spec.FileName} non dichiara uno schemaVersion valido.",
                    spec.Kind));
            }
            if (string.IsNullOrWhiteSpace(databaseVersion))
            {
                diagnostics.Add(Warning(
                    "database.version",
                    $"{spec.FileName} non dichiara databaseVersion.",
                    spec.Kind));
            }

            var collections = new Dictionary<string, Dictionary<string, JsonElement>>(
                StringComparer.OrdinalIgnoreCase);
            var collectionSummaries = new List<FusionBossCollectionSummary>();
            foreach (var collectionId in spec.Collections)
            {
                var displayName = CollectionDisplayName(collectionId);
                if (!root.TryGetProperty(collectionId, out var collection) ||
                    collection.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(Error(
                        "database.collection",
                        $"{spec.FileName} non contiene la collezione '{collectionId}'.",
                        spec.Kind,
                        collectionId));
                    collectionSummaries.Add(new FusionBossCollectionSummary
                    {
                        Id = collectionId,
                        DisplayName = displayName,
                    });
                    continue;
                }

                var records = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in collection.EnumerateObject())
                {
                    if (!records.TryAdd(record.Name, record.Value.Clone()))
                    {
                        diagnostics.Add(Error(
                            "record.id-duplicate",
                            $"L'identificatore '{record.Name}' compare più volte in {collectionId}.",
                            spec.Kind,
                            collectionId,
                            record.Name));
                    }
                }
                collections[collectionId] = records;
                collectionSummaries.Add(new FusionBossCollectionSummary
                {
                    Id = collectionId,
                    DisplayName = displayName,
                    RecordIds = records.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
                });
            }

            return new DatabaseLoadResult(
                new FusionBossDatabaseSummary
                {
                    Kind = spec.Kind,
                    DisplayName = spec.DisplayName,
                    SourcePath = sourcePath,
                    Exists = true,
                    SchemaVersion = schemaVersion,
                    DatabaseVersion = databaseVersion,
                    Collections = collectionSummaries,
                },
                new ParsedDatabase(collections));
        }
        catch (JsonException exception)
        {
            diagnostics.Add(Error(
                "database.json-invalid",
                $"{spec.FileName} non contiene JSON valido.",
                spec.Kind,
                details: exception.Message));
            return new DatabaseLoadResult(MissingStructureSummary(spec, sourcePath));
        }
    }

    private async Task ValidateReferencesAsync(
        ZiapProject project,
        IReadOnlyDictionary<FusionBossDatabaseKind, ParsedDatabase> databases,
        IReadOnlyList<FusionBossPluginStatus> plugins,
        ICollection<FusionBossDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var enemies = Records(databases, FusionBossDatabaseKind.Combat, "enemies");
        var bosses = Records(databases, FusionBossDatabaseKind.Combat, "bosses");
        var scalingProfiles = Records(databases, FusionBossDatabaseKind.Combat, "scalingProfiles");
        var encounters = Records(databases, FusionBossDatabaseKind.Encounters, "encounters");
        var arenas = Records(databases, FusionBossDatabaseKind.Arenas, "arenas");
        var completionProfiles = Records(
            databases,
            FusionBossDatabaseKind.Arenas,
            "completionProfiles");
        var puzzles = Records(databases, FusionBossDatabaseKind.Puzzles, "puzzles");

        foreach (var (bossId, boss) in bosses)
        {
            ValidateReference(
                ReadString(boss, "enemy"),
                enemies,
                "boss.enemy-missing",
                $"Il boss '{bossId}' riferisce un enemy inesistente.",
                FusionBossDatabaseKind.Combat,
                "bosses",
                bossId,
                diagnostics);
            if (TryGetObject(boss, "scaling", out var scaling))
            {
                ValidateReference(
                    ReadString(scaling, "profile"),
                    scalingProfiles,
                    "boss.scaling-profile-missing",
                    $"Il boss '{bossId}' riferisce uno scaling profile inesistente.",
                    FusionBossDatabaseKind.Combat,
                    "bosses",
                    bossId,
                    diagnostics);
            }
        }

        foreach (var (encounterId, encounter) in encounters)
        {
            ValidateReference(
                ReadString(encounter, "boss"),
                bosses,
                "encounter.boss-missing",
                $"L'encounter '{encounterId}' riferisce un boss inesistente.",
                FusionBossDatabaseKind.Encounters,
                "encounters",
                encounterId,
                diagnostics);
            ValidateEncounterPhases(encounterId, encounter, diagnostics);
        }

        foreach (var (arenaId, arena) in arenas)
        {
            ValidateReference(
                ReadString(arena, "encounterId"),
                encounters,
                "arena.encounter-missing",
                $"L'arena '{arenaId}' riferisce un encounter inesistente.",
                FusionBossDatabaseKind.Arenas,
                "arenas",
                arenaId,
                diagnostics);
            ValidateReference(
                ReadString(arena, "completionProfile"),
                completionProfiles,
                "arena.completion-profile-missing",
                $"L'arena '{arenaId}' riferisce un completion profile inesistente.",
                FusionBossDatabaseKind.Arenas,
                "arenas",
                arenaId,
                diagnostics);
            ValidateArenaBindings(arenaId, arena, enemies, diagnostics);
            ValidateArenaPuzzles(arenaId, arena, puzzles, plugins, diagnostics);
            await ValidateArenaMapsAsync(
                project,
                arenaId,
                arena,
                diagnostics,
                cancellationToken);
        }
    }

    private static void ValidateEncounterPhases(
        string encounterId,
        JsonElement encounter,
        ICollection<FusionBossDiagnostic> diagnostics)
    {
        if (!TryGetObject(encounter, "phases", out var phases))
        {
            diagnostics.Add(Error(
                "encounter.phases-missing",
                $"L'encounter '{encounterId}' non dichiara le fasi.",
                FusionBossDatabaseKind.Encounters,
                "encounters",
                encounterId));
            return;
        }

        var phaseIds = phases.EnumerateObject()
            .Select(phase => phase.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var initialPhase = ReadString(encounter, "initialPhase");
        if (string.IsNullOrWhiteSpace(initialPhase) || !phaseIds.Contains(initialPhase))
        {
            diagnostics.Add(Error(
                "encounter.initial-phase-missing",
                $"L'encounter '{encounterId}' ha una initialPhase inesistente.",
                FusionBossDatabaseKind.Encounters,
                "encounters",
                encounterId));
        }

        foreach (var phase in phases.EnumerateObject())
        {
            if (!phase.Value.TryGetProperty("transitions", out var transitions) ||
                transitions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var transition in transitions.EnumerateArray())
            {
                var destination = ReadString(transition, "to");
                if (!string.IsNullOrWhiteSpace(destination) && !phaseIds.Contains(destination))
                {
                    diagnostics.Add(Error(
                        "encounter.transition-target-missing",
                        $"La fase '{phase.Name}' dell'encounter '{encounterId}' transiziona verso '{destination}', che non esiste.",
                        FusionBossDatabaseKind.Encounters,
                        "encounters",
                        encounterId));
                }
            }
        }
    }

    private static void ValidateArenaBindings(
        string arenaId,
        JsonElement arena,
        IReadOnlyDictionary<string, JsonElement> enemies,
        ICollection<FusionBossDiagnostic> diagnostics)
    {
        if (!TryGetObject(arena, "bindings", out var bindings))
        {
            return;
        }
        foreach (var binding in bindings.EnumerateObject())
        {
            var entityId = ReadString(binding.Value, "entityId");
            if (!string.IsNullOrWhiteSpace(entityId) && !enemies.ContainsKey(entityId))
            {
                diagnostics.Add(Error(
                    "arena.entity-missing",
                    $"Il binding '{binding.Name}' dell'arena '{arenaId}' riferisce l'entità inesistente '{entityId}'.",
                    FusionBossDatabaseKind.Arenas,
                    "arenas",
                    arenaId));
            }
        }
    }

    private static void ValidateArenaPuzzles(
        string arenaId,
        JsonElement arena,
        IReadOnlyDictionary<string, JsonElement> puzzles,
        IReadOnlyList<FusionBossPluginStatus> plugins,
        ICollection<FusionBossDiagnostic> diagnostics)
    {
        if (!arena.TryGetProperty("providers", out var providers) ||
            providers.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var provider in providers.EnumerateArray())
        {
            if (!string.Equals(ReadString(provider, "type"), "fusionPuzzle", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var puzzleId = ReadString(provider, "puzzleId");
            if (string.IsNullOrWhiteSpace(puzzleId) || !puzzles.ContainsKey(puzzleId))
            {
                diagnostics.Add(Error(
                    "arena.puzzle-missing",
                    $"L'arena '{arenaId}' riferisce il puzzle inesistente '{puzzleId ?? "(vuoto)"}'.",
                    FusionBossDatabaseKind.Arenas,
                    "arenas",
                    arenaId));
            }
            if (plugins.FirstOrDefault(plugin =>
                    plugin.Id == FusionBossIntegrationProvider.PuzzlePluginName)?.IsActive != true)
            {
                diagnostics.Add(Error(
                    "arena.puzzle-plugin-inactive",
                    $"L'arena '{arenaId}' usa un puzzle, ma ZDP_FusionPuzzle non è attivo.",
                    FusionBossDatabaseKind.Arenas,
                    "arenas",
                    arenaId));
            }
        }
    }

    private async Task ValidateArenaMapsAsync(
        ZiapProject project,
        string arenaId,
        JsonElement arena,
        ICollection<FusionBossDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!arena.TryGetProperty("mapIds", out var mapIds) ||
            mapIds.ValueKind != JsonValueKind.Array ||
            mapIds.GetArrayLength() == 0)
        {
            diagnostics.Add(Error(
                "arena.maps-missing",
                $"L'arena '{arenaId}' non dichiara alcuna mappa.",
                FusionBossDatabaseKind.Arenas,
                "arenas",
                arenaId));
            return;
        }

        var requiredAnchors = ReadRequiredCounts(arena, "anchors");
        foreach (var mapIdElement in mapIds.EnumerateArray())
        {
            if (!mapIdElement.TryGetInt32(out var mapId) || mapId <= 0)
            {
                diagnostics.Add(Error(
                    "arena.map-id-invalid",
                    $"L'arena '{arenaId}' contiene un mapId non valido.",
                    FusionBossDatabaseKind.Arenas,
                    "arenas",
                    arenaId));
                continue;
            }

            var mapPath = Path.Combine(project.Path, "data", $"Map{mapId:000}.json");
            if (!_fileSystem.FileExists(mapPath))
            {
                diagnostics.Add(Error(
                    "arena.map-missing",
                    $"La mappa #{mapId} dell'arena '{arenaId}' non esiste.",
                    FusionBossDatabaseKind.Arenas,
                    "arenas",
                    arenaId));
                continue;
            }

            try
            {
                using var map = JsonDocument.Parse(
                    await _fileSystem.ReadAllBytesAsync(mapPath, cancellationToken));
                var foundAnchors = ScanArenaAnchors(map.RootElement, arenaId);
                foreach (var (anchorId, requiredCount) in requiredAnchors)
                {
                    foundAnchors.TryGetValue(anchorId, out var foundCount);
                    if (foundCount < requiredCount)
                    {
                        diagnostics.Add(Error(
                            "arena.anchor-count",
                            $"Map{mapId:000}: l'arena '{arenaId}' richiede {requiredCount} anchor '{anchorId}', trovati {foundCount}.",
                            FusionBossDatabaseKind.Arenas,
                            "arenas",
                            arenaId));
                    }
                }
            }
            catch (JsonException exception)
            {
                diagnostics.Add(Error(
                    "arena.map-json-invalid",
                    $"Map{mapId:000}.json non contiene JSON valido.",
                    FusionBossDatabaseKind.Arenas,
                    "arenas",
                    arenaId,
                    exception.Message));
            }
        }
    }

    private static Dictionary<string, int> ScanArenaAnchors(JsonElement map, string arenaId)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!map.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var mapEvent in events.EnumerateArray())
        {
            if (mapEvent.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            var note = ReadString(mapEvent, "note");
            if (string.IsNullOrWhiteSpace(note) || !ArenaTagRegex().Matches(note)
                .Any(match => match.Groups[1].Value.Equals(arenaId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            foreach (Match match in AnchorTagRegex().Matches(note))
            {
                var anchorId = match.Groups[1].Value.Trim();
                result[anchorId] = result.GetValueOrDefault(anchorId) + 1;
            }
        }
        return result;
    }

    private static Dictionary<string, int> ReadRequiredCounts(JsonElement parent, string propertyName)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetObject(parent, propertyName, out var definitions))
        {
            return result;
        }
        foreach (var definition in definitions.EnumerateObject())
        {
            var count = ReadInt(definition.Value, "count") ?? 1;
            result[definition.Name] = Math.Max(0, count);
        }
        return result;
    }

    private static void ValidateReference(
        string? reference,
        IReadOnlyDictionary<string, JsonElement> targets,
        string code,
        string message,
        FusionBossDatabaseKind database,
        string collectionId,
        string recordId,
        ICollection<FusionBossDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(reference) || !targets.ContainsKey(reference))
        {
            diagnostics.Add(Error(code, message, database, collectionId, recordId));
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> Records(
        IReadOnlyDictionary<FusionBossDatabaseKind, ParsedDatabase> databases,
        FusionBossDatabaseKind kind,
        string collectionId) =>
        databases.TryGetValue(kind, out var database) &&
        database.Collections.TryGetValue(collectionId, out var records)
            ? records
            : EmptyRecords;

    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyRecords =
        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

    private static bool IsPluginActive(
        IReadOnlyList<RpgMakerPluginRegistration> registrations,
        string pluginId) => registrations.Any(plugin =>
            plugin.IsActive && RpgMakerPluginRegistryService.PluginNameEquals(plugin.Name, pluginId));

    private static FusionBossDatabaseSummary MissingStructureSummary(
        DatabaseSpec spec,
        string sourcePath) => new()
        {
            Kind = spec.Kind,
            DisplayName = spec.DisplayName,
            SourcePath = sourcePath,
            Exists = true,
        };

    private static string CollectionDisplayName(string id) => id switch
    {
        "scalingProfiles" => "Scaling profiles",
        "rewardRules" => "Reward rules",
        "enemies" => "Enemies",
        "bosses" => "Boss",
        "encounters" => "Encounters",
        "arenas" => "Arenas",
        "completionProfiles" => "Completion profiles",
        "puzzles" => "Puzzles",
        _ => id,
    };

    private static int? ReadInt(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.TryGetInt32(out var value)
            ? value
            : null;

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static IReadOnlyList<string> ReadStringArray(
        JsonElement element,
        string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String
                    ? item.GetString() ?? string.Empty
                    : string.Empty)
                .ToArray()
            : [];

    private static IReadOnlyList<int> ReadIntArray(
        JsonElement element,
        string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray()
                .Select(item => item.TryGetInt32(out var value) ? value : 0)
                .ToArray()
            : [];

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.True;

    private static double ReadDouble(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.TryGetDouble(out var value)
            ? value
            : 0;

    private static double? ReadDoubleNullable(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.TryGetDouble(out var value)
            ? value
            : null;

    private static bool TryGetObject(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }
        value = default;
        return false;
    }

    private static FusionBossDiagnostic Error(
        string code,
        string message,
        FusionBossDatabaseKind? database = null,
        string? collectionId = null,
        string? recordId = null,
        string? details = null) => Diagnostic(
            code,
            FusionBossDiagnosticSeverity.Error,
            message,
            database,
            collectionId,
            recordId,
            details);

    private static FusionBossDiagnostic Warning(
        string code,
        string message,
        FusionBossDatabaseKind? database = null,
        string? collectionId = null,
        string? recordId = null) => Diagnostic(
            code,
            FusionBossDiagnosticSeverity.Warning,
            message,
            database,
            collectionId,
            recordId);

    private static FusionBossDiagnostic Diagnostic(
        string code,
        FusionBossDiagnosticSeverity severity,
        string message,
        FusionBossDatabaseKind? database,
        string? collectionId,
        string? recordId,
        string? details = null) => new()
        {
            Code = code,
            Severity = severity,
            Message = message,
            Database = database,
            CollectionId = collectionId,
            RecordId = recordId,
            Details = details,
            NavigationTarget = BuildNavigation(database, collectionId, recordId),
        };

    private static Uri BuildNavigation(
        FusionBossDatabaseKind? database,
        string? collectionId,
        string? recordId)
    {
        var path = new List<string>();
        if (database is not null) path.Add(database.Value.ToString().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(collectionId)) path.Add(Uri.EscapeDataString(collectionId));
        if (!string.IsNullOrWhiteSpace(recordId)) path.Add(Uri.EscapeDataString(recordId));
        return new Uri($"fusionboss://workspace/{string.Join('/', path)}");
    }

    [GeneratedRegex(@"<\s*FusionArena\s*:\s*([^>]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex ArenaTagRegex();

    [GeneratedRegex(@"<\s*FusionAnchor\s*:\s*([^>]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex AnchorTagRegex();

    [GeneratedRegex(@"<\s*FusionRole\s*:\s*([^>]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex RoleTagRegex();

    [GeneratedRegex(@"<ABS>(.*?)</ABS>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AbsBlockRegex();

    private sealed record DatabaseSpec(
        FusionBossDatabaseKind Kind,
        string DisplayName,
        string FileName,
        string PluginId,
        IReadOnlyList<string> Collections);

    private sealed record PluginSpec(string Id, string DisplayName, bool IsRequired);

    private sealed record ParsedDatabase(
        IReadOnlyDictionary<string, Dictionary<string, JsonElement>> Collections);

    private sealed record DatabaseLoadResult(
        FusionBossDatabaseSummary Summary,
        ParsedDatabase? Parsed = null);
}
