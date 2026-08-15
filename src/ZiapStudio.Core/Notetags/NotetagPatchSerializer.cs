namespace ZiapStudio.Core.Notetags;

public sealed class NotetagPatchSerializer
{
    private readonly NotetagParser _parser;

    public NotetagPatchSerializer(NotetagParser? parser = null)
    {
        _parser = parser ?? new NotetagParser();
    }

    public string SetInlineValue(
        string source,
        Func<InlineNotetagNode, bool> predicate,
        string tagName,
        string value)
    {
        var document = _parser.Parse(source);
        var node = document.InlineTags.FirstOrDefault(predicate);
        if (node?.ValueSpan is { } valueSpan)
        {
            return Replace(source, valueSpan, value);
        }

        if (node is not null)
        {
            return Replace(source, node.Span, $"<{tagName}:{value}>");
        }

        return AppendLine(source, $"<{tagName}:{value}>");
    }

    public string SetFlag(
        string source,
        Func<InlineNotetagNode, bool> predicate,
        string expression,
        bool enabled)
    {
        var node = _parser.Parse(source).InlineTags.FirstOrDefault(predicate);
        if (enabled)
        {
            return node is null ? AppendLine(source, $"<{expression}>") : source;
        }

        return node is null ? source : RemoveLine(source, node.Span);
    }

    public string RemoveInline(
        string source,
        Func<InlineNotetagNode, bool> predicate)
    {
        var node = _parser.Parse(source).InlineTags.FirstOrDefault(predicate);
        return node is null ? source : RemoveLine(source, node.Span);
    }

    public string SetBlockBody(string source, string blockName, string body)
    {
        var node = _parser.Parse(source).BlockTags.FirstOrDefault(candidate =>
            candidate.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase));
        if (node is not null)
        {
            var replacement = node.Body.EndsWith('\n') && !body.EndsWith('\n')
                ? body + DetectNewline(source)
                : body;
            return Replace(source, node.BodySpan, replacement);
        }

        var newline = DetectNewline(source);
        var block = $"<{blockName}>{newline}{body}{newline}</{blockName}>";
        return AppendLine(source, block);
    }

    public string RemoveBlock(string source, string blockName)
    {
        var node = _parser.Parse(source).BlockTags.FirstOrDefault(candidate =>
            candidate.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase));
        return node is null ? source : RemoveLine(source, node.Span);
    }

    public static string Replace(string source, TextSpan span, string replacement) =>
        string.Concat(source.AsSpan(0, span.Start), replacement, source.AsSpan(span.End));

    private static string AppendLine(string source, string line)
    {
        if (source.Length == 0)
        {
            return line;
        }

        var newline = DetectNewline(source);
        return source.EndsWith('\n') || source.EndsWith('\r')
            ? source + line
            : source + newline + line;
    }

    private static string RemoveLine(string source, TextSpan span)
    {
        var start = span.Start;
        var end = span.End;
        if (end < source.Length && source[end] == '\r')
        {
            end++;
        }

        if (end < source.Length && source[end] == '\n')
        {
            end++;
        }
        else if (start > 0 && source[start - 1] == '\n')
        {
            start--;
            if (start > 0 && source[start - 1] == '\r')
            {
                start--;
            }
        }

        return string.Concat(source.AsSpan(0, start), source.AsSpan(end));
    }

    private static string DetectNewline(string source) =>
        source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
