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
        return new FusionBossWorkspaceDocument
        {
            Descriptor = descriptor,
            Plugins = plugins,
            Databases = summaries,
            Diagnostics = diagnostics
                .OrderByDescending(diagnostic => diagnostic.Severity)
                .ThenBy(diagnostic => diagnostic.Database)
                .ThenBy(diagnostic => diagnostic.RecordId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

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
