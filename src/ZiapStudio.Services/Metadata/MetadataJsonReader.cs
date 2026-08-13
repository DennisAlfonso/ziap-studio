using System.Text.Json;

namespace ZiapStudio.Services.Metadata;

internal static class MetadataJsonReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<T> ReadRequiredAsync<T>(
        FileSystemService fileSystem,
        string path,
        string displayName,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var json = await fileSystem.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new ProjectLoadException($"Il file {displayName} è vuoto.");
        }
        catch (JsonException exception)
        {
            throw new ProjectLoadException(
                $"Il file {displayName} non contiene JSON valido.",
                exception);
        }
        catch (IOException exception)
        {
            throw new ProjectLoadException($"Impossibile leggere {displayName}.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ProjectLoadException(
                $"Non hai i permessi necessari per leggere {displayName}.",
                exception);
        }
    }
}
