using System.Text.Json.Nodes;

namespace ZiapStudio.Core.Editing;

public sealed record DocumentChange
{
    public string Target { get; init; } = string.Empty;

    public string PropertyPath { get; init; } = string.Empty;

    public JsonNode? OldValue { get; init; }

    public bool OldValueExists { get; init; } = true;

    public JsonNode? NewValue { get; init; }

    public bool NewValueExists { get; init; } = true;
}

public sealed record DocumentChangeSet
{
    public static DocumentChangeSet Empty { get; } = new();

    public IReadOnlyList<DocumentChange> Changes { get; init; } = [];

    public int Count => Changes.Count;
}
