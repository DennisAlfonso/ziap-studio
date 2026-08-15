using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Fusion.Weapons;

namespace ZiapStudio.Services.Documents;

public sealed class RpgMakerDatabaseDocumentProvider : IDocumentProvider
{
    private static readonly IReadOnlyDictionary<string, string> ResourceFiles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["actors"] = "Actors.json",
            ["classes"] = "Classes.json",
            ["skills"] = "Skills.json",
            ["items"] = "Items.json",
            ["weapons"] = "Weapons.json",
            ["armors"] = "Armors.json",
            ["enemies"] = "Enemies.json",
            ["troops"] = "Troops.json",
            ["states"] = "States.json",
            ["animations"] = "Animations.json",
            ["tilesets"] = "Tilesets.json",
            ["common-events"] = "CommonEvents.json",
        };

    private readonly FileSystemService _fileSystem;
    private readonly RpgMakerValueResolver _valueResolver;
    private readonly WeaponNotetagCatalogProvider _weaponNotetagCatalogProvider;

    public RpgMakerDatabaseDocumentProvider(FileSystemService fileSystem)
        : this(
            fileSystem,
            new RpgMakerValueResolver(fileSystem),
            new WeaponNotetagCatalogProvider(fileSystem))
    {
    }

    public RpgMakerDatabaseDocumentProvider(
        FileSystemService fileSystem,
        RpgMakerValueResolver valueResolver,
        WeaponNotetagCatalogProvider? weaponNotetagCatalogProvider = null)
    {
        _fileSystem = fileSystem;
        _valueResolver = valueResolver;
        _weaponNotetagCatalogProvider = weaponNotetagCatalogProvider ??
            new WeaponNotetagCatalogProvider(fileSystem);
    }

    public bool CanOpen(DocumentDescriptor descriptor) =>
        descriptor.Kind == DocumentKind.RpgMakerDatabase &&
        descriptor.ResourceId.Scheme.Equals("rpgmaker", StringComparison.OrdinalIgnoreCase) &&
        descriptor.ResourceId.Host.Equals("database", StringComparison.OrdinalIgnoreCase);

    public async Task<StudioDocument> OpenAsync(
        ZiapProject project,
        DocumentDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        var resourceName = descriptor.ResourceId.AbsolutePath.Trim('/');
        if (!ResourceFiles.TryGetValue(resourceName, out var fileName))
        {
            throw new DocumentLoadException(
                $"La risorsa RPG Maker '{descriptor.ResourceId}' non è supportata.");
        }

        var sourcePath = Path.Combine(project.Path, "data", fileName);
        if (!_fileSystem.FileExists(sourcePath))
        {
            throw new DocumentLoadException($"Il file '{fileName}' non esiste nel progetto.");
        }

        try
        {
            var sourceBytes = await _fileSystem.ReadAllBytesAsync(sourcePath, cancellationToken);
            using var jsonDocument = JsonDocument.Parse(sourceBytes);
            if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new DocumentLoadException(
                    $"Il file '{fileName}' non contiene un database RPG Maker ad elenco.");
            }

            var definition = RpgMakerDatabaseDefinitions.Get(
                resourceName,
                descriptor.DisplayName);
            var valueDefinitions = definition.Columns.Select(column => new ValueDefinition(
                    column.Key,
                    column.Presentation,
                    column.ReferenceTarget))
                .Concat(definition.Sections.SelectMany(section =>
                    section.Fields.Select(field => new ValueDefinition(
                        field.Key,
                        field.Presentation,
                        field.ReferenceTarget))))
                .GroupBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToArray();
            var recordElements = jsonDocument.RootElement.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.Object)
                .ToArray();
            var rawValues = recordElements.SelectMany(element =>
                    valueDefinitions.Select(value => ReadValue(element, value.Key)))
                .ToArray();
            var resolutionContext = await _valueResolver.CreateContextAsync(
                project,
                definition,
                rawValues,
                cancellationToken);
            var entries = recordElements
                .Select(element => CreateEntry(element, valueDefinitions, resolutionContext))
                .ToArray();
            var weaponNotetagCatalog = resourceName.Equals(
                "weapons",
                StringComparison.OrdinalIgnoreCase)
                    ? await _weaponNotetagCatalogProvider.LoadAsync(project, cancellationToken)
                    : null;

            return new RpgMakerDatabaseDocument
            {
                Descriptor = descriptor,
                SourcePath = sourcePath,
                Definition = definition,
                Entries = entries,
                SourceRoot = JsonNode.Parse(sourceBytes) ??
                    throw new DocumentLoadException($"Il file '{fileName}' è vuoto."),
                SourceSnapshot = new DocumentSourceSnapshot
                {
                    SourcePath = sourcePath,
                    LoadedAtUtc = DateTimeOffset.UtcNow,
                    LastWriteTimeUtc = _fileSystem.GetLastWriteTimeUtc(sourcePath),
                    Length = sourceBytes.LongLength,
                    ContentHash = Convert.ToHexString(SHA256.HashData(sourceBytes)),
                },
                WeaponNotetagCatalog = weaponNotetagCatalog,
            };
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new DocumentLoadException(
                $"Impossibile leggere il database RPG Maker '{fileName}'.",
                exception);
        }
    }

    private RpgMakerDatabaseEntry CreateEntry(
        JsonElement element,
        IReadOnlyList<ValueDefinition> valueDefinitions,
        RpgMakerValueResolutionContext resolutionContext)
    {
        var values = valueDefinitions.ToDictionary(
            value => value.Key,
            value => _valueResolver.Resolve(
                ReadValue(element, value.Key),
                value.Presentation,
                value.ReferenceTarget,
                resolutionContext),
            StringComparer.OrdinalIgnoreCase);

        return new RpgMakerDatabaseEntry
        {
            Id = int.TryParse(ReadValue(element, "id"), out var id) ? id : 0,
            Name = ReadValue(element, "name"),
            Values = values,
        };
    }

    private sealed record ValueDefinition(
        string Key,
        RpgMakerValuePresentation Presentation,
        string? ReferenceTarget);

    private static string ReadValue(JsonElement element, string key)
    {
        var bracketIndex = key.IndexOf('[', StringComparison.Ordinal);
        var propertyName = bracketIndex < 0 ? key : key[..bracketIndex];
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        if (bracketIndex >= 0)
        {
            var closingBracketIndex = key.IndexOf(']', bracketIndex + 1);
            if (closingBracketIndex < 0 ||
                !int.TryParse(key.AsSpan(bracketIndex + 1, closingBracketIndex - bracketIndex - 1), out var index) ||
                value.ValueKind != JsonValueKind.Array ||
                index < 0 ||
                index >= value.GetArrayLength())
            {
                return string.Empty;
            }

            value = value[index];
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "Sì",
            JsonValueKind.False => "No",
            JsonValueKind.Null => string.Empty,
            _ => value.GetRawText(),
        };
    }
}
