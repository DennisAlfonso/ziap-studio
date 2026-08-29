using System.Text.Json;
using System.Text.RegularExpressions;
using ZiapStudio.Core.Fusion.Weapons;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Fusion.Weapons;

public sealed partial class WeaponNotetagCatalogProvider
{
    private static readonly IReadOnlyDictionary<string, string> LegacyLoreAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["viaggiamondiDistrutta"] = "1",
            ["spadaAscendente"] = "2",
            ["precursore"] = "3",
        };

    private readonly FileSystemService _fileSystem;
    private readonly LocalizationService _localizationService;

    public WeaponNotetagCatalogProvider(FileSystemService fileSystem)
        : this(
            fileSystem,
            new LocalizationService([new FusionLocalizationProvider(fileSystem)]))
    {
    }

    public WeaponNotetagCatalogProvider(
        FileSystemService fileSystem,
        LocalizationService localizationService)
    {
        _fileSystem = fileSystem;
        _localizationService = localizationService;
    }

    public async Task<WeaponNotetagCatalog> LoadAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var perks = await LoadPerksAsync(project.Path, cancellationToken);
        var lore = await LoadLoreAsync(project.Path, cancellationToken);
        var resources = await LoadDisassemblyResourcesAsync(project.Path, cancellationToken);
        var attackSkills = await LoadAttackSkillsAsync(project.Path, cancellationToken);
        var systemOptions = await LoadSystemOptionsAsync(project.Path, cancellationToken);
        return new WeaponNotetagCatalog
        {
            Perks = perks,
            LoreEntries = lore,
            DisassemblyResources = resources,
            AttackSkills = attackSkills,
            WeaponTypes = systemOptions.WeaponTypes,
            EquipTypes = systemOptions.EquipTypes,
            Elements = systemOptions.Elements,
            CustomParameters = [new WeaponCustomParameterOption(1, "Maestria Hex")],
        };
    }

    private async Task<IReadOnlyList<WeaponAttackSkillOption>> LoadAttackSkillsAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(projectPath, "data", "Skills.json");
        if (!_fileSystem.FileExists(path))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(
                await _fileSystem.ReadAllTextAsync(path, cancellationToken));
            var skills = new List<WeaponAttackSkillOption>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("id", out var idNode) || !idNode.TryGetInt32(out var id) ||
                    !element.TryGetProperty("name", out var nameNode) || nameNode.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var note = element.TryGetProperty("note", out var noteNode) &&
                    noteNode.ValueKind == JsonValueKind.String
                        ? noteNode.GetString() ?? string.Empty
                        : string.Empty;
                if (!note.Contains("<ABS>", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rawName = nameNode.GetString() ?? $"Abilità {id}";
                var resolution = await _localizationService.ResolveAsync(
                    projectPath,
                    rawName,
                    cancellationToken: cancellationToken);
                skills.Add(new WeaponAttackSkillOption(
                    id,
                    resolution?.ResolvedValue ?? rawName,
                    ReadAbsNumber(note, "reloadTime"),
                    ReadAbsNumber(note, "range"),
                    ReadAbsNumber(note, "radius"),
                    ReadAbsNumber(note, "speed"),
                    ReadAbsNumber(note, "colliderRadius")));
            }

            return skills.OrderBy(skill => skill.Id).ToArray();
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async Task<WeaponSystemOptions> LoadSystemOptionsAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(projectPath, "data", "System.json");
        if (!_fileSystem.FileExists(path))
        {
            return new([], [], []);
        }

        try
        {
            using var document = JsonDocument.Parse(
                await _fileSystem.ReadAllTextAsync(path, cancellationToken));
            return new WeaponSystemOptions(
                await ReadSystemArrayAsync(document.RootElement, "weaponTypes", projectPath, cancellationToken),
                await ReadSystemArrayAsync(document.RootElement, "equipTypes", projectPath, cancellationToken),
                await ReadSystemArrayAsync(document.RootElement, "elements", projectPath, cancellationToken));
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new([], [], []);
        }
    }

    private async Task<IReadOnlyList<WeaponDatabaseOption>> ReadSystemArrayAsync(
        JsonElement root,
        string propertyName,
        string projectPath,
        CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var options = new List<WeaponDatabaseOption>();
        var id = 0;
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()))
            {
                var raw = element.GetString()!;
                var resolution = await _localizationService.ResolveAsync(
                    projectPath,
                    raw,
                    cancellationToken: cancellationToken);
                options.Add(new WeaponDatabaseOption(id, resolution?.ResolvedValue ?? raw));
            }

            id++;
        }

        return options;
    }

    private static double? ReadAbsNumber(string note, string key)
    {
        var match = Regex.Match(
            note,
            $@"(?im)^\s*{Regex.Escape(key)}\s*:\s*(?<value>[+-]?(?:\d+(?:\.\d+)?|\.\d+))\s*$",
            RegexOptions.CultureInvariant);
        return match.Success && double.TryParse(
            match.Groups["value"].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
                ? value
                : null;
    }

    private sealed record WeaponSystemOptions(
        IReadOnlyList<WeaponDatabaseOption> WeaponTypes,
        IReadOnlyList<WeaponDatabaseOption> EquipTypes,
        IReadOnlyList<WeaponDatabaseOption> Elements);

    public static bool TryResolveLegacyLoreAlias(string rawValue, out int id)
    {
        id = 0;
        return LegacyLoreAliases.TryGetValue(rawValue, out var value) &&
            int.TryParse(value, out id);
    }

    private async Task<IReadOnlyList<WeaponPerkOption>> LoadPerksAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        var defaults = WeaponNotetagCatalog.Empty.Perks.ToList();
        var path = Path.Combine(
            projectPath,
            "js",
            "plugins",
            "zenkaiDevPlugins",
            "ZDP_WeaponPerks.js");
        if (!_fileSystem.FileExists(path))
        {
            return defaults;
        }

        try
        {
            var source = BlockCommentRegex().Replace(
                await _fileSystem.ReadAllTextAsync(path, cancellationToken),
                string.Empty);
            for (var column = 1; column <= 3; column++)
            {
                var startMarker = $"id: \"colonna{column}\"";
                var start = source.IndexOf(startMarker, StringComparison.Ordinal);
                if (start < 0)
                {
                    continue;
                }

                var end = column < 3
                    ? source.IndexOf($"id: \"colonna{column + 1}\"", start, StringComparison.Ordinal)
                    : source.IndexOf("// Funzioni di utilità", start, StringComparison.Ordinal);
                if (end < 0)
                {
                    end = source.Length;
                }

                var section = source[start..end];
                foreach (Match match in PerkEntryRegex().Matches(section))
                {
                    var id = match.Groups["id"].Value;
                    if (id.StartsWith("colonna", StringComparison.OrdinalIgnoreCase) ||
                        defaults.Any(option => option.Column == column &&
                            option.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var rawName = match.Groups["name"].Value;
                    var resolution = await _localizationService.ResolveAsync(
                        projectPath,
                        rawName,
                        cancellationToken: cancellationToken);
                    var displayName = resolution?.ResolvedValue;
                    defaults.Add(new WeaponPerkOption(
                        id,
                        string.IsNullOrWhiteSpace(displayName)
                            ? HumanizeIdentifier(id)
                            : displayName,
                        column,
                        int.TryParse(match.Groups["level"].Value, out var level)
                            ? level
                            : column switch
                            {
                                1 => 5,
                                2 => 15,
                                _ => 30,
                            }));
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return defaults;
        }

        return defaults
            .OrderBy(option => option.Column)
            .ThenBy(option => option.Id == "random" ? 0 : option.Id == "nullo" ? 1 : 2)
            .ThenBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<WeaponLoreOption>> LoadLoreAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            Path.Combine(projectPath, "locales", "it", "books.json"),
            Path.Combine(projectPath, "lang", "books.json"),
        };
        var path = candidates.FirstOrDefault(_fileSystem.FileExists);
        if (path is null)
        {
            return [];
        }

        try
        {
            var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("library", out var library) ||
                !library.TryGetProperty("books", out var books) ||
                books.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var entries = new List<WeaponLoreOption>();
            foreach (var property in books.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object ||
                    !property.Value.TryGetProperty("category", out var category) ||
                    !category.TryGetInt32(out var categoryId) ||
                    categoryId != 0 ||
                    !property.Value.TryGetProperty("id", out var idElement) ||
                    !idElement.TryGetInt32(out var id))
                {
                    continue;
                }

                var title = property.Value.TryGetProperty("title", out var titleElement) &&
                    titleElement.ValueKind == JsonValueKind.String
                        ? titleElement.GetString() ?? property.Name
                        : property.Name;
                entries.Add(new WeaponLoreOption(property.Name, title, id));
            }

            return entries.OrderBy(entry => entry.Id).ToArray();
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<WeaponDisassemblyResourceOption>> LoadDisassemblyResourcesAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        var entries = new List<WeaponDisassemblyResourceOption>();
        foreach (var fileName in new[] { "Items.json", "Weapons.json", "Armors.json" })
        {
            var path = Path.Combine(projectPath, "data", fileName);
            if (!_fileSystem.FileExists(path))
            {
                continue;
            }

            try
            {
                var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object ||
                        !element.TryGetProperty("name", out var nameElement) ||
                        nameElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var raw = nameElement.GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    var resolution = await _localizationService.ResolveAsync(
                        projectPath,
                        raw,
                        cancellationToken: cancellationToken);
                    entries.Add(new WeaponDisassemblyResourceOption(
                        raw,
                        resolution?.ResolvedValue ?? raw));
                }
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException)
            {
                // Un catalogo opzionale non deve impedire l'apertura del database.
            }
        }

        return entries
            .DistinctBy(option => option.RawValue, StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string HumanizeIdentifier(string id)
    {
        var spaced = IdentifierBoundaryRegex().Replace(id, " $1");
        return spaced.Length == 0
            ? id
            : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    [GeneratedRegex(
        "id\\s*:\\s*\\\"(?<id>[^\\\"]+)\\\"[\\s\\S]*?name\\s*:\\s*\\\"(?<name>[^\\\"]+)\\\"[\\s\\S]*?level\\s*:\\s*(?<level>\\d+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PerkEntryRegex();

    [GeneratedRegex("([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierBoundaryRegex();

    [GeneratedRegex(@"/\*[\s\S]*?\*/", RegexOptions.CultureInvariant)]
    private static partial Regex BlockCommentRegex();
}
