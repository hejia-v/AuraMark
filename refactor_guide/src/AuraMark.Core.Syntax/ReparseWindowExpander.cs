using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed class ReparseWindowExpander
{
    public ReparseWindow ExpandToSafeWindow(SourceText text, ParseResult? previous, TextSpan dirtySpan)
    {
        if (text.Length == 0) return new ReparseWindow(dirtySpan, new TextSpan(0, 0));

        int safeStart = Math.Clamp(dirtySpan.Start, 0, text.Length);
        int safeEndExclusive = Math.Clamp(Math.Max(dirtySpan.End, dirtySpan.Start), 0, text.Length);
        int startLine = text.Lines.GetLineFromPosition(safeStart).LineNumber;
        int endLine = text.Lines.GetLineFromPosition(Math.Max(0, safeEndExclusive == 0 ? 0 : safeEndExclusive - 1)).LineNumber;

        startLine = ExpandUp(text, startLine);
        endLine = ExpandDown(text, endLine);

        int start = text.Lines[startLine].Start;
        int end = text.Lines[endLine].EndIncludingLineBreak;

        return new ReparseWindow(dirtySpan, TextSpan.FromBounds(start, end));
    }

    private int ExpandUp(SourceText text, int lineNumber)
    {
        int i = lineNumber;
        while (i > 0)
        {
            var current = GetLineText(text, i);
            var prev = GetLineText(text, i - 1);
            if (IsHardBoundary(prev)) break;
            if (LooksLikeFenceBoundary(prev) || LooksLikeFenceBoundary(current)) { i = ExpandFenceUp(text, i); break; }
            if (LooksLikeTableRegion(prev, current) || LooksLikeQuoteLine(prev) || LooksLikeListLine(prev) || IsContinuationLine(prev) || LooksLikeParagraphContinuation(prev, current)) { i--; continue; }
            break;
        }
        return i;
    }

    private int ExpandDown(SourceText text, int lineNumber)
    {
        int i = lineNumber;
        int max = text.Lines.Count - 1;
        while (i < max)
        {
            var current = GetLineText(text, i);
            var next = GetLineText(text, i + 1);
            if (IsHardBoundary(next)) break;
            if (LooksLikeFenceBoundary(current) || LooksLikeFenceBoundary(next)) { i = ExpandFenceDown(text, i); break; }
            if (LooksLikeTableRegion(current, next) || LooksLikeQuoteLine(next) || LooksLikeListLine(next) || IsContinuationLine(next) || LooksLikeParagraphContinuation(current, next)) { i++; continue; }
            break;
        }
        return i;
    }

    private static string GetLineText(SourceText text, int lineNumber) => text.ToString(text.Lines[lineNumber].Span);
    private static bool IsHardBoundary(string line) => string.IsNullOrWhiteSpace(line) || IsAtxHeading(line);
    private static bool IsAtxHeading(string line) { var t = line.TrimStart(); int hashes = 0; while (hashes < t.Length && hashes < 6 && t[hashes] == '#') hashes++; return hashes > 0 && hashes < t.Length && t[hashes] == ' '; }
    private int ExpandFenceUp(SourceText text, int lineNumber) { int i = lineNumber; while (i > 0) { if (LooksLikeFenceBoundary(GetLineText(text, i))) return i; i--; } return 0; }
    private int ExpandFenceDown(SourceText text, int lineNumber) { int i = lineNumber; int max = text.Lines.Count - 1; while (i < max) { if (LooksLikeFenceBoundary(GetLineText(text, i))) return i; i++; } return max; }
    private static bool LooksLikeFenceBoundary(string line) { var t = line.TrimStart(); return t.StartsWith("```") || t.StartsWith("~~~"); }
    private static bool LooksLikeQuoteLine(string line) => line.TrimStart().StartsWith(">");
    private static bool LooksLikeListLine(string line) { var t = line.TrimStart(); if (t.StartsWith("- ") || t.StartsWith("* ") || t.StartsWith("+ ")) return true; int i = 0; while (i < t.Length && char.IsDigit(t[i])) i++; return i > 0 && i + 1 < t.Length && t[i] == '.' && t[i + 1] == ' '; }
    private static bool IsContinuationLine(string line) { if (string.IsNullOrWhiteSpace(line)) return false; int indent = 0; while (indent < line.Length && line[indent] == ' ') indent++; return indent >= 2; }
    private static bool LooksLikeTableRegion(string a, string b) => LooksLikeTableLine(a) || LooksLikeTableLine(b);
    private static bool LooksLikeTableLine(string line) { var t = line.Trim(); return !string.IsNullOrEmpty(t) && t.Contains('|'); }
    private static bool LooksLikeParagraphContinuation(string prev, string current) { if (string.IsNullOrWhiteSpace(prev) || string.IsNullOrWhiteSpace(current)) return false; if (LooksLikeFenceBoundary(prev) || LooksLikeFenceBoundary(current)) return false; if (LooksLikeQuoteLine(current) || LooksLikeListLine(current) || LooksLikeTableLine(current)) return false; if (IsAtxHeading(current)) return false; return true; }
}
