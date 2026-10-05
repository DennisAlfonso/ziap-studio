using System.Security.Cryptography;

namespace ZiapStudio.Services.ProjectSafety;

/// <summary>Correlates watcher notifications with a completed atomic write by path and hash.</summary>
public sealed class SelfWriteRegistry
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public SelfWriteRegistry(TimeProvider? timeProvider = null, TimeSpan? retention = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _retention = retention ?? TimeSpan.FromMinutes(2);
    }

    public string RegisterPending(string path, string expectedResultHash)
    {
        var operationId = Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            PurgeExpired();
            _entries[Path.GetFullPath(path)] = new Entry(operationId, expectedResultHash, false, _timeProvider.GetUtcNow());
        }
        return operationId;
    }

    public void MarkCommitted(string path, string operationId)
    {
        lock (_gate)
        {
            var normalized = Path.GetFullPath(path);
            if (_entries.TryGetValue(normalized, out var entry) && entry.OperationId == operationId)
            {
                _entries[normalized] = entry with { Committed = true, RecordedAt = _timeProvider.GetUtcNow() };
            }
        }
    }

    public bool IsSelfWrite(string path, string actualContentHash)
    {
        lock (_gate)
        {
            PurgeExpired();
            return _entries.TryGetValue(Path.GetFullPath(path), out var entry) &&
                CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(entry.ExpectedResultHash),
                    Convert.FromHexString(actualContentHash));
        }
    }

    private void PurgeExpired()
    {
        var minimum = _timeProvider.GetUtcNow() - _retention;
        foreach (var key in _entries.Where(pair => pair.Value.RecordedAt < minimum).Select(pair => pair.Key).ToArray())
        {
            _entries.Remove(key);
        }
    }

    private sealed record Entry(string OperationId, string ExpectedResultHash, bool Committed, DateTimeOffset RecordedAt);
}
