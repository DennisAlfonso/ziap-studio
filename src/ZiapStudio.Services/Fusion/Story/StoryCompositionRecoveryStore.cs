using System.Text.Json;
using ZiapStudio.Core.Fusion.Story;

namespace ZiapStudio.Services.Fusion.Story;

/// <summary>Durable local journal; intentionally stored outside a game repository.</summary>
public sealed class StoryCompositionRecoveryStore
{
    private readonly FileSystemService _fileSystem;
    private readonly string _journalPath;

    public StoryCompositionRecoveryStore(FileSystemService fileSystem, string journalPath)
    {
        _fileSystem = fileSystem;
        _journalPath = journalPath;
    }

    public async Task<IReadOnlyList<StoryCompositionRecoveryRecord>> LoadAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        var records = await ReadAllAsync(cancellationToken);
        return records.Where(record => record.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
            .Where(record => record.State is not StoryCompositionState.Completed and not StoryCompositionState.Dismissed)
            .ToArray();
    }

    public async Task UpsertAsync(StoryCompositionRecoveryRecord record, CancellationToken cancellationToken = default)
    {
        var records = (await ReadAllAsync(cancellationToken)).ToList();
        var index = records.FindIndex(candidate => candidate.Plan.OperationId == record.Plan.OperationId);
        if (index >= 0) records[index] = record; else records.Add(record);
        await WriteAllAsync(records, cancellationToken);
    }

    public async Task MarkCompletedAsync(string operationId, CancellationToken cancellationToken = default)
    {
        var records = (await ReadAllAsync(cancellationToken)).ToList();
        var index = records.FindIndex(record => record.Plan.OperationId == operationId);
        if (index < 0) return;
        records[index] = records[index] with { State = StoryCompositionState.Completed, UpdatedAt = DateTimeOffset.UtcNow };
        await WriteAllAsync(records, cancellationToken);
    }

    public async Task DismissAsync(string operationId, CancellationToken cancellationToken = default)
    {
        var records = (await ReadAllAsync(cancellationToken)).ToList();
        var index = records.FindIndex(record => record.Plan.OperationId == operationId);
        if (index < 0) return;
        records[index] = records[index] with { State = StoryCompositionState.Dismissed, UpdatedAt = DateTimeOffset.UtcNow };
        await WriteAllAsync(records, cancellationToken);
    }

    private async Task<IReadOnlyList<StoryCompositionRecoveryRecord>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(_journalPath)) return [];
        try
        {
            await using var stream = File.OpenRead(_journalPath);
            return await JsonSerializer.DeserializeAsync<List<StoryCompositionRecoveryRecord>>(
                stream, cancellationToken: cancellationToken) ?? [];
        }
        catch (JsonException exception)
        {
            throw new StoryCompositionRecoveryException("Il recovery journal Story è corrotto.", exception);
        }
    }

    private async Task WriteAllAsync(IReadOnlyList<StoryCompositionRecoveryRecord> records, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_journalPath) ?? throw new InvalidOperationException("Journal path non valido.");
        _fileSystem.CreateDirectory(directory);
        var temporary = _journalPath + ".ziap-tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(records, new JsonSerializerOptions { WriteIndented = true });
            await _fileSystem.WriteAllBytesWithFlushAsync(temporary, bytes, cancellationToken);
            _fileSystem.MoveFile(temporary, _journalPath, overwrite: true);
        }
        finally
        {
            if (_fileSystem.FileExists(temporary)) _fileSystem.DeleteFile(temporary);
        }
    }
}

public sealed class StoryCompositionRecoveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);
