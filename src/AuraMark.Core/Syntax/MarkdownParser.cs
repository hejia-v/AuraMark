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

            if (TryParseQuote(text, line.LineNumber, end, out var quote, out var nextQuotePosition))
            {
                blocks.Add(quote);
                cursor = nextQuotePosition;
                continue;
            }

            if (TryParseList(text, line.LineNumber, end, out var list, out var nextListPosition))
            {
                blocks.Add(list);
                cursor = nextListPosition;
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
        block = new HeadingBlock(line.Span, level, ParseInlines(text, inlineSpan));
        return true;
    }

    private static bool TryParseQuote(SourceText text, int startLine, int end, out QuoteBlock block, out int nextPosition)
    {
        block = default!;
        nextPosition = 0;

        var lines = CreateProjectedLines(text, startLine, end);
        if (!TryParseQuote(text, lines, 0, out block, out var consumedLines))
        {
            return false;
        }

        var lastLine = text.Lines[startLine + consumedLines - 1];
        nextPosition = lastLine.EndIncludingLineBreak;
        return true;
    }

    private static bool TryParseQuote(
        SourceText text,
        IReadOnlyList<ProjectedLine> lines,
        int startIndex,
        out QuoteBlock block,
        out int consumedLines)
    {
        block = default!;
        consumedLines = 0;

        if (startIndex >= lines.Count ||
            !TryStripQuoteMarker(lines[startIndex], out var firstContent))
        {
            return false;
        }

        var quotedLines = new List<ProjectedLine> { firstContent };
        var index = startIndex + 1;
        while (index < lines.Count && TryStripQuoteMarker(lines[index], out var content))
        {
            quotedLines.Add(content);
            index++;
        }

        var children = ParseProjectedBlocks(text, quotedLines);
        var span = TextSpan.FromBounds(lines[startIndex].Span.Start, lines[index - 1].Span.End);
        block = new QuoteBlock(span, children);
        consumedLines = index - startIndex;
        return true;
    }

    private static bool TryParseList(SourceText text, int startLine, int end, out ListBlock block, out int nextPosition)
    {
        block = default!;
        nextPosition = 0;

        var lines = CreateProjectedLines(text, startLine, end);
        if (!TryParseList(text, lines, 0, out block, out var consumedLines))
        {
            return false;
        }

        var lastLine = text.Lines[startLine + consumedLines - 1];
        nextPosition = lastLine.EndIncludingLineBreak;
        return true;
    }

    private static bool TryParseList(
        SourceText text,
        IReadOnlyList<ProjectedLine> lines,
        int startIndex,
        out ListBlock block,
        out int consumedLines)
    {
        block = default!;
        consumedLines = 0;

        if (startIndex >= lines.Count ||
            !TryMatchListMarker(lines[startIndex].Text, out var ordered, out var baseIndent, out _, out var contentOffset))
        {
            return false;
        }

        var items = new List<ListItemBlock>();
        var index = startIndex;

        while (index < lines.Count &&
               TryMatchListMarker(lines[index].Text, out var itemOrdered, out var itemIndent, out _, out var itemContentOffset) &&
               itemOrdered == ordered &&
               itemIndent == baseIndent)
        {
            var itemLine = lines[index];
            var itemChildrenLines = new List<ProjectedLine>();
            var itemContent = itemLine.Slice(itemContentOffset);
            if (!string.IsNullOrWhiteSpace(itemContent.Text))
            {
                itemChildrenLines.Add(itemContent);
            }

            var continuationIndex = index + 1;
            while (continuationIndex < lines.Count)
            {
                var nextLine = lines[continuationIndex];
                if (string.IsNullOrWhiteSpace(nextLine.Text))
                {
                    if (!HasListContinuation(lines, continuationIndex + 1, baseIndent, itemContentOffset))
                    {
                        break;
                    }

                    itemChildrenLines.Add(nextLine.Slice(Math.Min(nextLine.Text.Length, itemContentOffset)));
                    continuationIndex++;
                    continue;
                }

                if (TryMatchListMarker(nextLine.Text, out _, out var nextIndent, out _, out _) &&
                    nextIndent <= baseIndent)
                {
                    break;
                }

                var leadingSpaces = CountLeadingSpaces(nextLine.Text);
                if (leadingSpaces < itemContentOffset)
                {
                    break;
                }

                var continuationOffset = Math.Min(nextLine.Text.Length, itemContentOffset);
                itemChildrenLines.Add(nextLine.Slice(continuationOffset));
                continuationIndex++;
            }

            var itemChildren = ParseProjectedBlocks(text, itemChildrenLines);
            var itemSpan = TextSpan.FromBounds(itemLine.Span.Start, lines[Math.Max(index, continuationIndex - 1)].Span.End);
            items.Add(new ListItemBlock(itemSpan, itemChildren));
            index = continuationIndex;
        }

        if (items.Count == 0)
        {
            return false;
        }

        var span = TextSpan.FromBounds(lines[startIndex].Span.Start, lines[index - 1].Span.End);
        block = new ListBlock(span, ordered, items);
        consumedLines = index - startIndex;
        return true;
    }

    private static ParagraphBlock ParseParagraph(SourceText text, int startLine, int end, out int nextPosition)
    {
        var lineIndex = startLine;
        var start = text.Lines[startLine].Start;
        var paragraphEnd = start;
        var stoppedAtBlockStart = false;

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
                stoppedAtBlockStart = true;
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
        if (lineIndex >= text.Lines.Count)
        {
            nextPosition = text.Length;
        }
        else
        {
            var nextLine = text.Lines[lineIndex];
            nextPosition = stoppedAtBlockStart
                ? nextLine.Start
                : nextLine.EndIncludingLineBreak;
        }

        return new ParagraphBlock(span, ParseInlines(text, span));
    }

    private static IReadOnlyList<MdBlock> ParseProjectedBlocks(SourceText text, IReadOnlyList<ProjectedLine> lines)
    {
        var blocks = new List<MdBlock>();
        var index = 0;

        while (index < lines.Count)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                index++;
                continue;
            }

            if (TryParseProjectedHeading(text, line, out var heading))
            {
                blocks.Add(heading);
                index++;
                continue;
            }

            if (TryParseProjectedFence(lines, index, out var fence, out var consumedFenceLines))
            {
                blocks.Add(fence);
                index += consumedFenceLines;
                continue;
            }

            if (TryParseQuote(text, lines, index, out var quote, out var consumedQuoteLines))
            {
                blocks.Add(quote);
                index += consumedQuoteLines;
                continue;
            }

            if (TryParseList(text, lines, index, out var list, out var consumedListLines))
            {
                blocks.Add(list);
                index += consumedListLines;
                continue;
            }

            var paragraph = ParseProjectedParagraph(text, lines, index, out var consumedParagraphLines);
            blocks.Add(paragraph);
            index += consumedParagraphLines;
        }

        return blocks;
    }

    private static IReadOnlyList<MdInline> ParseInlines(SourceText text, TextSpan span)
    {
        if (span.Length == 0)
        {
            return Array.Empty<MdInline>();
        }

        return ParseInlineRange(text.ToString(span), span.Start);
    }

    private static bool LooksLikeBlockStart(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("#", StringComparison.Ordinal) ||
               trimmed.StartsWith("```", StringComparison.Ordinal) ||
               trimmed.StartsWith("~~~", StringComparison.Ordinal) ||
               trimmed.StartsWith(">", StringComparison.Ordinal) ||
               trimmed.StartsWith("- ", StringComparison.Ordinal) ||
               trimmed.StartsWith("* ", StringComparison.Ordinal) ||
               trimmed.StartsWith("+ ", StringComparison.Ordinal) ||
               IsOrderedListMarker(trimmed);
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

    private static int CountLeadingSpaces(string text)
    {
        var index = 0;
        while (index < text.Length && text[index] == ' ')
        {
            index++;
        }

        return index;
    }

    private static bool TryParseProjectedHeading(SourceText text, ProjectedLine line, out HeadingBlock block)
    {
        block = default!;

        var level = CountLeadingHashes(line.Text);
        if (level is < 1 or > 6)
        {
            return false;
        }

        if (line.Text.Length <= level || line.Text[level] != ' ')
        {
            return false;
        }

        var inlineSpan = TextSpan.FromBounds(line.Span.Start + level + 1, line.Span.End);
        block = new HeadingBlock(line.Span, level, ParseInlines(text, inlineSpan));
        return true;
    }

    private static bool TryParseProjectedFence(
        IReadOnlyList<ProjectedLine> lines,
        int startIndex,
        out CodeFenceBlock block,
        out int consumedLines)
    {
        block = default!;
        consumedLines = 0;

        if (startIndex >= lines.Count ||
            !TryMatchFenceOpen(lines[startIndex].Text, out var marker, out _, out var language))
        {
            return false;
        }

        var openLine = lines[startIndex];
        var contentStart = openLine.EndIncludingLineBreak;

        for (var index = startIndex + 1; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!TryMatchFenceClose(line.Text, marker))
            {
                continue;
            }

            block = new CodeFenceBlock(
                TextSpan.FromBounds(openLine.Span.Start, line.EndIncludingLineBreak),
                openLine.Span,
                TextSpan.FromBounds(contentStart, line.Span.Start),
                language);
            consumedLines = index - startIndex + 1;
            return true;
        }

        var lastLine = lines[^1];
        block = new CodeFenceBlock(
            TextSpan.FromBounds(openLine.Span.Start, lastLine.EndIncludingLineBreak),
            openLine.Span,
            TextSpan.FromBounds(contentStart, lastLine.EndIncludingLineBreak),
            language);
        consumedLines = lines.Count - startIndex;
        return true;
    }

    private static ParagraphBlock ParseProjectedParagraph(
        SourceText text,
        IReadOnlyList<ProjectedLine> lines,
        int startIndex,
        out int consumedLines)
    {
        var paragraphLines = new List<ProjectedLine>();
        var index = startIndex;
        while (index < lines.Count)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                break;
            }

            if (index != startIndex && LooksLikeBlockStart(line.Text))
            {
                break;
            }

            paragraphLines.Add(line);
            index++;
        }

        consumedLines = Math.Max(1, index - startIndex);
        var span = TextSpan.FromBounds(paragraphLines[0].Span.Start, paragraphLines[^1].Span.End);
        return new ParagraphBlock(span, ParseProjectedInlines(text, paragraphLines));
    }

    private static IReadOnlyList<MdInline> ParseProjectedInlines(SourceText text, IReadOnlyList<ProjectedLine> lines)
    {
        var inlines = new List<MdInline>();
        foreach (var line in lines)
        {
            inlines.AddRange(ParseInlines(text, line.Span));
        }

        return inlines;
    }

    private static List<ProjectedLine> CreateProjectedLines(SourceText text, int startLine, int end)
    {
        var result = new List<ProjectedLine>();
        for (var lineIndex = startLine; lineIndex < text.Lines.Count; lineIndex++)
        {
            var line = text.Lines[lineIndex];
            if (line.Start >= end)
            {
                break;
            }

            result.Add(new ProjectedLine(text.ToString(line.Span), line.Span, line.EndIncludingLineBreak));
            if (line.EndIncludingLineBreak >= end)
            {
                break;
            }
        }

        return result;
    }

    private static bool TryStripQuoteMarker(ProjectedLine line, out ProjectedLine contentLine)
    {
        contentLine = default;

        var leadingSpaces = CountLeadingSpaces(line.Text);
        if (leadingSpaces >= line.Text.Length || line.Text[leadingSpaces] != '>')
        {
            return false;
        }

        var contentOffset = leadingSpaces + 1;
        if (contentOffset < line.Text.Length && line.Text[contentOffset] == ' ')
        {
            contentOffset++;
        }

        contentLine = line.Slice(contentOffset);
        return true;
    }

    private static bool TryMatchListMarker(
        string text,
        out bool ordered,
        out int indent,
        out int markerLength,
        out int contentOffset)
    {
        ordered = false;
        indent = CountLeadingSpaces(text);
        markerLength = 0;
        contentOffset = 0;

        if (indent >= text.Length)
        {
            return false;
        }

        var marker = text[indent];
        if ((marker == '-' || marker == '*' || marker == '+') &&
            indent + 1 < text.Length &&
            text[indent + 1] == ' ')
        {
            markerLength = 2;
            contentOffset = indent + markerLength;
            return true;
        }

        var index = indent;
        while (index < text.Length && char.IsDigit(text[index]))
        {
            index++;
        }

        if (index == indent ||
            index + 1 >= text.Length ||
            text[index] != '.' ||
            text[index + 1] != ' ')
        {
            return false;
        }

        ordered = true;
        markerLength = index - indent + 2;
        contentOffset = indent + markerLength;
        return true;
    }

    private static bool IsOrderedListMarker(string text) =>
        TryMatchListMarker(text, out var ordered, out _, out _, out _) && ordered;

    private static bool HasListContinuation(
        IReadOnlyList<ProjectedLine> lines,
        int startIndex,
        int baseIndent,
        int itemContentOffset)
    {
        for (var index = startIndex; index < lines.Count; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            if (TryMatchListMarker(line.Text, out _, out var indent, out _, out _) &&
                indent <= baseIndent)
            {
                return false;
            }

            return CountLeadingSpaces(line.Text) >= itemContentOffset;
        }

        return false;
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

    private static IReadOnlyList<MdInline> ParseInlineRange(string content, int baseOffset)
    {
        var inlines = new List<MdInline>();
        var textStart = 0;
        var index = 0;

        while (index < content.Length)
        {
            if (TryParseCodeInline(content, baseOffset, ref index, inlines, ref textStart) ||
                TryParseDelimitedInline(content, baseOffset, ref index, inlines, ref textStart, "**", static (span, children) => new StrongInline(span, children)) ||
                TryParseDelimitedInline(content, baseOffset, ref index, inlines, ref textStart, "__", static (span, children) => new StrongInline(span, children)) ||
                TryParseDelimitedInline(content, baseOffset, ref index, inlines, ref textStart, "*", static (span, children) => new EmphasisInline(span, children)) ||
                TryParseDelimitedInline(content, baseOffset, ref index, inlines, ref textStart, "_", static (span, children) => new EmphasisInline(span, children)) ||
                TryParseLinkInline(content, baseOffset, ref index, inlines, ref textStart))
            {
                continue;
            }

            index++;
        }

        AppendTextInline(inlines, baseOffset, content, textStart, content.Length);
        return inlines;
    }

    private static bool TryParseCodeInline(
        string content,
        int baseOffset,
        ref int index,
        List<MdInline> inlines,
        ref int textStart)
    {
        if (content[index] != '`')
        {
            return false;
        }

        var closingIndex = content.IndexOf('`', index + 1);
        if (closingIndex <= index + 1)
        {
            return false;
        }

        AppendTextInline(inlines, baseOffset, content, textStart, index);
        inlines.Add(new CodeInline(TextSpan.FromBounds(baseOffset + index, baseOffset + closingIndex + 1)));
        index = closingIndex + 1;
        textStart = index;
        return true;
    }

    private static bool TryParseDelimitedInline(
        string content,
        int baseOffset,
        ref int index,
        List<MdInline> inlines,
        ref int textStart,
        string delimiter,
        Func<TextSpan, IReadOnlyList<MdInline>, MdInline> factory)
    {
        if (!content.AsSpan(index).StartsWith(delimiter, StringComparison.Ordinal))
        {
            return false;
        }

        var closingIndex = FindClosingDelimiter(content, index, delimiter);
        if (closingIndex <= index + delimiter.Length)
        {
            return false;
        }

        AppendTextInline(inlines, baseOffset, content, textStart, index);
        var innerStart = index + delimiter.Length;
        var innerSpan = TextSpan.FromBounds(baseOffset + innerStart, baseOffset + closingIndex);
        var fullSpan = TextSpan.FromBounds(baseOffset + index, baseOffset + closingIndex + delimiter.Length);
        var children = ParseInlineRange(content[innerStart..closingIndex], baseOffset + innerStart);
        inlines.Add(factory(fullSpan, children));
        index = closingIndex + delimiter.Length;
        textStart = index;
        return true;
    }

    private static bool TryParseLinkInline(
        string content,
        int baseOffset,
        ref int index,
        List<MdInline> inlines,
        ref int textStart)
    {
        if (content[index] != '[')
        {
            return false;
        }

        var closingBracket = content.IndexOf(']', index + 1);
        if (closingBracket <= index + 1 ||
            closingBracket + 2 >= content.Length ||
            content[closingBracket + 1] != '(')
        {
            return false;
        }

        var closingParen = content.IndexOf(')', closingBracket + 2);
        if (closingParen <= closingBracket + 2)
        {
            return false;
        }

        AppendTextInline(inlines, baseOffset, content, textStart, index);
        var labelSpan = TextSpan.FromBounds(baseOffset + index + 1, baseOffset + closingBracket);
        var fullSpan = TextSpan.FromBounds(baseOffset + index, baseOffset + closingParen + 1);
        var destination = content[(closingBracket + 2)..closingParen];
        var children = ParseInlineRange(content[(index + 1)..closingBracket], baseOffset + index + 1);
        inlines.Add(new LinkInline(fullSpan, labelSpan, destination, children));
        index = closingParen + 1;
        textStart = index;
        return true;
    }

    private static int FindClosingDelimiter(string content, int startIndex, string delimiter)
    {
        var searchIndex = startIndex + delimiter.Length;
        while (searchIndex < content.Length)
        {
            var candidate = content.IndexOf(delimiter, searchIndex, StringComparison.Ordinal);
            if (candidate < 0)
            {
                return -1;
            }

            if (delimiter.Length > 1 &&
                delimiter.All(ch => ch == delimiter[0]) &&
                candidate + delimiter.Length < content.Length &&
                content[candidate + delimiter.Length] == delimiter[0])
            {
                searchIndex = candidate + 1;
                continue;
            }

            return candidate;
        }

        return -1;
    }

    private static void AppendTextInline(List<MdInline> inlines, int baseOffset, string content, int start, int end)
    {
        if (end <= start)
        {
            return;
        }

        inlines.Add(new TextInline(TextSpan.FromBounds(baseOffset + start, baseOffset + end)));
    }

    private readonly record struct ProjectedLine(string Text, TextSpan Span, int EndIncludingLineBreak)
    {
        public ProjectedLine Slice(int offset)
        {
            var safeOffset = Math.Clamp(offset, 0, Text.Length);
            return new ProjectedLine(
                Text[safeOffset..],
                TextSpan.FromBounds(Span.Start + safeOffset, Span.End),
                EndIncludingLineBreak);
        }
    }
}
