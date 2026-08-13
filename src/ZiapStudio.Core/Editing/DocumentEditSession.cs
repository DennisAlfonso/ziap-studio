using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ZiapStudio.Core.Documents;

namespace ZiapStudio.Core.Editing;

public sealed class DocumentEditSession : INotifyPropertyChanged
{
    private readonly List<JsonValueEditCommand> _history = [];
    private JsonNode _originalState;
    private int _historyIndex;

    public DocumentEditSession(
        RpgMakerDatabaseDocument document,
        IReadOnlyList<DocumentValidationIssue>? baselineIssues = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.SourceRoot);

        Descriptor = document.Descriptor;
        Definition = document.Definition;
        SourceSnapshot = document.SourceSnapshot;
        WorkingState = document.SourceRoot.DeepClone();
        _originalState = document.SourceRoot.DeepClone();
        BaselineIssues = baselineIssues ?? [];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DocumentDescriptor Descriptor { get; }

    public RpgMakerDatabaseDefinition Definition { get; }

    public DocumentSourceSnapshot SourceSnapshot { get; private set; }

    public JsonNode WorkingState { get; }

    public JsonNode OriginalState => _originalState.DeepClone();

    public IReadOnlyList<DocumentValidationIssue> BaselineIssues { get; }

    public bool IsDirty => !JsonNode.DeepEquals(WorkingState, _originalState);

    public bool CanUndo => _historyIndex > 0;

    public bool CanRedo => _historyIndex < _history.Count;

    public DocumentChangeSet ChangeSet => BuildChangeSet();

    public bool SetValue(int recordId, string propertyPath, JsonNode? newValue)
    {
        if (recordId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recordId));
        }

        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            throw new ArgumentException("Il percorso della proprietà è obbligatorio.", nameof(propertyPath));
        }

        var record = FindRecord(recordId) ??
            throw new InvalidOperationException($"Il record #{recordId} non esiste nel documento.");
        if (!JsonPropertyPath.TryRead(record, propertyPath, out var oldValue, out var existed))
        {
            throw new InvalidOperationException(
                $"Il percorso JSON '{propertyPath}' non è compatibile con il record #{recordId}.");
        }

        if (existed && JsonNode.DeepEquals(oldValue, newValue))
        {
            return false;
        }

        if (_historyIndex < _history.Count)
        {
            _history.RemoveRange(_historyIndex, _history.Count - _historyIndex);
        }

        var target = $"rpgmaker://database/{Descriptor.ResourceId.AbsolutePath.Trim('/')}/{recordId}";
        var command = new JsonValueEditCommand(
            record,
            target,
            propertyPath,
            oldValue?.DeepClone(),
            newValue?.DeepClone(),
            existed);
        command.Execute();
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

        _history[--_historyIndex].Undo();
        NotifyStateChanged();
        return true;
    }

    public JsonNode? GetValue(int recordId, string propertyPath)
    {
        if (!TryGetValue(recordId, propertyPath, out var value))
        {
            throw new InvalidOperationException(
                $"Il percorso JSON '{propertyPath}' non esiste nel record #{recordId}.");
        }

        return value;
    }

    public bool TryGetValue(int recordId, string propertyPath, out JsonNode? value)
    {
        value = null;
        var record = FindRecord(recordId);
        if (record is null ||
            !JsonPropertyPath.TryRead(record, propertyPath, out var sourceValue, out var existed) ||
            !existed)
        {
            return false;
        }

        value = sourceValue?.DeepClone();
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        _history[_historyIndex++].Execute();
        NotifyStateChanged();
        return true;
    }

    public void AcceptSavedSnapshot(DocumentSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SourceSnapshot = snapshot;
        _originalState = WorkingState.DeepClone();
        _history.Clear();
        _historyIndex = 0;
        OnPropertyChanged(nameof(SourceSnapshot));
        OnPropertyChanged(nameof(OriginalState));
        NotifyStateChanged();
    }

    private JsonObject? FindRecord(int recordId)
    {
        if (WorkingState is not JsonArray array)
        {
            return null;
        }

        return array
            .OfType<JsonObject>()
            .FirstOrDefault(record =>
                record["id"] is JsonValue idValue &&
                idValue.TryGetValue<int>(out var id) &&
                id == recordId);
    }

    private DocumentChangeSet BuildChangeSet()
    {
        if (_historyIndex == 0)
        {
            return DocumentChangeSet.Empty;
        }

        var changes = _history
            .Take(_historyIndex)
            .GroupBy(
                command => (command.Target, command.PropertyPath),
                StringTupleComparer.Ordinal)
            .Select(group => new DocumentChange
            {
                Target = group.Key.Target,
                PropertyPath = group.Key.PropertyPath,
                OldValue = group.First().OldValue?.DeepClone(),
                OldValueExists = group.First().OldValueExisted,
                NewValue = group.Last().NewValue?.DeepClone(),
            })
            .Where(change =>
                change.OldValueExists != change.NewValueExists ||
                !JsonNode.DeepEquals(change.OldValue, change.NewValue))
            .ToArray();
        return changes.Length == 0
            ? DocumentChangeSet.Empty
            : new DocumentChangeSet { Changes = changes };
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

    private sealed class JsonValueEditCommand
    {
        private readonly JsonObject _record;
        private readonly bool _oldValueExisted;

        public JsonValueEditCommand(
            JsonObject record,
            string target,
            string propertyPath,
            JsonNode? oldValue,
            JsonNode? newValue,
            bool oldValueExisted)
        {
            _record = record;
            Target = target;
            PropertyPath = propertyPath;
            OldValue = oldValue;
            NewValue = newValue;
            _oldValueExisted = oldValueExisted;
        }

        public string Target { get; }

        public string PropertyPath { get; }

        public JsonNode? OldValue { get; }

        public JsonNode? NewValue { get; }

        public bool OldValueExisted => _oldValueExisted;

        public void Execute() =>
            JsonPropertyPath.Write(_record, PropertyPath, NewValue?.DeepClone(), remove: false);

        public void Undo() =>
            JsonPropertyPath.Write(
                _record,
                PropertyPath,
                OldValue?.DeepClone(),
                remove: !_oldValueExisted);
    }

    private static class JsonPropertyPath
    {
        public static bool TryRead(
            JsonObject record,
            string propertyPath,
            out JsonNode? value,
            out bool existed)
        {
            value = null;
            existed = false;
            if (!TryParse(propertyPath, out var propertyName, out var indexes))
            {
                return false;
            }

            if (!record.TryGetPropertyValue(propertyName, out var node))
            {
                return indexes.Count == 0;
            }

            existed = true;
            foreach (var index in indexes)
            {
                if (node is not JsonArray array || index < 0 || index >= array.Count)
                {
                    existed = false;
                    return false;
                }

                node = array[index];
            }

            value = node;
            return true;
        }

        public static void Write(
            JsonObject record,
            string propertyPath,
            JsonNode? value,
            bool remove)
        {
            if (!TryParse(propertyPath, out var propertyName, out var indexes))
            {
                throw new InvalidOperationException($"Il percorso JSON '{propertyPath}' non è valido.");
            }

            if (indexes.Count == 0)
            {
                if (remove)
                {
                    record.Remove(propertyName);
                }
                else
                {
                    record[propertyName] = value;
                }

                return;
            }

            JsonNode? node = record[propertyName];
            for (var position = 0; position < indexes.Count - 1; position++)
            {
                node = node is JsonArray array ? array[indexes[position]] : null;
            }

            if (node is not JsonArray targetArray)
            {
                throw new InvalidOperationException(
                    $"Il percorso JSON '{propertyPath}' non punta a un array.");
            }

            targetArray[indexes[^1]] = value;
        }

        private static bool TryParse(
            string path,
            out string propertyName,
            out IReadOnlyList<int> indexes)
        {
            propertyName = string.Empty;
            var parsedIndexes = new List<int>();
            var firstBracket = path.IndexOf('[');
            propertyName = (firstBracket < 0 ? path : path[..firstBracket]).Trim();
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                indexes = [];
                return false;
            }

            var position = firstBracket;
            while (position >= 0 && position < path.Length)
            {
                var closingBracket = path.IndexOf(']', position + 1);
                if (closingBracket < 0 ||
                    !int.TryParse(path.AsSpan(position + 1, closingBracket - position - 1), out var index))
                {
                    indexes = [];
                    return false;
                }

                parsedIndexes.Add(index);
                position = closingBracket + 1;
                if (position == path.Length)
                {
                    break;
                }

                if (path[position] != '[')
                {
                    indexes = [];
                    return false;
                }
            }

            indexes = parsedIndexes;
            return true;
        }
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Target, string PropertyPath)>
    {
        public static StringTupleComparer Ordinal { get; } = new();

        public bool Equals(
            (string Target, string PropertyPath) x,
            (string Target, string PropertyPath) y) =>
            string.Equals(x.Target, y.Target, StringComparison.Ordinal) &&
            string.Equals(x.PropertyPath, y.PropertyPath, StringComparison.Ordinal);

        public int GetHashCode((string Target, string PropertyPath) value) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.Target),
                StringComparer.Ordinal.GetHashCode(value.PropertyPath));
    }
}
