namespace ZiapStudio.Core.Notetags;

public sealed record TextSpan(int Start, int Length)
{
    public int End => Start + Length;
}

public sealed record NotetagDocument
{
    public string Source { get; init; } = string.Empty;

    public IReadOnlyList<NotetagNode> Nodes { get; init; } = [];

    public IEnumerable<InlineNotetagNode> InlineTags => Nodes.OfType<InlineNotetagNode>();

    public IEnumerable<BlockNotetagNode> BlockTags => Nodes.OfType<BlockNotetagNode>();
}

public abstract record NotetagNode
{
    public TextSpan Span { get; init; } = new(0, 0);

    public string RawText { get; init; } = string.Empty;
}

public sealed record RawTextNotetagNode : NotetagNode;

public sealed record InlineNotetagNode : NotetagNode
{
    public string Name { get; init; } = string.Empty;

    public string? Value { get; init; }

    public TextSpan? NameSpan { get; init; }

    public TextSpan? ValueSpan { get; init; }
}

public sealed record BlockNotetagNode : NotetagNode
{
    public string Name { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public TextSpan BodySpan { get; init; } = new(0, 0);
}
