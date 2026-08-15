using System.Text.RegularExpressions;

namespace ZiapStudio.Core.Notetags;

public sealed partial class NotetagParser
{
    public NotetagDocument Parse(string? source)
    {
        source ??= string.Empty;
        var parsed = new List<NotetagNode>();
        var occupied = new List<TextSpan>();

        foreach (Match match in BlockTagRegex().Matches(source))
        {
            var name = match.Groups["name"];
            var body = match.Groups["body"];
            var span = new TextSpan(match.Index, match.Length);
            parsed.Add(new BlockNotetagNode
            {
                Name = name.Value.Trim(),
                Body = body.Value,
                BodySpan = new TextSpan(body.Index, body.Length),
                Span = span,
                RawText = match.Value,
            });
            occupied.Add(span);
        }

        foreach (Match match in InlineTagRegex().Matches(source))
        {
            if (occupied.Any(span => match.Index >= span.Start && match.Index < span.End))
            {
                continue;
            }

            var content = match.Groups["content"];
            var colonOffset = content.Value.IndexOf(':');
            var rawName = colonOffset < 0 ? content.Value : content.Value[..colonOffset];
            var trimmedName = rawName.Trim();
            var nameLeadingWhitespace = rawName.Length - rawName.TrimStart().Length;
            string? value = null;
            TextSpan? valueSpan = null;
            if (colonOffset >= 0)
            {
                var rawValue = content.Value[(colonOffset + 1)..];
                value = rawValue.Trim();
                var valueLeadingWhitespace = rawValue.Length - rawValue.TrimStart().Length;
                valueSpan = new TextSpan(
                    content.Index + colonOffset + 1 + valueLeadingWhitespace,
                    value.Length);
            }

            parsed.Add(new InlineNotetagNode
            {
                Name = trimmedName,
                Value = value,
                NameSpan = new TextSpan(
                    content.Index + nameLeadingWhitespace,
                    trimmedName.Length),
                ValueSpan = valueSpan,
                Span = new TextSpan(match.Index, match.Length),
                RawText = match.Value,
            });
        }

        parsed.Sort((left, right) => left.Span.Start.CompareTo(right.Span.Start));
        var nodes = new List<NotetagNode>();
        var cursor = 0;
        foreach (var node in parsed)
        {
            if (node.Span.Start > cursor)
            {
                var raw = source[cursor..node.Span.Start];
                nodes.Add(new RawTextNotetagNode
                {
                    Span = new TextSpan(cursor, raw.Length),
                    RawText = raw,
                });
            }

            nodes.Add(node);
            cursor = node.Span.End;
        }

        if (cursor < source.Length)
        {
            nodes.Add(new RawTextNotetagNode
            {
                Span = new TextSpan(cursor, source.Length - cursor),
                RawText = source[cursor..],
            });
        }

        return new NotetagDocument { Source = source, Nodes = nodes };
    }

    [GeneratedRegex(
        @"(?ms)^[\t ]*<(?<name>[A-Za-z][^<>:/]*?)>[\t ]*\r?\n(?<body>.*?)^[\t ]*</\k<name>>[\t ]*(?=\r?$)",
        RegexOptions.CultureInvariant)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(
        @"(?m)^[\t ]*<(?<content>[^<>\r\n]+)>[\t ]*(?=\r?$)",
        RegexOptions.CultureInvariant)]
    private static partial Regex InlineTagRegex();
}
