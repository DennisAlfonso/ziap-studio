namespace ZiapStudio.Services.Localization;

/// <summary>
/// Maps a localization namespace used in content to its path below locales/&lt;locale&gt;.
/// The conventional namespace.json layout remains the fallback so new namespaces need
/// no registration unless their file lives in a subdirectory.
/// </summary>
public sealed class LocalizationNamespaceRegistry
{
    private readonly IReadOnlyDictionary<string, string> _sourceFiles;

    public LocalizationNamespaceRegistry(IEnumerable<KeyValuePair<string, string>>? sourceFiles = null)
    {
        var entries = sourceFiles ?? [];
        _sourceFiles = entries.ToDictionary(
            pair => pair.Key,
            pair => NormalizeRelativePath(pair.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    public static LocalizationNamespaceRegistry Fusion { get; } = new(
    [
        new KeyValuePair<string, string>("mdv", "dialogue/mdv.json"),
    ]);

    public string GetSourceFile(string namespaceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        return _sourceFiles.TryGetValue(namespaceName, out var sourceFile)
            ? sourceFile
            : $"{namespaceName}.json";
    }

    private static string NormalizeRelativePath(string sourceFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        var normalized = sourceFile.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Contains(':'))
        {
            throw new ArgumentException("A localization source file must be a relative path.", nameof(sourceFile));
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new ArgumentException("A localization source file contains an unsafe path segment.", nameof(sourceFile));
        }

        return string.Join('/', parts);
    }
}
