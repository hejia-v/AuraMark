using Microsoft.CodeAnalysis.Text;
using AuraMark.Core.Text;

namespace AuraMark.Core.Syntax;

public sealed class MarkdownParser : IMarkdownParser, IIncrementalMarkdownParser
{
    private readonly ReparseWindowExpander _expander = new();

    public ParseResult Parse(DocumentSnapshot snapshot) => FullParse(snapshot);
    public ReparseWindow ExpandToSafeWindow(SourceText text, ParseResult? previous, TextSpan dirtySpan) => _expander.ExpandToSafeWindow(text, previous, dirtySpan);

    public ParseResult Parse(DocumentSnapshot snapshot, ParseResult? previous, TextSpan dirtySpan)
    {
        if (previous is null) return FullParse(snapshot);
        var blocks = ParseBlocks(snapshot.Text, 0, snapshot.Text.Length);
        return new ParseResult(snapshot, blocks, BuildOutline(snapshot.Text, blocks), Array.Empty<ParseDiagnostic>(), BuildBlockMap(blocks));
    }

    private ParseResult FullParse(DocumentSnapshot snapshot)
    {
        var blocks = ParseBlocks(snapshot.Text, 0, snapshot.Text.Length);
        return new ParseResult(snapshot, blocks, BuildOutline(snapshot.Text, blocks), Array.Empty<ParseDiagnostic>(), BuildBlockMap(blocks));
    }

    private IReadOnlyList<MdBlock> ParseBlocks(SourceText text, int start, int end)
    {
        var blocks = new List<MdBlock>();
        var cursor = start;
        while (cursor < end)
        {
            var line = text.Lines.GetLineFromPosition(cursor);
            var lineText = text.ToString(line.Span);
            if (string.IsNullOrWhiteSpace(lineText)) { cursor = line.EndIncludingLineBreak; continue; }
            if (TryParseFence(text, line.LineNumber, end, out var fence, out var nextFencePos)) { blocks.Add(fence); cursor = nextFencePos; continue; }
            if (TryParseHeading(text, line.LineNumber, out var heading)) { blocks.Add(heading); cursor = line.EndIncludingLineBreak; continue; }
            var paragraph = ParseParagraph(text, line.LineNumber, end, out var nextParagraphPos); blocks.Add(paragraph); cursor = nextParagraphPos;
        }
        return blocks;
    }

    private bool TryParseFence(SourceText text, int startLine, int end, out CodeFenceBlock block, out int nextPos)
    {
        block = default!; nextPos = 0;
        var openLine = text.Lines[startLine];
        var openText = text.ToString(openLine.Span);
        if (!TryMatchFenceOpen(openText, out var marker, out var markerLen, out var language)) return false;
        var contentStart = openLine.EndIncludingLineBreak;
        int i = startLine + 1;
        while (i < text.Lines.Count)
        {
            var line = text.Lines[i];
            var lineText = text.ToString(line.Span);
            if (TryMatchFenceClose(lineText, marker, markerLen))
            {
                var span = TextSpan.FromBounds(openLine.Start, line.EndIncludingLineBreak);
                var contentSpan = TextSpan.FromBounds(contentStart, line.Start);
                block = new CodeFenceBlock(span, openLine.Span, contentSpan, language);
                nextPos = line.EndIncludingLineBreak;
                return true;
            }
            if (line.Start >= end) break;
            i++;
        }
        var lastLine = text.Lines[Math.Min(i - 1, text.Lines.Count - 1)];
        var fallbackSpan = TextSpan.FromBounds(openLine.Start, lastLine.EndIncludingLineBreak);
        block = new CodeFenceBlock(fallbackSpan, openLine.Span, TextSpan.FromBounds(contentStart, fallbackSpan.End), language);
        nextPos = fallbackSpan.End;
        return true;
    }

    private bool TryParseHeading(SourceText text, int lineNumber, out HeadingBlock block)
    {
        block = default!;
        var line = text.Lines[lineNumber];
        var s = text.ToString(line.Span);
        int level = CountLeadingHashes(s);
        if (level is < 1 or > 6) return false;
        if (s.Length <= level || s[level] != ' ') return false;
        var inlineSpan = TextSpan.FromBounds(line.Start + level + 1, line.End);
        var inlines = ParseInlines(text, inlineSpan);
        block = new HeadingBlock(line.Span, level, inlines);
        return true;
    }

    private ParagraphBlock ParseParagraph(SourceText text, int startLine, int end, out int nextPos)
    {
        int i = startLine;
        int start = text.Lines[startLine].Start;
        int paragraphEnd = start;
        while (i < text.Lines.Count)
        {
            var line = text.Lines[i];
            var lineText = text.ToString(line.Span);
            if (string.IsNullOrWhiteSpace(lineText)) break;
            if (i != startLine && LooksLikeBlockStart(lineText)) break;
            paragraphEnd = line.End;
            if (line.EndIncludingLineBreak >= end) break;
            i++;
        }
        var span = TextSpan.FromBounds(start, paragraphEnd);
        var inlines = ParseInlines(text, span);
        nextPos = i < text.Lines.Count ? text.Lines[i].Start : text.Length;
        return new ParagraphBlock(span, inlines);
    }

    private IReadOnlyList<MdInline> ParseInlines(SourceText text, TextSpan span) => new MdInline[] { new TextInline(span) };
    private static bool LooksLikeBlockStart(string line) { var t = line.TrimStart(); return t.StartsWith("#") || t.StartsWith("```") || t.StartsWith("~~~") || t.StartsWith(">") || t.StartsWith("- ") || t.StartsWith("* ") || t.StartsWith("+ "); }
    private static bool TryMatchFenceOpen(string text, out string marker, out int markerLen, out string? language) { marker = ""; markerLen = 0; language = null; var t = text.TrimStart(); if (t.StartsWith("```")) { marker = "`"; markerLen = 3; language = t[3..].Trim(); return true; } if (t.StartsWith("~~~")) { marker = "~"; markerLen = 3; language = t[3..].Trim(); return true; } return false; }
    private static bool TryMatchFenceClose(string text, string marker, int markerLen) { var t = text.TrimStart(); return marker == "`" ? t.StartsWith("```") : t.StartsWith("~~~"); }
    private static int CountLeadingHashes(string s) { int i = 0; while (i < s.Length && i < 6 && s[i] == '#') i++; return i; }
    private static IReadOnlyList<OutlineItem> BuildOutline(SourceText text, IReadOnlyList<MdBlock> blocks) => blocks.OfType<HeadingBlock>().Select(h => new OutlineItem(text.ToString(h.Span).TrimStart('#', ' '), h.Level, h.Span)).ToArray();
    private static IReadOnlyList<BlockMapEntry> BuildBlockMap(IReadOnlyList<MdBlock> blocks) => blocks.Select((b, i) => new BlockMapEntry(b.Span, i, b.Kind)).ToArray();
}
