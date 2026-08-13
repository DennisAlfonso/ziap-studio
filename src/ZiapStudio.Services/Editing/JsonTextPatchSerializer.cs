using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ZiapStudio.Core.Editing;

namespace ZiapStudio.Services.Editing;

public sealed class JsonTextPatchSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private readonly FileSystemService _fileSystem;

    public JsonTextPatchSerializer(FileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<byte[]> SerializeAsync(
        DocumentEditSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var sourceBytes = await _fileSystem.ReadAllBytesAsync(
            session.SourceSnapshot.SourcePath,
            cancellationToken);
        var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes));
        if (!string.Equals(
            sourceHash,
            session.SourceSnapshot.ContentHash,
            StringComparison.Ordinal))
        {
            throw new ExternalDocumentModificationException(
                $"{Path.GetFileName(session.SourceSnapshot.SourcePath)} è stato modificato esternamente.");
        }

        var requestedChanges = session.ChangeSet.Changes
            .Select(CreateRequestedChange)
            .ToDictionary(change => change.Key);
        if (requestedChanges.Count == 0)
        {
            return sourceBytes;
        }

        var patches = LocatePatches(sourceBytes, requestedChanges);
        if (patches.Count != requestedChanges.Count)
        {
            var missing = requestedChanges.Keys.Except(patches.Select(patch => patch.Key));
            throw new JsonException(
                $"Impossibile individuare nel JSON: {string.Join(", ", missing)}.");
        }

        using var output = new MemoryStream(sourceBytes.Length + 256);
        var cursor = 0;
        foreach (var patch in patches.OrderBy(patch => patch.Start))
        {
            output.Write(sourceBytes, cursor, patch.Start - cursor);
            output.Write(patch.Replacement);
            cursor = patch.End;
        }

        output.Write(sourceBytes, cursor, sourceBytes.Length - cursor);
        return output.ToArray();
    }

    private static RequestedChange CreateRequestedChange(DocumentChange change)
    {
        if (!change.NewValueExists)
        {
            throw new JsonException(
                "La rimozione di proprietà non è ancora supportata dal writer conservativo.");
        }

        var targetSegments = new Uri(change.Target).AbsolutePath.Trim('/').Split('/');
        if (targetSegments.Length < 2 ||
            !int.TryParse(targetSegments[^1], out var recordId) ||
            !TryParsePropertyPath(change.PropertyPath, out var propertyName, out var arrayIndex))
        {
            throw new JsonException($"ChangeSet non supportato: {change.Target} {change.PropertyPath}.");
        }

        var json = change.NewValue?.ToJsonString(JsonOptions) ?? "null";
        return new RequestedChange(
            new ChangeKey(recordId, propertyName, arrayIndex),
            Encoding.UTF8.GetBytes(json));
    }

    private static IReadOnlyList<JsonPatch> LocatePatches(
        byte[] sourceBytes,
        IReadOnlyDictionary<ChangeKey, RequestedChange> requestedChanges)
    {
        var patches = new List<JsonPatch>();
        var reader = new Utf8JsonReader(sourceBytes);
        var rootElementIndex = -1;
        int? currentRecordIndex = null;

        while (reader.Read())
        {
            if (reader.CurrentDepth == 1 &&
                reader.TokenType is JsonTokenType.Null or JsonTokenType.StartObject)
            {
                rootElementIndex++;
                currentRecordIndex = reader.TokenType == JsonTokenType.StartObject
                    ? rootElementIndex
                    : null;
                continue;
            }

            if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.EndObject)
            {
                currentRecordIndex = null;
                continue;
            }

            if (currentRecordIndex is null ||
                reader.CurrentDepth != 2 ||
                reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var propertyName = reader.GetString() ?? string.Empty;
            if (!reader.Read())
            {
                throw new JsonException("Valore JSON mancante dopo il nome della proprietà.");
            }

            var scalarKey = new ChangeKey(currentRecordIndex.Value, propertyName, null);
            if (requestedChanges.TryGetValue(scalarKey, out var scalarChange))
            {
                AddScalarPatch(reader, scalarChange, patches);
                continue;
            }

            if (reader.TokenType != JsonTokenType.StartArray ||
                !requestedChanges.Keys.Any(key =>
                    key.RecordId == currentRecordIndex.Value &&
                    string.Equals(key.PropertyName, propertyName, StringComparison.Ordinal) &&
                    key.ArrayIndex is not null))
            {
                continue;
            }

            var arrayIndex = -1;
            while (reader.Read())
            {
                if (reader.CurrentDepth == 2 && reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                if (reader.CurrentDepth != 3 || !IsScalar(reader.TokenType))
                {
                    continue;
                }

                arrayIndex++;
                var arrayKey = new ChangeKey(
                    currentRecordIndex.Value,
                    propertyName,
                    arrayIndex);
                if (requestedChanges.TryGetValue(arrayKey, out var arrayChange))
                {
                    AddScalarPatch(reader, arrayChange, patches);
                }
            }
        }

        return patches;
    }

    private static void AddScalarPatch(
        Utf8JsonReader reader,
        RequestedChange change,
        ICollection<JsonPatch> patches)
    {
        if (!IsScalar(reader.TokenType))
        {
            throw new JsonException($"{change.Key} non punta a un valore JSON scalare.");
        }

        patches.Add(new JsonPatch(
            change.Key,
            checked((int)reader.TokenStartIndex),
            checked((int)reader.BytesConsumed),
            change.Replacement));
    }

    private static bool IsScalar(JsonTokenType tokenType) => tokenType is
        JsonTokenType.String or
        JsonTokenType.Number or
        JsonTokenType.True or
        JsonTokenType.False or
        JsonTokenType.Null;

    private static bool TryParsePropertyPath(
        string propertyPath,
        out string propertyName,
        out int? arrayIndex)
    {
        propertyName = propertyPath;
        arrayIndex = null;
        var bracket = propertyPath.IndexOf('[');
        if (bracket < 0)
        {
            return !string.IsNullOrWhiteSpace(propertyName);
        }

        if (!propertyPath.EndsWith(']') ||
            propertyPath.IndexOf('[', bracket + 1) >= 0 ||
            !int.TryParse(propertyPath.AsSpan(bracket + 1, propertyPath.Length - bracket - 2), out var index))
        {
            return false;
        }

        propertyName = propertyPath[..bracket];
        arrayIndex = index;
        return !string.IsNullOrWhiteSpace(propertyName) && index >= 0;
    }

    private readonly record struct ChangeKey(
        int RecordId,
        string PropertyName,
        int? ArrayIndex);

    private sealed record RequestedChange(ChangeKey Key, byte[] Replacement);

    private sealed record JsonPatch(
        ChangeKey Key,
        int Start,
        int End,
        byte[] Replacement);
}
