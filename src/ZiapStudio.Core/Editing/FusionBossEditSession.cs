using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ZiapStudio.Core.Documents;

namespace ZiapStudio.Core.Editing;

public sealed class FusionBossEditSession : INotifyPropertyChanged
{
    private readonly List<EditCommand> _history = [];
    private JsonNode _originalState;
    private int _historyIndex;

    public FusionBossEditSession(FusionBossWorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.EncounterSourceRoot);
        ArgumentNullException.ThrowIfNull(document.EncounterSourceSnapshot);

        SourceSnapshot = document.EncounterSourceSnapshot;
        WorkingState = document.EncounterSourceRoot.DeepClone();
        _originalState = document.EncounterSourceRoot.DeepClone();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DocumentSourceSnapshot SourceSnapshot { get; private set; }

    public JsonNode WorkingState { get; }

    public bool IsDirty => !JsonNode.DeepEquals(WorkingState, _originalState);

    public bool CanUndo => _historyIndex > 0;

    public bool CanRedo => _historyIndex < _history.Count;

    public JsonNode? GetValue(string pointer)
    {
        if (!TryResolve(pointer, out _, out _, out var value, out var existed) || !existed)
        {
            return null;
        }
        return value?.DeepClone();
    }

    public bool SetValue(string pointer, JsonNode? value) =>
        SetValues([(pointer, value)]);

    public bool SetValues(IEnumerable<(string Pointer, JsonNode? Value)> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var mutations = new List<Mutation>();
        foreach (var (pointer, value) in changes)
        {
            if (!TryResolve(pointer, out var parent, out var segment, out var oldValue, out var existed))
            {
                throw new InvalidOperationException($"Il percorso JSON '{pointer}' non è modificabile.");
            }
            if (existed && JsonNode.DeepEquals(oldValue, value))
            {
                continue;
            }
            mutations.Add(new Mutation(
                parent,
                segment,
                oldValue?.DeepClone(),
                value?.DeepClone(),
                existed));
        }
        if (mutations.Count == 0)
        {
            return false;
        }
        if (_historyIndex < _history.Count)
        {
            _history.RemoveRange(_historyIndex, _history.Count - _historyIndex);
        }
        var command = new EditCommand(mutations);
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
        NotifyStateChanged();
    }

    public static string EscapePointerSegment(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    private bool TryResolve(
        string pointer,
        out JsonNode parent,
        out string segment,
        out JsonNode? value,
        out bool existed)
    {
        parent = WorkingState;
        segment = string.Empty;
        value = null;
        existed = false;
        var segments = ParsePointer(pointer);
        if (segments.Count == 0)
        {
            return false;
        }
        JsonNode? current = WorkingState;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            current = ReadChild(current, segments[index], out var childExists);
            if (!childExists || current is null)
            {
                return false;
            }
        }
        if (current is not JsonObject && current is not JsonArray)
        {
            return false;
        }
        parent = current;
        segment = segments[^1];
        value = ReadChild(parent, segment, out existed);
        return parent is JsonObject || existed;
    }

    private static JsonNode? ReadChild(JsonNode? parent, string segment, out bool existed)
    {
        existed = false;
        if (parent is JsonObject jsonObject)
        {
            existed = jsonObject.TryGetPropertyValue(segment, out var value);
            return value;
        }
        if (parent is JsonArray jsonArray &&
            int.TryParse(segment, out var index) &&
            index >= 0 && index < jsonArray.Count)
        {
            existed = true;
            return jsonArray[index];
        }
        return null;
    }

    private static IReadOnlyList<string> ParsePointer(string pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer) || pointer[0] != '/')
        {
            return [];
        }
        return pointer.Split('/').Skip(1).Select(segment =>
            segment.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal)).ToArray();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(WorkingState));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class EditCommand(IReadOnlyList<Mutation> mutations)
    {
        public void Execute()
        {
            foreach (var mutation in mutations)
            {
                Write(mutation.Parent, mutation.Segment, mutation.NewValue?.DeepClone(), false);
            }
        }

        public void Undo()
        {
            foreach (var mutation in mutations.Reverse())
            {
                Write(
                    mutation.Parent,
                    mutation.Segment,
                    mutation.OldValue?.DeepClone(),
                    !mutation.OldValueExisted);
            }
        }

        private static void Write(JsonNode parent, string segment, JsonNode? value, bool remove)
        {
            if (parent is JsonObject jsonObject)
            {
                if (remove)
                {
                    jsonObject.Remove(segment);
                }
                else
                {
                    jsonObject[segment] = value;
                }
                return;
            }
            if (parent is JsonArray jsonArray &&
                int.TryParse(segment, out var index) &&
                index >= 0 && index < jsonArray.Count && !remove)
            {
                jsonArray[index] = value;
                return;
            }
            throw new InvalidOperationException($"Impossibile scrivere il segmento JSON '{segment}'.");
        }
    }

    private sealed record Mutation(
        JsonNode Parent,
        string Segment,
        JsonNode? OldValue,
        JsonNode? NewValue,
        bool OldValueExisted);
}
