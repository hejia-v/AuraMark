using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed class ReparseWindowExpander
{
    public ReparseWindow ExpandToSafeWindow(SourceText text, ParseResult? previous, TextSpan dirtySpan)
    {
        if (text.Length == 0)
        {
            return new ReparseWindow(dirtySpan, new TextSpan(0, 0));
        }

        var safeStart = Math.Clamp(dirtySpan.Start, 0, text.Length);
        var safeEndExclusive = Math.Clamp(Math.Max(dirtySpan.End, dirtySpan.Start), 0, text.Length);
        var startLine = text.Lines.GetLineFromPosition(safeStart).LineNumber;
        var endLine = text.Lines.GetLineFromPosition(Math.Max(0, safeEndExclusive == 0 ? 0 : safeEndExclusive - 1)).LineNumber;

        startLine = ExpandUp(text, startLine);
        endLine = ExpandDown(text, endLine);

        var start = text.Lines[startLine].Start;
        var end = text.Lines[endLine].EndIncludingLineBreak;
        return new ReparseWindow(dirtySpan, TextSpan.FromBounds(start, end));
    }

    private int ExpandUp(SourceText text, int lineNumber)
    {
        var index = lineNumber;
        while (index > 0)
        {
            var current = GetLineText(text, index);
            var previous = GetLineText(text, index - 1);
            if (IsHardBoundary(previous))
            {
                break;
            }

            if (LooksLikeFenceBoundary(previous) || LooksLikeFenceBoundary(current))
            {
                index = ExpandFenceUp(text, index);
                break;
            }

            if (LooksLikeTableRegion(previous, current) ||
                LooksLikeQuoteLine(previous) ||
                LooksLikeListLine(previous) ||
                IsContinuationLine(previous) ||
                LooksLikeParagraphContinuation(previous, current))
            {
                index--;
                continue;
            }

            break;
        }

        return index;
    }

    private int ExpandDown(SourceText text, int lineNumber)
    {
        var index = lineNumber;
        var max = text.Lines.Count - 1;
        while (index < max)
        {
            var current = GetLineText(text, index);
            var next = GetLineText(text, index + 1);
            if (IsHardBoundary(next))
            {
                break;
            }

            if (LooksLikeFenceBoundary(current) || LooksLikeFenceBoundary(next))
            {
                index = ExpandFenceDown(text, index);
                break;
            }

            if (LooksLikeTableRegion(current, next) ||
                LooksLikeQuoteLine(next) ||
                LooksLikeListLine(next) ||
                IsContinuationLine(next) ||
                LooksLikeParagraphContinuation(current, next))
            {
                index++;
                continue;
            }

            break;
        }

        return index;
    }

    private static string GetLineText(SourceText text, int lineNumber) => text.ToString(text.Lines[lineNumber].Span);

    private static bool IsHardBoundary(string line) => string.IsNullOrWhiteSpace(line) || IsAtxHeading(line);

    private static bool IsAtxHeading(string line)
    {
        var trimmed = line.TrimStart();
        var hashes = 0;
        while (hashes < trimmed.Length && hashes < 6 && trimmed[hashes] == '#')
        {
            hashes++;
        }

        return hashes > 0 && hashes < trimmed.Length && trimmed[hashes] == ' ';
    }

    private int ExpandFenceUp(SourceText text, int lineNumber)
    {
        var index = lineNumber;
        while (index > 0)
        {
            if (LooksLikeFenceBoundary(GetLineText(text, index)))
            {
                return index;
            }

            index--;
        }

        return 0;
    }

    private int ExpandFenceDown(SourceText text, int lineNumber)
    {
        var index = lineNumber;
        var max = text.Lines.Count - 1;
        while (index < max)
        {
            if (LooksLikeFenceBoundary(GetLineText(text, index)))
            {
                return index;
            }

            index++;
        }

        return max;
    }

    private static bool LooksLikeFenceBoundary(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }

    private static bool LooksLikeQuoteLine(string line) => line.TrimStart().StartsWith(">", StringComparison.Ordinal);

    private static bool LooksLikeListLine(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
            trimmed.StartsWith("* ", StringComparison.Ordinal) ||
            trimmed.StartsWith("+ ", StringComparison.Ordinal))
        {
            return true;
        }

        var index = 0;
        while (index < trimmed.Length && char.IsDigit(trimmed[index]))
        {
            index++;
        }

        return index > 0 &&
               index + 1 < trimmed.Length &&
               trimmed[index] == '.' &&
               trimmed[index + 1] == ' ';
    }

    private static bool IsContinuationLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var indent = 0;
        while (indent < line.Length && line[indent] == ' ')
        {
            indent++;
        }

        return indent >= 2;
    }

    private static bool LooksLikeTableRegion(string current, string next) =>
        LooksLikeTableLine(current) || LooksLikeTableLine(next);

    private static bool LooksLikeTableLine(string line)
    {
        var trimmed = line.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Contains('|');
    }

    private static bool LooksLikeParagraphContinuation(string previous, string current)
    {
        if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(current))
        {
            return false;
        }

        if (LooksLikeFenceBoundary(previous) || LooksLikeFenceBoundary(current))
        {
            return false;
        }

        if (LooksLikeQuoteLine(current) || LooksLikeListLine(current) || LooksLikeTableLine(current))
        {
            return false;
        }

        return !IsAtxHeading(current);
    }
}
