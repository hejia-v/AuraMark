using System.Text;
using System.Text.RegularExpressions;

namespace AuraMark.Core;

public static partial class TextLayoutPreserver
{
    public static string MergePreservingLayout(string? originalText, string? editedText)
    {
        var safeOriginal = originalText ?? string.Empty;
        var safeEdited = editedText ?? string.Empty;
        if (string.Equals(safeOriginal, safeEdited, StringComparison.Ordinal))
        {
            return safeOriginal;
        }

        var normalizedOriginal = NormalizeLineEndings(safeOriginal);
        var normalizedEdited = NormalizeLineEndings(safeEdited);
        if (string.Equals(normalizedOriginal, normalizedEdited, StringComparison.Ordinal))
        {
            return safeOriginal;
        }

        var originalLines = TokenizeRawLines(safeOriginal);
        var editedLines = TokenizeNormalizedLines(normalizedEdited);
        var operations = BuildDiff(
            originalLines.Select(line => NormalizeComparableLine(line.Normalized)).ToArray(),
            editedLines.Select(NormalizeComparableLine).ToArray());
        var preferredLineEnding = DetectPreferredLineEnding(originalLines);
        var builder = new StringBuilder(Math.Max(safeOriginal.Length, safeEdited.Length));

        for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
        {
            var operation = operations[operationIndex];
            switch (operation.Kind)
            {
                case DiffKind.Equal:
                    builder.Append(originalLines[operation.OriginalIndex].Raw);
                    break;
                case DiffKind.Delete:
                    if (ShouldPreserveDeletedBlankLine(
                            operations,
                            operationIndex,
                            originalLines,
                            editedLines))
                    {
                        builder.Append(originalLines[operation.OriginalIndex].Raw);
                    }
                    break;
                case DiffKind.Insert:
                    builder.Append(ApplyPreferredLineEnding(editedLines[operation.EditedIndex], preferredLineEnding));
                    break;
            }
        }

        return RemoveRedundantBlankLinesBeforeStructuralMarkers(builder.ToString());
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static List<RawLineToken> TokenizeRawLines(string text)
    {
        var tokens = new List<RawLineToken>();
        if (text.Length == 0)
        {
            return tokens;
        }

        var lineStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is not ('\r' or '\n'))
            {
                continue;
            }

            var lineBreakLength = 1;
            if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                lineBreakLength = 2;
            }

            var contentLength = i - lineStart;
            var rawLength = contentLength + lineBreakLength;
            var content = text.Substring(lineStart, contentLength);
            var raw = text.Substring(lineStart, rawLength);
            tokens.Add(new RawLineToken(raw, $"{content}\n"));

            i += lineBreakLength - 1;
            lineStart = i + 1;
        }

        if (lineStart < text.Length)
        {
            var raw = text[lineStart..];
            tokens.Add(new RawLineToken(raw, raw));
        }

        return tokens;
    }

    private static string[] TokenizeNormalizedLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var tokens = new List<string>();
        var lineStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            tokens.Add(text.Substring(lineStart, i - lineStart + 1));
            lineStart = i + 1;
        }

        if (lineStart < text.Length)
        {
            tokens.Add(text[lineStart..]);
        }

        return [.. tokens];
    }

    private static string DetectPreferredLineEnding(IReadOnlyList<RawLineToken> originalLines)
    {
        var crlfCount = 0;
        var lfCount = 0;
        var crCount = 0;

        foreach (var line in originalLines)
        {
            if (line.Raw.EndsWith("\r\n", StringComparison.Ordinal))
            {
                crlfCount++;
            }
            else if (line.Raw.EndsWith('\n'))
            {
                lfCount++;
            }
            else if (line.Raw.EndsWith('\r'))
            {
                crCount++;
            }
        }

        if (crlfCount >= lfCount && crlfCount >= crCount && crlfCount > 0)
        {
            return "\r\n";
        }

        if (lfCount >= crCount && lfCount > 0)
        {
            return "\n";
        }

        if (crCount > 0)
        {
            return "\r";
        }

        return Environment.NewLine;
    }

    private static string ApplyPreferredLineEnding(string token, string preferredLineEnding)
    {
        return token.EndsWith('\n')
            ? token[..^1] + preferredLineEnding
            : token;
    }

    private static bool IsBlankComparableLine(string line)
    {
        return string.IsNullOrWhiteSpace(NormalizeComparableLine(line));
    }

    private static bool ShouldPreserveDeletedBlankLine(
        IReadOnlyList<DiffOperation> operations,
        int operationIndex,
        IReadOnlyList<RawLineToken> originalLines,
        IReadOnlyList<string> editedLines)
    {
        var operation = operations[operationIndex];
        if (!IsBlankComparableLine(originalLines[operation.OriginalIndex].Normalized))
        {
            return false;
        }

        return !HasAdjacentFormattingOnlyInsert(operations, operationIndex, editedLines);
    }

    private static bool HasAdjacentFormattingOnlyInsert(
        IReadOnlyList<DiffOperation> operations,
        int operationIndex,
        IReadOnlyList<string> editedLines)
    {
        for (var direction = -1; direction <= 1; direction += 2)
        {
            for (var index = operationIndex + direction; index >= 0 && index < operations.Count; index += direction)
            {
                var operation = operations[index];
                if (operation.Kind == DiffKind.Equal)
                {
                    break;
                }

                if (operation.Kind == DiffKind.Insert &&
                    IsFormattingOnlyComparableLine(editedLines[operation.EditedIndex]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsFormattingOnlyComparableLine(string line)
    {
        var normalized = NormalizeComparableLine(line);
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }

        normalized = normalized.Trim();
        return StructuralMarkerOnlyRegex().IsMatch(normalized);
    }

    private static string RemoveRedundantBlankLinesBeforeStructuralMarkers(string text)
    {
        var lines = TokenizeRawLines(text);
        if (lines.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < lines.Count; index++)
        {
            var current = lines[index];
            var previousIsBlank = index > 0 && IsBlankComparableLine(lines[index - 1].Normalized);
            var nextIsMarkerOnly = index + 1 < lines.Count && IsFormattingOnlyComparableLine(lines[index + 1].Normalized);
            if (IsBlankComparableLine(current.Normalized) &&
                !previousIsBlank &&
                nextIsMarkerOnly)
            {
                continue;
            }

            builder.Append(current.Raw);
        }

        return builder.ToString();
    }

    private static string NormalizeComparableLine(string line)
    {
        var hasTrailingLineFeed = line.EndsWith('\n');
        var content = hasTrailingLineFeed ? line[..^1] : line;
        content = content.TrimEnd('\u00A0', ' ', '\t');
        content = content.Replace("\\`", "`", StringComparison.Ordinal);
        content = BulletMarkerRegex().Replace(content, "$1-");
        content = OrderedListMarkerRegex().Replace(content, "${1}1.");
        content = PlaceholderBreakRegex().Replace(content, "$1");
        return content;
    }

    private static List<DiffOperation> BuildDiff(IReadOnlyList<string> originalLines, IReadOnlyList<string> editedLines)
    {
        var originalCount = originalLines.Count;
        var editedCount = editedLines.Count;
        var max = originalCount + editedCount;
        var trace = new List<Dictionary<int, int>>(max + 1);
        var frontier = new Dictionary<int, int> { [1] = 0 };

        for (var depth = 0; depth <= max; depth++)
        {
            var current = new Dictionary<int, int>();
            for (var diagonal = -depth; diagonal <= depth; diagonal += 2)
            {
                int x;
                if (diagonal == -depth ||
                    (diagonal != depth && GetX(frontier, diagonal - 1) < GetX(frontier, diagonal + 1)))
                {
                    x = GetX(frontier, diagonal + 1);
                }
                else
                {
                    x = GetX(frontier, diagonal - 1) + 1;
                }

                var y = x - diagonal;
                while (x < originalCount &&
                       y < editedCount &&
                       string.Equals(originalLines[x], editedLines[y], StringComparison.Ordinal))
                {
                    x++;
                    y++;
                }

                current[diagonal] = x;
                if (x >= originalCount && y >= editedCount)
                {
                    trace.Add(current);
                    return Backtrack(trace, originalCount, editedCount);
                }
            }

            trace.Add(current);
            frontier = current;
        }

        return [];
    }

    private static List<DiffOperation> Backtrack(
        IReadOnlyList<Dictionary<int, int>> trace,
        int originalCount,
        int editedCount)
    {
        var x = originalCount;
        var y = editedCount;
        var operations = new List<DiffOperation>();

        for (var depth = trace.Count - 1; depth >= 0; depth--)
        {
            if (depth == 0)
            {
                while (x > 0 && y > 0)
                {
                    operations.Add(new DiffOperation(DiffKind.Equal, x - 1, y - 1));
                    x--;
                    y--;
                }

                while (x > 0)
                {
                    operations.Add(new DiffOperation(DiffKind.Delete, x - 1, -1));
                    x--;
                }

                while (y > 0)
                {
                    operations.Add(new DiffOperation(DiffKind.Insert, -1, y - 1));
                    y--;
                }

                break;
            }

            var diagonal = x - y;
            var previous = trace[depth - 1];
            int previousDiagonal;
            if (diagonal == -depth ||
                (diagonal != depth && GetX(previous, diagonal - 1) < GetX(previous, diagonal + 1)))
            {
                previousDiagonal = diagonal + 1;
            }
            else
            {
                previousDiagonal = diagonal - 1;
            }

            var previousX = GetX(previous, previousDiagonal);
            var previousY = previousX - previousDiagonal;

            while (x > previousX && y > previousY)
            {
                operations.Add(new DiffOperation(DiffKind.Equal, x - 1, y - 1));
                x--;
                y--;
            }

            if (x == previousX)
            {
                operations.Add(new DiffOperation(DiffKind.Insert, -1, y - 1));
                y--;
            }
            else
            {
                operations.Add(new DiffOperation(DiffKind.Delete, x - 1, -1));
                x--;
            }
        }

        operations.Reverse();
        return operations;
    }

    private static int GetX(IReadOnlyDictionary<int, int> frontier, int diagonal)
    {
        return frontier.TryGetValue(diagonal, out var x) ? x : 0;
    }

    private readonly record struct RawLineToken(string Raw, string Normalized);

    private readonly record struct DiffOperation(DiffKind Kind, int OriginalIndex, int EditedIndex);

    private enum DiffKind
    {
        Equal,
        Delete,
        Insert,
    }

    [GeneratedRegex(@"^(\s*)[*+-](?=\s|$)", RegexOptions.Compiled)]
    private static partial Regex BulletMarkerRegex();

    [GeneratedRegex(@"^(\s*)\d+[.)](?=\s|$)", RegexOptions.Compiled)]
    private static partial Regex OrderedListMarkerRegex();

    [GeneratedRegex(@"^(\s*(?:[-+*]|\d+[.)]))\s+<br\s*/?>\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PlaceholderBreakRegex();

    [GeneratedRegex(@"^(?:[-+*]|1[.)]|>)$", RegexOptions.Compiled)]
    private static partial Regex StructuralMarkerOnlyRegex();
}
