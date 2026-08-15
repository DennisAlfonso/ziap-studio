using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ZiapStudio.Core.Preflight;
using ZiapStudio.Services.Editing;

namespace ZiapStudio.Services.Fusion.Preflight;

public sealed class PreflightSuppressionStore
{
    private const string MetadataRelativePath = ".ziap/project.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly FileSystemService _fileSystem;
    private readonly AtomicJsonFileWriter _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PreflightSuppressionStore(FileSystemService fileSystem, AtomicJsonFileWriter writer)
    {
        _fileSystem = fileSystem;
        _writer = writer;
    }

    public async Task<IReadOnlyList<PreflightSuppression>> LoadAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var path = GetMetadataPath(projectPath);
        if (!_fileSystem.FileExists(path))
        {
            return [];
        }

        var bytes = await _fileSystem.ReadAllBytesAsync(path, cancellationToken);
        var root = JsonNode.Parse(bytes) as JsonObject;
        if (root?["preflight"]?["suppressions"] is not JsonArray suppressions)
        {
            return [];
        }

        return suppressions.Deserialize<List<PreflightSuppression>>(SerializerOptions) ?? [];
    }

    public Task IgnoreAsync(
        string projectPath,
        PreflightSuppression suppression,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            projectPath,
            current =>
            {
                current.RemoveAll(candidate => candidate.Identity.Matches(suppression.Identity));
                current.Add(suppression);
            },
            cancellationToken);

    public Task RestoreAsync(
        string projectPath,
        PreflightIssueIdentity identity,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            projectPath,
            current => current.RemoveAll(candidate => candidate.Identity.Matches(identity)),
            cancellationToken);

    public Task RestoreAllAsync(
        string projectPath,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(projectPath, current => current.Clear(), cancellationToken);

    public Task RemoveObsoleteAsync(
        string projectPath,
        IReadOnlyCollection<PreflightSuppression> obsolete,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            projectPath,
            current => current.RemoveAll(candidate => obsolete.Any(item =>
                item.Identity.Matches(candidate.Identity))),
            cancellationToken);

    private async Task UpdateAsync(
        string projectPath,
        Action<List<PreflightSuppression>> update,
        CancellationToken cancellationToken)
    {
        var path = GetMetadataPath(projectPath);
        if (!_fileSystem.FileExists(path))
        {
            throw new InvalidOperationException(
                "Il progetto deve essere inizializzato prima di salvare le eccezioni Pre-Flight.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var originalBytes = await _fileSystem.ReadAllBytesAsync(path, cancellationToken);
            var root = JsonNode.Parse(originalBytes) as JsonObject ??
                throw new JsonException("Il metadata .ziap/project.json non contiene un oggetto JSON.");
            var preflight = root["preflight"] as JsonObject;
            if (preflight is null)
            {
                preflight = new JsonObject();
                root["preflight"] = preflight;
            }

            var current = preflight["suppressions"] is JsonArray array
                ? array.Deserialize<List<PreflightSuppression>>(SerializerOptions) ?? []
                : [];
            update(current);
            preflight["suppressions"] = JsonSerializer.SerializeToNode(current, SerializerOptions);

            var updatedBytes = Encoding.UTF8.GetBytes(root.ToJsonString(SerializerOptions) + Environment.NewLine);
            var expectedHash = Convert.ToHexString(SHA256.HashData(originalBytes));
            await _writer.WriteAsync(path, updatedBytes, expectedHash, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string GetMetadataPath(string projectPath) =>
        Path.Combine(projectPath, MetadataRelativePath);
}
