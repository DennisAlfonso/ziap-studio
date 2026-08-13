using System.Text.Json;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Models;
using ZiapStudio.Services.Localization;

namespace ZiapStudio.Services.Documents;

public sealed class RpgMakerValueResolver
{
    private static readonly IReadOnlyDictionary<string, string> DatabaseReferenceFiles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["classes"] = "Classes.json",
            ["animations"] = "Animations.json",
            ["skills"] = "Skills.json",
            ["states"] = "States.json",
            ["weapons"] = "Weapons.json",
            ["armors"] = "Armors.json",
            ["items"] = "Items.json",
        };

    private readonly FileSystemService _fileSystem;
    private readonly LocalizationService _localizationService;

    public RpgMakerValueResolver(FileSystemService fileSystem)
        : this(
            fileSystem,
            new LocalizationService(
            [
                new FusionLocalizationProvider(fileSystem),
            ]))
    {
    }

    public RpgMakerValueResolver(
        FileSystemService fileSystem,
        LocalizationService localizationService)
    {
        _fileSystem = fileSystem;
        _localizationService = localizationService;
    }

    public async Task<RpgMakerValueResolutionContext> CreateContextAsync(
        ZiapProject project,
        RpgMakerDatabaseDefinition definition,
        IEnumerable<string> rawValues,
        CancellationToken cancellationToken = default)
    {
        var references = definition.Columns
            .Select(column => (column.Presentation, column.ReferenceTarget))
            .Concat(definition.Sections.SelectMany(section => section.Fields.Select(field =>
                (field.Presentation, field.ReferenceTarget))))
            .Where(reference =>
                reference.ReferenceTarget is not null &&
                reference.Presentation is RpgMakerValuePresentation.DatabaseReference or
                    RpgMakerValuePresentation.SystemReference)
            .Distinct()
            .ToArray();
        var catalogs = new Dictionary<string, IReadOnlyDictionary<int, string>>(
            StringComparer.OrdinalIgnoreCase);
        JsonDocument? systemDocument = null;

        try
        {
            foreach (var (presentation, referenceTarget) in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var catalogKey = CreateCatalogKey(presentation, referenceTarget!);
                if (catalogs.ContainsKey(catalogKey))
                {
                    continue;
                }

                IReadOnlyDictionary<int, string>? catalog;
                if (presentation == RpgMakerValuePresentation.SystemReference)
                {
                    if (systemDocument is null)
                    {
                        systemDocument = await TryLoadJsonDocumentAsync(
                            Path.Combine(project.Path, "data", "System.json"),
                            cancellationToken);
                    }

                    catalog = ReadSystemCatalog(systemDocument, referenceTarget!);
                }
                else
                {
                    catalog = await LoadDatabaseCatalogAsync(
                        project.Path,
                        referenceTarget!,
                        cancellationToken);
                }

                if (catalog is not null)
                {
                    catalogs[catalogKey] = catalog;
                }
            }
        }
        finally
        {
            systemDocument?.Dispose();
        }

        var localizations = new Dictionary<string, LocalizationResolution>(
            StringComparer.Ordinal);
        var localizationCandidates = rawValues
            .Concat(catalogs.Values.SelectMany(catalog => catalog.Values))
            .Where(value => value.TrimStart().StartsWith('{') && value.TrimEnd().EndsWith('}'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var candidate in localizationCandidates)
        {
            var localization = await _localizationService.ResolveAsync(
                project.Path,
                candidate,
                cancellationToken: cancellationToken);
            if (localization is not null)
            {
                localizations[candidate] = localization;
            }
        }

        return new RpgMakerValueResolutionContext(catalogs, localizations);
    }

    public RpgMakerResolvedValue Resolve(
        string rawValue,
        RpgMakerValuePresentation presentation,
        string? referenceTarget,
        RpgMakerValueResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var localization = presentation == RpgMakerValuePresentation.Note
            ? null
            : context.ResolveLocalization(rawValue);
        if (localization is not null)
        {
            return new RpgMakerResolvedValue
            {
                RawValue = rawValue,
                ResolvedValue = localization.ResolvedValue,
                DisplayValue = localization.ResolvedValue ?? rawValue,
                Kind = RpgMakerResolvedValueKind.LocalizationReference,
                Target = localization.Target,
                LocalizationOrigin = localization.Origin,
                Status = localization.Status,
            };
        }

        if (presentation is RpgMakerValuePresentation.DatabaseReference or
            RpgMakerValuePresentation.SystemReference)
        {
            var hasNumericId = int.TryParse(rawValue, out var id);
            var hasValidId = hasNumericId && id > 0 && referenceTarget is not null;
            var catalogValue = hasValidId
                ? context.Resolve(presentation, referenceTarget!, id)
                : null;
            var localizedCatalog = catalogValue is null
                ? null
                : context.ResolveLocalization(catalogValue);
            var resolvedValue = localizedCatalog?.ResolvedValue ?? catalogValue;
            var target = hasValidId
                ? presentation == RpgMakerValuePresentation.DatabaseReference
                    ? $"rpgmaker://database/{referenceTarget}/{id}"
                    : $"rpgmaker://system/{referenceTarget}/{id}"
                : null;
            return new RpgMakerResolvedValue
            {
                RawValue = rawValue,
                ResolvedValue = resolvedValue,
                DisplayValue = string.IsNullOrWhiteSpace(resolvedValue)
                    ? rawValue
                    : $"{rawValue} — {resolvedValue}",
                Kind = presentation == RpgMakerValuePresentation.DatabaseReference
                    ? RpgMakerResolvedValueKind.DatabaseReference
                    : RpgMakerResolvedValueKind.SystemReference,
                ReferenceTarget = referenceTarget,
                Target = target,
                LocalizationOrigin = localizedCatalog?.Origin,
                Status = !string.IsNullOrWhiteSpace(catalogValue)
                    ? RpgMakerResolutionStatus.Resolved
                    : hasValidId
                        ? RpgMakerResolutionStatus.MissingTarget
                        : hasNumericId && id == 0
                            ? RpgMakerResolutionStatus.NotApplicable
                            : RpgMakerResolutionStatus.Unresolved,
                EditorOptions = CreateEditorOptions(
                    presentation,
                    referenceTarget,
                    hasNumericId ? id : null,
                    context),
            };
        }

        return new RpgMakerResolvedValue
        {
            RawValue = rawValue,
            DisplayValue = presentation == RpgMakerValuePresentation.Text
                ? NormalizeText(rawValue)
                : rawValue,
            Kind = presentation switch
            {
                RpgMakerValuePresentation.Text => RpgMakerResolvedValueKind.Text,
                RpgMakerValuePresentation.Note => RpgMakerResolvedValueKind.Note,
                RpgMakerValuePresentation.AssetReference => RpgMakerResolvedValueKind.AssetReference,
                _ => RpgMakerResolvedValueKind.Primitive,
            },
            ReferenceTarget = referenceTarget,
            Target = presentation == RpgMakerValuePresentation.AssetReference &&
                referenceTarget is not null &&
                !string.IsNullOrWhiteSpace(rawValue)
                    ? $"rpgmaker://asset/{referenceTarget}/{Uri.EscapeDataString(rawValue)}"
                    : null,
            Status = presentation == RpgMakerValuePresentation.AssetReference &&
                !string.IsNullOrWhiteSpace(rawValue)
                    ? RpgMakerResolutionStatus.Unresolved
                    : RpgMakerResolutionStatus.NotApplicable,
        };
    }

    private async Task<IReadOnlyDictionary<int, string>?> LoadDatabaseCatalogAsync(
        string projectPath,
        string referenceTarget,
        CancellationToken cancellationToken)
    {
        if (!DatabaseReferenceFiles.TryGetValue(referenceTarget, out var fileName))
        {
            return null;
        }

        using var document = await TryLoadJsonDocumentAsync(
            Path.Combine(projectPath, "data", fileName),
            cancellationToken);
        if (document?.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new Dictionary<int, string>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty("id", out var idElement) &&
                idElement.TryGetInt32(out var id) &&
                element.TryGetProperty("name", out var nameElement) &&
                nameElement.ValueKind == JsonValueKind.String)
            {
                values[id] = nameElement.GetString() ?? string.Empty;
            }
        }

        return values;
    }

    private async Task<JsonDocument?> TryLoadJsonDocumentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken);
            return JsonDocument.Parse(json);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<int, string>? ReadSystemCatalog(
        JsonDocument? document,
        string referenceTarget)
    {
        if (document is null ||
            document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(referenceTarget, out var valuesElement) ||
            valuesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return valuesElement.EnumerateArray()
            .Select((value, index) => (value, index))
            .Where(item => item.value.ValueKind == JsonValueKind.String)
            .ToDictionary(
                item => item.index,
                item => item.value.GetString() ?? string.Empty);
    }

    private static string CreateCatalogKey(
        RpgMakerValuePresentation presentation,
        string referenceTarget) => $"{presentation}:{referenceTarget}";

    private static IReadOnlyList<RpgMakerReferenceOption> CreateEditorOptions(
        RpgMakerValuePresentation presentation,
        string? referenceTarget,
        int? currentId,
        RpgMakerValueResolutionContext context)
    {
        if (referenceTarget is null)
        {
            return [];
        }

        var options = context.GetEditorOptions(presentation, referenceTarget);

        if (currentId is > 0 && options.All(option => option.Value != currentId.Value))
        {
            return
            [
                .. options,
                new RpgMakerReferenceOption(
                    currentId.Value,
                    $"{currentId.Value} — Mancante"),
            ];
        }

        return options;
    }

    private static string NormalizeText(string rawValue)
    {
        var normalized = rawValue
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return string.Join(
            Environment.NewLine,
            normalized.Split('\n').Select(line => line.TrimEnd()))
            .Trim();
    }
}

public sealed class RpgMakerValueResolutionContext
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> _catalogs;
    private readonly IReadOnlyDictionary<string, LocalizationResolution> _localizations;
    private readonly Dictionary<string, IReadOnlyList<RpgMakerReferenceOption>> _editorOptions =
        new(StringComparer.OrdinalIgnoreCase);

    internal RpgMakerValueResolutionContext(
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> catalogs,
        IReadOnlyDictionary<string, LocalizationResolution> localizations)
    {
        _catalogs = catalogs;
        _localizations = localizations;
    }

    internal string? Resolve(
        RpgMakerValuePresentation presentation,
        string referenceTarget,
        int id)
    {
        var key = $"{presentation}:{referenceTarget}";
        return _catalogs.TryGetValue(key, out var catalog) &&
            catalog.TryGetValue(id, out var value) &&
            !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
    }

    internal IReadOnlyDictionary<int, string> GetCatalog(
        RpgMakerValuePresentation presentation,
        string referenceTarget)
    {
        var key = $"{presentation}:{referenceTarget}";
        return _catalogs.TryGetValue(key, out var catalog)
            ? catalog
            : new Dictionary<int, string>();
    }

    internal IReadOnlyList<RpgMakerReferenceOption> GetEditorOptions(
        RpgMakerValuePresentation presentation,
        string referenceTarget)
    {
        var key = $"{presentation}:{referenceTarget}";
        if (_editorOptions.TryGetValue(key, out var cachedOptions))
        {
            return cachedOptions;
        }

        var options = new List<RpgMakerReferenceOption>
        {
            new(0, "0 — Nessuna"),
        };
        foreach (var (id, rawName) in GetCatalog(presentation, referenceTarget)
            .OrderBy(item => item.Key))
        {
            if (id == 0 || string.IsNullOrWhiteSpace(rawName))
            {
                continue;
            }

            var displayName = ResolveLocalization(rawName)?.ResolvedValue ?? rawName;
            options.Add(new RpgMakerReferenceOption(id, $"{id} — {displayName}"));
        }

        _editorOptions[key] = options;
        return options;
    }

    internal LocalizationResolution? ResolveLocalization(string rawValue) =>
        _localizations.TryGetValue(rawValue, out var resolution)
            ? resolution
            : null;
}
