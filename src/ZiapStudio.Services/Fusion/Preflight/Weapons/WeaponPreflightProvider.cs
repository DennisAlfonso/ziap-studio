using System.Text.Json;
using ZiapStudio.Core.Fusion.Weapons;
using ZiapStudio.Core.Models;
using ZiapStudio.Core.Preflight;
using ZiapStudio.Services.Fusion.Weapons;

namespace ZiapStudio.Services.Fusion.Preflight.Weapons;

public sealed class WeaponPreflightProvider : IPreflightProvider
{
    private readonly FileSystemService _fileSystem;
    private readonly WeaponNotetagCatalogProvider _catalogProvider;
    private readonly WeaponAdvancedMetadataProvider _metadataProvider;
    private readonly IReadOnlyList<IPreflightRule<WeaponPreflightContext>> _rules;

    public WeaponPreflightProvider(
        FileSystemService fileSystem,
        WeaponNotetagCatalogProvider catalogProvider,
        WeaponAdvancedMetadataProvider? metadataProvider = null)
    {
        _fileSystem = fileSystem;
        _catalogProvider = catalogProvider;
        _metadataProvider = metadataProvider ?? new WeaponAdvancedMetadataProvider();
        _rules = WeaponPreflightProfile.CreateRules();
    }

    public string Scope => "Weapons";

    public async Task<IReadOnlyList<PreflightIssue>> ScanAsync(
        ZiapProject project,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(project.Path, "data", "Weapons.json");
        if (!_fileSystem.FileExists(path))
        {
            return [];
        }

        var catalog = await _catalogProvider.LoadAsync(project, cancellationToken);
        var bytes = await _fileSystem.ReadAllBytesAsync(path, cancellationToken);
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Weapons.json deve contenere un array JSON.");
        }

        var issues = new List<PreflightIssue>();
        var index = 0;
        foreach (var record in document.RootElement.EnumerateArray())
        {
            if (record.ValueKind != JsonValueKind.Object ||
                !TryReadInteger(record, "wtypeId", out var weaponTypeId) || weaponTypeId == 0)
            {
                index++;
                continue;
            }

            var id = TryReadInteger(record, "id", out var recordId) ? recordId : index;
            var name = record.TryGetProperty("name", out var nameProperty) &&
                nameProperty.ValueKind == JsonValueKind.String
                    ? nameProperty.GetString() ?? string.Empty
                    : string.Empty;
            var note = record.TryGetProperty("note", out var noteProperty) &&
                noteProperty.ValueKind == JsonValueKind.String
                    ? noteProperty.GetString() ?? string.Empty
                    : string.Empty;
            var context = new WeaponPreflightContext(
                id,
                string.IsNullOrWhiteSpace(name) ? $"Arma #{id}" : name,
                _metadataProvider.Parse(note, catalog),
                catalog);
            foreach (var rule in _rules)
            {
                issues.AddRange(rule.Evaluate(context));
            }

            index++;
        }

        return issues;
    }

    private static bool TryReadInteger(JsonElement record, string propertyName, out int value)
    {
        value = 0;
        return record.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }
}

public sealed record WeaponPreflightContext(
    int RecordId,
    string RecordName,
    WeaponAdvancedMetadata Metadata,
    WeaponNotetagCatalog Catalog)
{
    public PreflightIssue CreateIssue(
        string ruleId,
        PreflightSeverity severity,
        string message) => new()
    {
        RuleId = ruleId,
        Scope = "Weapons",
        RecordId = RecordId,
        RecordName = RecordName,
        Severity = severity,
        Message = message,
        NavigationTarget = new Uri($"rpgmaker://database/weapons/{RecordId}"),
    };
}
