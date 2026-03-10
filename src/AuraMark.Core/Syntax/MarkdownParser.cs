using Microsoft.CodeAnalysis.Text;
using AuraMark.Core.Text;

namespace AuraMark.Core.Syntax;

public sealed class MarkdownParser : IMarkdownParser, IIncrementalMarkdownParser
{
    private readonly ReparseWindowExpander _expander = new();

    public ParseResult Parse(DocumentSnapshot snapshot) => FullParse(snapshot);

    public ReparseWindow ExpandToSafeWindow(SourceText text, ParseResult? previous, TextSpan dirtySpan) =>
        _expander.ExpandToSafeWindow(text, previous, dirtySpan);

    public ParseResult Parse(DocumentSnapshot snapshot, ParseResult? previous, TextSpan dirtySpan)
    {
        if (previous is null)
        {
            return FullParse(snapshot);
        }

        var blocks = ParseBlocks(snapshot.Text, 0, snapshot.Text.Length);
        return new ParseResult(
            snapshot,
            blocks,
            BuildOutline(snapshot.Text, blocks),
            Array.Empty<ParseDiagnostic>(),
            BuildBlockMap(blocks));
    }

    private ParseResult FullParse(DocumentSnapshot snapshot)
    {
        var blocks = ParseBlocks(snapshot.Text, 0, snapshot.Text.Length);
        return new ParseResult(
            snapshot,
            blocks,
            BuildOutline(snapshot.Text, blocks),
            Array.Empty<ParseDiagnostic>(),
            BuildBlockMap(blocks));
    }

    private IReadOnlyList<MdBlock> ParseBlocks(SourceText text, int start, int end)
    {
        var blocks = new List<MdBlock>();
        var cursor = start;

        while (cursor < end)
        {
            var line = text.Lines.GetLineFromPosition(cursor);
            var lineText = text.ToString(line.Span);

            if (string.IsNullOrWhiteSpace(lineText))
            {
                cursor = line.EndIncludingLineBreak;
                continue;
            }

            if (TryParseFence(text, line.LineNumber, end, out var fence, out var nextFencePosition))
            {
                blocks.Add(fence);
                cursor = nextFencePosition;
                continue;
            }

            if (TryParseHeading(text, line.LineNumber, out var heading))
            {
                blocks.Add(heading);
                cursor = line.EndIncludingLineBreak;
                continue;
            }

            var paragraph = ParseParagraph(text, line.LineNumber, end, out var nextParagraphPosition);
            blocks.Add(paragraph);
            cursor = nextParagraphPosition;
        }

        return blocks;
    }

    private static bool TryParseFence(SourceText text, int startLine, int end, out CodeFenceBlock block, out int nextPosition)
    {
        block = default!;
        nextPosition = 0;

        var openLine = text.Lines[startLine];
        var openText = text.ToString(openLine.Span);
        if (!TryMatchFenceOpen(openText, out var marker, out _, out var language))
        {
            return false;
        }

        var contentStart = openLine.EndIncludingLineBreak;
        var lineIndex = startLine + 1;
        while (lineIndex < text.Lines.Count)
        {
            var line = text.Lines[lineIndex];
            var lineText = text.ToString(line.Span);
            if (TryMatchFenceClose(lineText, marker))
            {
                var span = TextSpan.FromBounds(openLine.Start, line.EndIncludingLineBreak);
                var contentSpan = TextSpan.FromBounds(contentStart, line.Start);
                block = new CodeFenceBlock(span, openLine.Span, contentSpan, language);
                nextPosition = line.EndIncludingLineBreak;
                return true;
            }

            if (line.Start >= end)
            {
                break;
            }

            lineIndex++;
        }

        var lastLine = text.Lines[Math.Min(lineIndex - 1, text.Lines.Count - 1)];
        var fallbackSpan = TextSpan.FromBounds(openLine.Start, lastLine.EndIncludingLineBreak);
        block = new CodeFenceBlock(
            fallbackSpan,
            openLine.Span,
            TextSpan.FromBounds(contentStart, fallbackSpan.End),
            language);
        nextPosition = fallbackSpan.End;
        return true;
    }

    private static bool TryParseHeading(SourceText text, int lineNumber, out HeadingBlock block)
    {
        block = default!;

        var line = text.Lines[lineNumber];
        var lineText = text.ToString(line.Span);
        var level = CountLeadingHashes(lineText);
        if (level is < 1 or > 6)
        {
            return false;
        }

        if (lineText.Length <= level || lineText[level] != ' ')
        {
            return false;
        }

        var inlineSpan = TextSpan.FromBounds(line.Start + level + 1, line.End);
        block = new HeadingBlock(line.Span, level, ParseInlines(inlineSpan));
        return true;
    }

    private static ParagraphBlock ParseParagraph(SourceText text, int startLine, int end, out int nextPosition)
    {
        var lineIndex = startLine;
        var start = text.Lines[startLine].Start;
        var paragraphEnd = start;

        while (lineIndex < text.Lines.Count)
        {
            var line = text.Lines[lineIndex];
            var lineText = text.ToString(line.Span);
            if (string.IsNullOrWhiteSpace(lineText))
            {
                break;
            }

            if (lineIndex != startLine && LooksLikeBlockStart(lineText))
            {
                break;
            }

            paragraphEnd = line.End;
            if (line.EndIncludingLineBreak >= end)
            {
                break;
            }

            lineIndex++;
        }

        var span = TextSpan.FromBounds(start, paragraphEnd);
        nextPosition = lineIndex < text.Lines.Count
            ? text.Lines[lineIndex].EndIncludingLineBreak
            : text.Length;
        return new ParagraphBlock(span, ParseInlines(span));
    }

    private static IReadOnlyList<MdInline> ParseInlines(TextSpan span) => [new TextInline(span)];

    private static bool LooksLikeBlockStart(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("#", StringComparison.Ordinal) ||
               trimmed.StartsWith("```", StringComparison.Ordinal) ||
               trimmed.StartsWith("~~~", StringComparison.Ordinal) ||
               trimmed.StartsWith(">", StringComparison.Ordinal) ||
               trimmed.StartsWith("- ", StringComparison.Ordinal) ||
               trimmed.StartsWith("* ", StringComparison.Ordinal) ||
               trimmed.StartsWith("+ ", StringComparison.Ordinal);
    }

    private static bool TryMatchFenceOpen(string text, out string marker, out int markerLength, out string? language)
    {
        marker = string.Empty;
        markerLength = 0;
        language = null;

        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            marker = "`";
            markerLength = 3;
            language = trimmed[3..].Trim();
            return true;
        }

        if (trimmed.StartsWith("~~~", StringComparison.Ordinal))
        {
            marker = "~";
            markerLength = 3;
            language = trimmed[3..].Trim();
            return true;
        }

        return false;
    }

    private static bool TryMatchFenceClose(string text, string marker)
    {
        var trimmed = text.TrimStart();
        return marker == "`"
            ? trimmed.StartsWith("```", StringComparison.Ordinal)
            : trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    private static int CountLeadingHashes(string text)
    {
        var index = 0;
        while (index < text.Length && index < 6 && text[index] == '#')
        {
            index++;
        }

        return index;
    }

    private static IReadOnlyList<OutlineItem> BuildOutline(SourceText text, IReadOnlyList<MdBlock> blocks)
    {
        return blocks
            .OfType<HeadingBlock>()
            .Select(block => new OutlineItem(ExtractHeadingText(text, block), block.Level, block.Span))
            .Where(item => !string.IsNullOrWhiteSpace(item.Text))
            .ToArray();
    }

    private static string ExtractHeadingText(SourceText text, HeadingBlock heading)
    {
        var raw = text.ToString(heading.Span).Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var markerLength = CountLeadingHashes(raw);
        if (markerLength > 0 && markerLength < raw.Length && raw[markerLength] == ' ')
        {
            raw = raw[(markerLength + 1)..].Trim();
        }

        var trimmed = raw.TrimEnd();
        var closingHashIndex = trimmed.Length;
        while (closingHashIndex > 0 && trimmed[closingHashIndex - 1] == '#')
        {
            closingHashIndex--;
        }

        if (closingHashIndex < trimmed.Length)
        {
            trimmed = trimmed[..closingHashIndex].TrimEnd();
        }

        return trimmed;
    }

    private static IReadOnlyList<BlockMapEntry> BuildBlockMap(IReadOnlyList<MdBlock> blocks)
    {
        return blocks
            .Select((block, index) => new BlockMapEntry(block.Span, index, block.Kind))
            .ToArray();
    }
}
