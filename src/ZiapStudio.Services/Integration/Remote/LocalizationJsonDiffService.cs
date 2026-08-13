using System.Text.Json;
using ZiapStudio.Core.Localization;

namespace ZiapStudio.Services.Integration.Remote;

public sealed class LocalizationJsonDiffService
{
    private const int MaximumDisplayedValueLength = 500;

    public IReadOnlyList<LocalizationJsonDifference> Compare(
        ReadOnlyMemory<byte>? localContents,
        ReadOnlyMemory<byte> publishedContents)
    {
        var local = localContents.HasValue
            ? Flatten(localContents.Value)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var published = Flatten(publishedContents);
        var paths = local.Keys
            .Concat(published.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var differences = new List<LocalizationJsonDifference>();
        foreach (var path in paths)
        {
            var hasLocal = local.TryGetValue(path, out var localValue);
            var hasPublished = published.TryGetValue(path, out var publishedValue);
            if (hasLocal && hasPublished && localValue == publishedValue)
            {
                continue;
            }

            differences.Add(new LocalizationJsonDifference
            {
                Path = path,
                Kind = !hasLocal
                    ? LocalizationDifferenceKind.OnlyPublished
                    : !hasPublished
                        ? LocalizationDifferenceKind.OnlyLocal
                        : LocalizationDifferenceKind.Modified,
                LocalValue = hasLocal ? TrimDisplayValue(localValue!) : null,
                PublishedValue = hasPublished ? TrimDisplayValue(publishedValue!) : null,
            });
        }
        return differences;
    }

    private static Dictionary<string, string> Flatten(ReadOnlyMemory<byte> contents)
    {
        try
        {
            using var document = JsonDocument.Parse(contents);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            AddElement(result, "$", document.RootElement);
            return result;
        }
        catch (JsonException exception)
        {
            throw new RemoteLocalizationException(
                "Il confronto richiede file JSON validi.",
                exception);
        }
    }

    private static void AddElement(
        IDictionary<string, string> result,
        string path,
        JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var properties = element.EnumerateObject().ToArray();
            if (properties.Length == 0)
            {
                result[path] = "{}";
                return;
            }
            foreach (var property in properties)
            {
                AddElement(
                    result,
                    path == "$" ? property.Name : $"{path}.{property.Name}",
                    property.Value);
            }
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var items = element.EnumerateArray().ToArray();
            if (items.Length == 0)
            {
                result[path] = "[]";
                return;
            }
            for (var index = 0; index < items.Length; index++)
            {
                AddElement(result, $"{path}[{index}]", items[index]);
            }
            return;
        }

        result[path] = element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : element.GetRawText();
    }

    private static string TrimDisplayValue(string value) =>
        value.Length <= MaximumDisplayedValueLength
            ? value
            : $"{value[..MaximumDisplayedValueLength]}…";
}
