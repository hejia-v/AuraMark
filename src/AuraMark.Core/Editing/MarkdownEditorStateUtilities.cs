using System.Text.Json;
using System.Text.RegularExpressions;
using AuraMark.Core.Text;

namespace AuraMark.Core.Editing;

internal static class MarkdownEditorPatterns
{
    internal static readonly Regex OrderedListRegex = new(@"^\s*\d+[.)]\s+", RegexOptions.Compiled);
    internal static readonly Regex UnorderedListRegex = new(@"^\s*[-+*]\s+", RegexOptions.Compiled);
    internal static readonly Regex TaskListRegex = new(@"^\s*[-+*]\s+\[(?: |x|X)\]\s+", RegexOptions.Compiled);
    internal static readonly Regex QuoteRegex = new(@"^\s*>\s+", RegexOptions.Compiled);
    internal static readonly Regex HeadingRegex = new(@"^\s{0,3}(#{1,6})\s+", RegexOptions.Compiled);
    internal static readonly Regex FenceRegex = new(@"^\s*```", RegexOptions.Compiled);
}

internal readonly record struct MarkdownEditorLineRange(int Start, int EndOffset);

internal readonly record struct MarkdownEditorLineSelection(int StartOffset, int Length, string[] Lines);

internal static class MarkdownEditorStateUtilities
{
    public static MarkdownEditorState NormalizeState(MarkdownEditorState state)
    {
        var text = state.Text ?? string.Empty;
        var max = text.Length;
        var selection = new TextSelection(
            Math.Clamp(state.Selection.Anchor, 0, max),
            Math.Clamp(state.Selection.Active, 0, max));

        return state with { Text = text, Selection = selection };
    }

    public static string GetSelectedTextOrPlaceholder(MarkdownEditorState state, string placeholder) =>
        state.Selection.IsCollapsed ? placeholder : GetSelectedText(state);

    public static string GetSelectedText(MarkdownEditorState state) =>
        state.Text[state.Selection.Start..state.Selection.End];

    public static MarkdownEditorLineSelection GetLineSelection(MarkdownEditorState state)
    {
        if (state.Text.Length == 0)
        {
            return new MarkdownEditorLineSelection(0, 0, [string.Empty]);
        }

        var lines = EnumerateLines(state.Text);
        var start = state.Selection.Start;
        var end = state.Selection.End;
        var startAnchor = Math.Min(start, Math.Max(0, state.Text.Length - 1));
        var endAnchor = Math.Min(Math.Max(start, end > start ? end - 1 : start), Math.Max(0, state.Text.Length - 1));
        var startIndex = GetLineIndex(lines, startAnchor);
        var endIndex = GetLineIndex(lines, endAnchor);
        var selectedLines = lines
            .Skip(startIndex)
            .Take(endIndex - startIndex + 1)
            .Select(line => state.Text[line.Start..line.EndOffset])
            .ToArray();

        return new MarkdownEditorLineSelection(
            lines[startIndex].Start,
            lines[endIndex].EndOffset - lines[startIndex].Start,
            selectedLines);
    }

    public static string GetCurrentLineText(MarkdownEditorState state)
    {
        if (state.Text.Length == 0)
        {
            return string.Empty;
        }

        var lines = EnumerateLines(state.Text);
        var offset = Math.Min(state.Selection.End, Math.Max(0, state.Text.Length - 1));
        var lineIndex = GetLineIndex(lines, offset);
        var line = lines[lineIndex];
        return state.Text[line.Start..line.EndOffset];
    }

    public static IReadOnlyList<MarkdownEditorLineRange> EnumerateLines(string text)
    {
        var lines = new List<MarkdownEditorLineRange>();
        if (text.Length == 0)
        {
            return lines;
        }

        var lineStart = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n'))
            {
                continue;
            }

            var endOffset = index;
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                index++;
            }

            lines.Add(new MarkdownEditorLineRange(lineStart, endOffset));
            lineStart = index + 1;
        }

        if (lineStart <= text.Length)
        {
            lines.Add(new MarkdownEditorLineRange(lineStart, text.Length));
        }

        return lines;
    }

    public static int GetLineIndex(IReadOnlyList<MarkdownEditorLineRange> lines, int offset)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (offset >= line.Start && offset <= line.EndOffset)
            {
                return index;
            }
        }

        return Math.Max(0, lines.Count - 1);
    }

    public static int GetHeadingLevelArg(IReadOnlyDictionary<string, object?>? args)
    {
        if (args is null || !args.TryGetValue("level", out var value) || value is null)
        {
            return 1;
        }

        var level = value switch
        {
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number => jsonElement.GetInt32(),
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String &&
                int.TryParse(jsonElement.GetString(), out var parsed) => parsed,
            int intValue => intValue,
            long longValue => (int)longValue,
            _ => 1,
        };

        return Math.Clamp(level, 1, 6);
    }

    public static int FindNextFootnoteIndex(string markdown)
    {
        var matches = Regex.Matches(markdown, @"\[\^(\d+)\]");
        var max = 0;
        foreach (Match match in matches)
        {
            if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
            {
                max = Math.Max(max, value);
            }
        }

        return max + 1;
    }
}
