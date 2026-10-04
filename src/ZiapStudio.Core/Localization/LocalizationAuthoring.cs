using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ZiapStudio.Core.Editing;

namespace ZiapStudio.Core.Localization;

public enum LocalizationAuthoringSource
{
    Published,
    Staging,
}

public enum LocalizationAuthoringState
{
    Clean,
    LocalChanges,
    Staging,
    Conflict,
    LocalOutOfSync,
    LockedByOther,
}

public sealed record LocalizationLockInfo
{
    public string? Owner { get; init; }
    public string? AcquiredAt { get; init; }
    public string? ExpiresAt { get; init; }
    public bool IsOwnedByCurrentUser { get; init; }
}

public sealed record LocalizationAuthoringSnapshot
{
    public required string ProjectId { get; init; }
    public required string Locale { get; init; }
    public required string SourceFile { get; init; }
    public required string Content { get; init; }
    public string? CurrentVersionId { get; init; }
    public string? CurrentChecksum { get; init; }
    public string? StagingBasedOnVersionId { get; init; }
    public string? StagingChecksum { get; init; }
    public LocalizationAuthoringSource Source { get; init; }
    public LocalizationLockInfo Lock { get; init; } = new();

    public bool HasStaging => Source == LocalizationAuthoringSource.Staging;
}

public sealed record LocalizationScalarPatch
{
    public required IReadOnlyList<LocalizationPathSegment> Path { get; init; }
    public required string ExpectedOldValue { get; init; }
    public required string Value { get; init; }
}

public sealed record LocalizationEditChange
{
    public required LocalizationReferenceOrigin Origin { get; init; }
    public required string OldValue { get; init; }
    public required string NewValue { get; init; }
}

public sealed record LocalizationEditChangeSet
{
    public static LocalizationEditChangeSet Empty { get; } = new();
    public IReadOnlyList<LocalizationEditChange> Changes { get; init; } = [];
    public int Count => Changes.Count;
}

/// <summary>
/// Local, UI-independent working state for existing scalar Localization leaves.
/// It never creates paths, array entries, or non-string values.
/// </summary>
public sealed class LocalizationEditSession : INotifyPropertyChanged
{
    private readonly List<SetStringCommand> _history = [];
    private JsonNode _originalState;
    private int _historyIndex;

    public LocalizationEditSession(
        LocalizationAuthoringSnapshot snapshot,
        DocumentSourceSnapshot localSourceSnapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(localSourceSnapshot);
        Snapshot = snapshot;
        LocalSourceSnapshot = localSourceSnapshot;
        WorkingState = ParseContent(snapshot.Content);
        _originalState = WorkingState.DeepClone();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizationAuthoringSnapshot Snapshot { get; private set; }

    public DocumentSourceSnapshot LocalSourceSnapshot { get; private set; }

    public JsonNode WorkingState { get; private set; }

    public bool IsDirty => !JsonNode.DeepEquals(_originalState, WorkingState);

    public bool CanUndo => _historyIndex > 0;

    public bool CanRedo => _historyIndex < _history.Count;

    public LocalizationEditChangeSet ChangeSet => BuildChangeSet();

    public bool CanEdit(LocalizationReferenceOrigin? origin) =>
        origin is not null &&
        origin.Locale.Equals(Snapshot.Locale, StringComparison.OrdinalIgnoreCase) &&
        origin.SourceFile.Equals(Snapshot.SourceFile, StringComparison.OrdinalIgnoreCase) &&
        origin.Segments.Count > 0 &&
        TryGetString(WorkingState, origin.Segments, out _);

    public bool TryGetValue(LocalizationReferenceOrigin origin, out string value)
    {
        ArgumentNullException.ThrowIfNull(origin);
        value = string.Empty;
        return CanEdit(origin) && TryGetString(WorkingState, origin.Segments, out value);
    }

    public bool TryGetOriginalValue(LocalizationReferenceOrigin origin, out string value)
    {
        ArgumentNullException.ThrowIfNull(origin);
        value = string.Empty;
        return origin.Locale.Equals(Snapshot.Locale, StringComparison.OrdinalIgnoreCase) &&
            origin.SourceFile.Equals(Snapshot.SourceFile, StringComparison.OrdinalIgnoreCase) &&
            origin.Segments.Count > 0 && TryGetString(_originalState, origin.Segments, out value);
    }

    public bool SetValue(LocalizationReferenceOrigin origin, string value)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(value);
        if (!CanEdit(origin) || !TryGetString(WorkingState, origin.Segments, out var oldValue))
        {
            throw new InvalidOperationException("La reference Localization non punta a una stringa esistente modificabile.");
        }
        if (string.Equals(oldValue, value, StringComparison.Ordinal))
        {
            return false;
        }
        if (_historyIndex < _history.Count)
        {
            _history.RemoveRange(_historyIndex, _history.Count - _historyIndex);
        }
        var command = new SetStringCommand(origin, oldValue, value);
        WriteString(WorkingState, origin.Segments, value);
        _history.Add(command);
        _historyIndex++;
        NotifyStateChanged();
        return true;
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }
        var command = _history[--_historyIndex];
        WriteString(WorkingState, command.Origin.Segments, command.OldValue);
        NotifyStateChanged();
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }
        var command = _history[_historyIndex++];
        WriteString(WorkingState, command.Origin.Segments, command.NewValue);
        NotifyStateChanged();
        return true;
    }

    public void Discard()
    {
        WorkingState = _originalState.DeepClone();
        _history.Clear();
        _historyIndex = 0;
        OnPropertyChanged(nameof(WorkingState));
        NotifyStateChanged();
    }

    public IReadOnlyList<LocalizationScalarPatch> BuildPatches() => ChangeSet.Changes
        .Select(change => new LocalizationScalarPatch
        {
            Path = change.Origin.Segments,
            ExpectedOldValue = change.OldValue,
            Value = change.NewValue,
        })
        .ToArray();

    public void AcceptSavedSnapshot(
        LocalizationAuthoringSnapshot snapshot,
        DocumentSourceSnapshot localSourceSnapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(localSourceSnapshot);
        Snapshot = snapshot;
        LocalSourceSnapshot = localSourceSnapshot;
        WorkingState = ParseContent(snapshot.Content);
        _originalState = WorkingState.DeepClone();
        _history.Clear();
        _historyIndex = 0;
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(LocalSourceSnapshot));
        OnPropertyChanged(nameof(WorkingState));
        NotifyStateChanged();
    }

    private LocalizationEditChangeSet BuildChangeSet()
    {
        var changes = _history.Take(_historyIndex)
            .GroupBy(command => command.Origin, LocalizationOriginComparer.Ordinal)
            .Select(group => new LocalizationEditChange
            {
                Origin = group.Key,
                OldValue = group.First().OldValue,
                NewValue = group.Last().NewValue,
            })
            .Where(change => !string.Equals(change.OldValue, change.NewValue, StringComparison.Ordinal))
            .ToArray();
        return changes.Length == 0 ? LocalizationEditChangeSet.Empty : new() { Changes = changes };
    }

    private static JsonNode ParseContent(string content) => JsonNode.Parse(content)
        ?? throw new InvalidOperationException("Lo snapshot Localization non contiene JSON.");

    private static bool TryGetString(
        JsonNode root,
        IReadOnlyList<LocalizationPathSegment> segments,
        out string value)
    {
        JsonNode? current = root;
        foreach (var segment in segments)
        {
            current = segment.PropertyName is { } propertyName && current is JsonObject @object
                ? @object[propertyName]
                : segment.ArrayIndex is int index && current is JsonArray array && index >= 0 && index < array.Count
                    ? array[index]
                    : null;
            if (current is null)
            {
                value = string.Empty;
                return false;
            }
        }
        if (current is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            value = stringValue;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static void WriteString(
        JsonNode root,
        IReadOnlyList<LocalizationPathSegment> segments,
        string value)
    {
        if (segments.Count == 0)
        {
            throw new InvalidOperationException("Il path Localization non può essere vuoto.");
        }
        JsonNode? parent = root;
        foreach (var segment in segments.Take(segments.Count - 1))
        {
            parent = segment.PropertyName is { } propertyName && parent is JsonObject @object
                ? @object[propertyName]
                : segment.ArrayIndex is int index && parent is JsonArray array && index >= 0 && index < array.Count
                    ? array[index]
                    : null;
            if (parent is null)
            {
                throw new InvalidOperationException("Il path Localization non esiste più nel working state.");
            }
        }
        var last = segments[^1];
        if (last.PropertyName is { } property && parent is JsonObject objectParent &&
            objectParent[property] is JsonValue propertyValue && propertyValue.TryGetValue<string>(out _))
        {
            objectParent[property] = value;
            return;
        }
        if (last.ArrayIndex is int arrayIndex && parent is JsonArray arrayParent &&
            arrayIndex >= 0 && arrayIndex < arrayParent.Count &&
            arrayParent[arrayIndex] is JsonValue arrayValue && arrayValue.TryGetValue<string>(out _))
        {
            arrayParent[arrayIndex] = value;
            return;
        }
        throw new InvalidOperationException("Il path Localization non punta a una stringa esistente.");
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(ChangeSet));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record SetStringCommand(LocalizationReferenceOrigin Origin, string OldValue, string NewValue);

    private sealed class LocalizationOriginComparer : IEqualityComparer<LocalizationReferenceOrigin>
    {
        public static LocalizationOriginComparer Ordinal { get; } = new();

        public bool Equals(LocalizationReferenceOrigin? x, LocalizationReferenceOrigin? y) =>
            x is not null && y is not null &&
            string.Equals(x.Locale, y.Locale, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.SourceFile, y.SourceFile, StringComparison.OrdinalIgnoreCase) &&
            x.Segments.SequenceEqual(y.Segments);

        public int GetHashCode(LocalizationReferenceOrigin origin) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(origin.Locale),
            StringComparer.OrdinalIgnoreCase.GetHashCode(origin.SourceFile),
            string.Join('/', origin.Segments.Select(segment =>
                segment.PropertyName ?? $"[{segment.ArrayIndex}]")));
    }
}
