using System.Text.Json;
using ZiapStudio.Core.Fusion.Story;

namespace ZiapStudio.Services.Fusion.Story;

/// <summary>
/// Immutable, read-only lookup tables used while projecting RPG Maker commands.
/// The files are read once for a Story workspace; a missing or malformed lookup
/// only removes a friendly name and never prevents raw command preservation.
/// </summary>
public sealed class RpgMakerStoryResourceResolver
{
    private readonly IReadOnlyDictionary<int, string> _maps;
    private readonly IReadOnlyDictionary<int, string> _switches;
    private readonly IReadOnlyDictionary<int, string> _variables;
    private readonly IReadOnlyDictionary<int, string> _actors;
    private readonly IReadOnlyDictionary<int, string> _items;
    private readonly IReadOnlyDictionary<int, string> _weapons;
    private readonly IReadOnlyDictionary<int, string> _armors;
    private readonly IReadOnlyDictionary<int, string> _animations;
    private readonly IReadOnlyDictionary<int, string> _commonEvents;
    private readonly IReadOnlyDictionary<int, string> _states;
    private readonly IReadOnlyDictionary<int, string> _skills;
    private readonly IReadOnlySet<int> _commonEventIds;
    private readonly IReadOnlySet<int> _actorIds;
    private readonly IReadOnlySet<int> _stateIds;
    private readonly IReadOnlySet<int> _skillIds;

    private RpgMakerStoryResourceResolver(
        IReadOnlyList<RpgMakerStoryMapInfo> mapInfos,
        IReadOnlyDictionary<int, string> switches,
        IReadOnlyDictionary<int, string> variables,
        IReadOnlyDictionary<int, string> actors,
        IReadOnlyDictionary<int, string> items,
        IReadOnlyDictionary<int, string> weapons,
        IReadOnlyDictionary<int, string> armors,
        IReadOnlyDictionary<int, string> animations,
        IReadOnlyDictionary<int, string> commonEvents,
        IReadOnlyDictionary<int, string> states,
        IReadOnlyDictionary<int, string> skills,
        IReadOnlySet<int> commonEventIds,
        IReadOnlySet<int> actorIds,
        IReadOnlySet<int> stateIds,
        IReadOnlySet<int> skillIds)
    {
        MapInfos = mapInfos;
        _maps = mapInfos.ToDictionary(map => map.Id, map => map.Name);
        _switches = switches;
        _variables = variables;
        _actors = actors;
        _items = items;
        _weapons = weapons;
        _armors = armors;
        _animations = animations;
        _commonEvents = commonEvents;
        _states = states;
        _skills = skills;
        _commonEventIds = commonEventIds;
        _actorIds = actorIds;
        _stateIds = stateIds;
        _skillIds = skillIds;
    }

    public IReadOnlyList<RpgMakerStoryMapInfo> MapInfos { get; }

    public static async Task<RpgMakerStoryResourceResolver> LoadAsync(
        FileSystemService fileSystem,
        string projectPath,
        ICollection<StoryDiagnostic> diagnostics,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var dataPath = Path.Combine(projectPath, "data");
        var mapInfos = await ReadMapInfosAsync(fileSystem, dataPath, diagnostics, cancellationToken);
        var system = await ReadDocumentAsync(fileSystem, dataPath, "System.json", diagnostics, cancellationToken);
        var switches = ReadNamedArray(system, "switches");
        var variables = ReadNamedArray(system, "variables");
        var actors = await ReadDatabaseEntriesAsync(fileSystem, dataPath, "Actors.json", diagnostics, cancellationToken);
        var commonEvents = await ReadDatabaseEntriesAsync(fileSystem, dataPath, "CommonEvents.json", diagnostics, cancellationToken);
        var states = await ReadDatabaseEntriesAsync(fileSystem, dataPath, "States.json", diagnostics, cancellationToken);
        var skills = await ReadDatabaseEntriesAsync(fileSystem, dataPath, "Skills.json", diagnostics, cancellationToken);

        return new RpgMakerStoryResourceResolver(
            mapInfos,
            switches,
            variables,
            actors.Names,
            await ReadDatabaseNamesAsync(fileSystem, dataPath, "Items.json", diagnostics, cancellationToken),
            await ReadDatabaseNamesAsync(fileSystem, dataPath, "Weapons.json", diagnostics, cancellationToken),
            await ReadDatabaseNamesAsync(fileSystem, dataPath, "Armors.json", diagnostics, cancellationToken),
            await ReadDatabaseNamesAsync(fileSystem, dataPath, "Animations.json", diagnostics, cancellationToken),
            commonEvents.Names,
            states.Names,
            skills.Names,
            commonEvents.Ids,
            actors.Ids,
            states.Ids,
            skills.Ids);
    }

    public string DescribeMap(int id) => _maps.TryGetValue(id, out var name)
        ? $"{id:000} — {name}"
        : $"Map {id:000}";

    public string DescribeSwitch(int id) => Describe("Switch", id, _switches);

    public string DescribeVariable(int id) => Describe("Variable", id, _variables);

    public string DescribeActor(int id) => Describe("Actor", id, _actors);

    public string DescribeItem(int id) => Describe("Item", id, _items);

    public string DescribeWeapon(int id) => Describe("Weapon", id, _weapons);

    public string DescribeArmor(int id) => Describe("Armor", id, _armors);

    public string DescribeAnimation(int id) => Describe("Animation", id, _animations);

    public string DescribeCommonEvent(int id) => Describe("Common Event", id, _commonEvents);

    public string DescribeState(int id) => Describe("State", id, _states);

    public string DescribeSkill(int id) => Describe("Skill", id, _skills);

    public bool HasCommonEvent(int id) => _commonEventIds.Contains(id);

    public bool HasActor(int id) => _actorIds.Contains(id);

    public bool HasState(int id) => _stateIds.Contains(id);

    public bool HasSkill(int id) => _skillIds.Contains(id);

    private static string Describe(string category, int id, IReadOnlyDictionary<int, string> names) =>
        names.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
            ? $"{category} #{id} \"{name}\""
            : $"{category} #{id}";

    private static async Task<IReadOnlyList<RpgMakerStoryMapInfo>> ReadMapInfosAsync(
        FileSystemService fileSystem,
        string dataPath,
        ICollection<StoryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(fileSystem, dataPath, "MapInfos.json", diagnostics, cancellationToken, required: true);
        if (document is not { ValueKind: JsonValueKind.Array } array)
        {
            if (document is not null)
            {
                diagnostics.Add(Diagnostic(
                    "story.map-infos-invalid", StoryDiagnosticSeverity.Error,
                    "MapInfos.json deve contenere un array RPG Maker.", "data/MapInfos.json"));
            }
            return [];
        }

        return array.EnumerateArray()
            .Select((entry, index) => entry.ValueKind == JsonValueKind.Object
                ? new RpgMakerStoryMapInfo(
                    ReadInt(entry, "id") ?? index,
                    ReadString(entry, "name") ?? string.Empty,
                    ReadInt(entry, "order") ?? int.MaxValue,
                    ReadInt(entry, "parentId") ?? 0,
                    ReadBoolean(entry, "expanded"))
                : null)
            .Where(info => info is { Id: > 0 })
            .Select(info => info!)
            .OrderBy(info => info.Order)
            .ThenBy(info => info.Id)
            .ToArray();
    }

    private static async Task<IReadOnlyDictionary<int, string>> ReadDatabaseNamesAsync(
        FileSystemService fileSystem,
        string dataPath,
        string fileName,
        ICollection<StoryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(fileSystem, dataPath, fileName, diagnostics, cancellationToken);
        if (document is not { ValueKind: JsonValueKind.Array } array)
        {
            return new Dictionary<int, string>();
        }

        return array.EnumerateArray()
            .Select((entry, index) => entry.ValueKind == JsonValueKind.Object
                ? (Id: ReadInt(entry, "id") ?? index, Name: ReadString(entry, "name"))
                : (Id: 0, Name: (string?)null))
            .Where(entry => entry.Id > 0 && !string.IsNullOrWhiteSpace(entry.Name))
            .GroupBy(entry => entry.Id)
            .ToDictionary(group => group.Key, group => group.Last().Name!);
    }

    private static async Task<RpgMakerStoryDatabaseEntries> ReadDatabaseEntriesAsync(
        FileSystemService fileSystem,
        string dataPath,
        string fileName,
        ICollection<StoryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(fileSystem, dataPath, fileName, diagnostics, cancellationToken);
        if (document is not { ValueKind: JsonValueKind.Array } array)
        {
            return RpgMakerStoryDatabaseEntries.Empty;
        }

        var entries = array.EnumerateArray()
            .Select((entry, index) => entry.ValueKind == JsonValueKind.Object
                ? (Id: ReadInt(entry, "id") ?? index, Name: ReadString(entry, "name"))
                : (Id: 0, Name: (string?)null))
            .Where(entry => entry.Id > 0)
            .ToArray();
        return new RpgMakerStoryDatabaseEntries(
            entries.Select(entry => entry.Id).ToHashSet(),
            entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .GroupBy(entry => entry.Id)
                .ToDictionary(group => group.Key, group => group.Last().Name!));
    }

    private static async Task<JsonElement?> ReadDocumentAsync(
        FileSystemService fileSystem,
        string dataPath,
        string fileName,
        ICollection<StoryDiagnostic> diagnostics,
        CancellationToken cancellationToken,
        bool required = false)
    {
        var path = Path.Combine(dataPath, fileName);
        var relativePath = $"data/{fileName}";
        if (!fileSystem.FileExists(path))
        {
            if (required)
            {
                diagnostics.Add(Diagnostic("story.map-infos-missing", StoryDiagnosticSeverity.Warning,
                    "data/MapInfos.json non esiste; non è possibile enumerare le mappe.", relativePath));
            }
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(await fileSystem.ReadAllTextAsync(path, cancellationToken));
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Diagnostic(
                required ? "story.map-infos-unreadable" : "story.resource-unreadable",
                required ? StoryDiagnosticSeverity.Error : StoryDiagnosticSeverity.Warning,
                required ? "MapInfos.json non è leggibile." : $"{relativePath} non è leggibile; i nomi delle risorse restano ID raw.",
                relativePath));
            return null;
        }
    }

    private static IReadOnlyDictionary<int, string> ReadNamedArray(JsonElement? document, string propertyName)
    {
        if (document is not { ValueKind: JsonValueKind.Object } root ||
            !root.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<int, string>();
        }

        return values.EnumerateArray()
            .Select((entry, index) => (Index: index, Name: entry.ValueKind == JsonValueKind.String ? entry.GetString() : null))
            .Where(entry => entry.Index > 0 && !string.IsNullOrWhiteSpace(entry.Name))
            .ToDictionary(entry => entry.Index, entry => entry.Name!);
    }

    private static StoryDiagnostic Diagnostic(string code, StoryDiagnosticSeverity severity, string message, string path) => new()
    {
        Code = code,
        Severity = severity,
        Message = message,
        SourcePath = path,
    };

    private static string? ReadString(JsonElement source, string propertyName) =>
        source.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? ReadInt(JsonElement source, string propertyName) =>
        source.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var value)
            ? value
            : null;

    private static bool ReadBoolean(JsonElement source, string propertyName) =>
        source.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;
}

public sealed record RpgMakerStoryMapInfo(int Id, string Name, int Order, int ParentId, bool Expanded);

internal sealed record RpgMakerStoryDatabaseEntries(
    IReadOnlySet<int> Ids,
    IReadOnlyDictionary<int, string> Names)
{
    public static RpgMakerStoryDatabaseEntries Empty { get; } = new(
        new HashSet<int>(), new Dictionary<int, string>());
}

/// <summary>Map-local names that cannot be resolved from global RPG Maker databases.</summary>
public sealed record RpgMakerStoryCommandContext
{
    public int? MapId { get; init; }
    public int? EventId { get; init; }
    public IReadOnlyDictionary<int, string> EventNames { get; init; } = new Dictionary<int, string>();

    public string DescribeEvent(int eventId) => eventId switch
    {
        -1 => "Player",
        0 => "This Event",
        _ when EventNames.TryGetValue(eventId, out var name) && !string.IsNullOrWhiteSpace(name) =>
            $"Event #{eventId} \"{name}\"",
        _ => $"Event #{eventId}",
    };
}
