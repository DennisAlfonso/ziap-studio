using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZiapStudio.Core.Documents;
using ZiapStudio.Core.Localization;

namespace ZiapStudio.Services.Localization;

public sealed class FusionLocalizationProvider : ILocalizationProvider
{
    private static readonly Regex NamespacePattern = new(
        "^[a-zA-Z_][a-zA-Z0-9_-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly FileSystemService _fileSystem;
    private readonly ConcurrentDictionary<string, Task<JsonElement?>> _documents =
        new(StringComparer.OrdinalIgnoreCase);

    public FusionLocalizationProvider(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public bool CanResolve(string rawValue) => TryParseKey(rawValue, out _);

    public async Task<LocalizationResolution> ResolveAsync(
        string projectPath,
        string locale,
        string rawValue,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseKey(rawValue, out var key))
        {
            return CreateMissing(rawValue, rawValue, locale, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var origin = new LocalizationReferenceOrigin
        {
            Namespace = key.Namespace,
            Path = key.FocusPath,
            Locale = locale,
            SourceFile = $"{key.Namespace}.json",
        };
        var path = Path.Combine(projectPath, "locales", locale, origin.SourceFile);
        var document = await _documents.GetOrAdd(
            path,
            candidatePath => LoadDocumentAsync(candidatePath));
        if (document is null || !TryNavigate(document.Value, key.Path, out var value))
        {
            return CreateMissing(rawValue, key.Target, locale, origin);
        }

        return new LocalizationResolution
        {
            RawValue = rawValue,
            ResolvedValue = value,
            Target = key.Target,
            Locale = locale,
            Origin = origin,
            Status = RpgMakerResolutionStatus.Resolved,
        };
    }

    private async Task<JsonElement?> LoadDocumentAsync(string path)
    {
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            var json = await _fileSystem.ReadAllTextAsync(path);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryNavigate(
        JsonElement root,
        IReadOnlyList<LocalizationPathSegment> path,
        out string value)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (segment.ArrayIndex is int index)
            {
                if (current.ValueKind != JsonValueKind.Array ||
                    index < 0 ||
                    index >= current.GetArrayLength())
                {
                    value = string.Empty;
                    return false;
                }

                current = current[index];
            }
            else if (segment.PropertyName is string propertyName)
            {
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(propertyName, out current))
                {
                    value = string.Empty;
                    return false;
                }
            }
        }

        value = current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => current.GetRawText(),
            _ => string.Empty,
        };
        return current.ValueKind is JsonValueKind.String or JsonValueKind.Number or
            JsonValueKind.True or JsonValueKind.False;
    }

    private static bool TryParseKey(string rawValue, out LocalizationKey key)
    {
        var trimmed = rawValue.Trim();
        if (trimmed.Length < 4 || trimmed[0] != '{' || trimmed[^1] != '}')
        {
            key = default;
            return false;
        }

        var target = trimmed[1..^1];
        var namespaceEnd = target.IndexOfAny(['[', '.']);
        if (namespaceEnd <= 0)
        {
            key = default;
            return false;
        }

        var namespaceName = target[..namespaceEnd];
        if (!NamespacePattern.IsMatch(namespaceName) ||
            !TryParsePath(target.AsSpan(namespaceEnd), out var path))
        {
            key = default;
            return false;
        }

        key = new LocalizationKey(namespaceName, target, CreateFocusPath(path), path);
        return true;
    }

    private static string CreateFocusPath(IEnumerable<LocalizationPathSegment> path) =>
        string.Join(
            '.',
            path.Select(segment => segment.ArrayIndex?.ToString() ?? segment.PropertyName));

    private static bool TryParsePath(
        ReadOnlySpan<char> pathText,
        out IReadOnlyList<LocalizationPathSegment> path)
    {
        var segments = new List<LocalizationPathSegment>();
        var position = 0;
        while (position < pathText.Length)
        {
            if (pathText[position] == '.')
            {
                var start = ++position;
                while (position < pathText.Length && pathText[position] is not '.' and not '[')
                {
                    position++;
                }

                if (position == start)
                {
                    path = [];
                    return false;
                }

                segments.Add(new LocalizationPathSegment(
                    pathText[start..position].ToString(),
                    null));
            }
            else if (pathText[position] == '[')
            {
                var closingBracket = pathText[position..].IndexOf(']');
                if (closingBracket <= 1 ||
                    !int.TryParse(pathText.Slice(position + 1, closingBracket - 1), out var index))
                {
                    path = [];
                    return false;
                }

                segments.Add(new LocalizationPathSegment(null, index));
                position += closingBracket + 1;
            }
            else
            {
                path = [];
                return false;
            }
        }

        path = segments;
        return segments.Count > 0;
    }

    private static LocalizationResolution CreateMissing(
        string rawValue,
        string target,
        string locale,
        LocalizationReferenceOrigin? origin) => new()
    {
        RawValue = rawValue,
        Target = target,
        Locale = locale,
        Origin = origin,
        Status = RpgMakerResolutionStatus.MissingTarget,
    };

    private readonly record struct LocalizationKey(
        string Namespace,
        string Target,
        string FocusPath,
        IReadOnlyList<LocalizationPathSegment> Path);

    private readonly record struct LocalizationPathSegment(
        string? PropertyName,
        int? ArrayIndex);
}
