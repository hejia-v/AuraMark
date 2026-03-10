namespace AuraMark.Core.Editing;

public sealed class MarkdownEditorActionStateEvaluator
{
    public MarkdownEditorActionStateSnapshot Evaluate(MarkdownEditorState state)
    {
        state = MarkdownEditorStateUtilities.NormalizeState(state);

        var currentLine = MarkdownEditorStateUtilities.GetCurrentLineText(state);
        var selectedLines = MarkdownEditorStateUtilities.GetLineSelection(state).Lines;
        var actions = new Dictionary<string, MarkdownEditorActionState>(StringComparer.Ordinal)
        {
            ["paragraph.paragraph"] = new(IsParagraphActive(selectedLines)),
            ["paragraph.heading.increase"] = new(GetCurrentHeadingLevel(currentLine) > 0),
            ["paragraph.heading.decrease"] = new(GetCurrentHeadingLevel(currentLine) > 0),
            ["paragraph.quote"] = new(MarkdownEditorPatterns.QuoteRegex.IsMatch(currentLine)),
            ["paragraph.ordered-list"] = new(MarkdownEditorPatterns.OrderedListRegex.IsMatch(currentLine)),
            ["paragraph.unordered-list"] = new(MarkdownEditorPatterns.UnorderedListRegex.IsMatch(currentLine)),
            ["paragraph.task-list"] = new(MarkdownEditorPatterns.TaskListRegex.IsMatch(currentLine)),
            ["paragraph.code-fence"] = new(IsWithinFenceBlock(state)),
            ["paragraph.math-block"] = new(false),
            ["paragraph.table"] = new(selectedLines.Any(line => line.Contains('|'))),
            ["paragraph.footnote"] = new(false),
            ["paragraph.horizontal-rule"] = new(false),
            ["format.bold"] = new(IsWrappedSelection(state, "**", "**")),
            ["format.italic"] = new(IsWrappedSelection(state, "*", "*")),
            ["format.underline"] = new(IsWrappedSelection(state, "<u>", "</u>")),
            ["format.strikethrough"] = new(IsWrappedSelection(state, "~~", "~~")),
            ["format.inline-code"] = new(IsWrappedSelection(state, "`", "`")),
            ["format.inline-math"] = new(IsWrappedSelection(state, "$", "$")),
            ["format.link"] = new(false),
            ["format.image"] = new(false),
            ["format.highlight"] = new(IsWrappedSelection(state, "<mark>", "</mark>")),
            ["format.superscript"] = new(IsWrappedSelection(state, "<sup>", "</sup>")),
            ["format.subscript"] = new(IsWrappedSelection(state, "<sub>", "</sub>")),
            ["format.clear"] = new(false),
        };

        for (var level = 1; level <= 6; level++)
        {
            actions[$"paragraph.heading.{level}"] = new(IsHeadingActive(selectedLines, level));
        }

        return new MarkdownEditorActionStateSnapshot(actions);
    }

    private static int GetCurrentHeadingLevel(string line)
    {
        var match = MarkdownEditorPatterns.HeadingRegex.Match(line);
        return match.Success ? match.Groups[1].Length : 0;
    }

    private static bool IsParagraphActive(string[] lines)
    {
        return lines.Any(line => !string.IsNullOrWhiteSpace(line)) &&
               lines.All(line =>
               {
                   if (string.IsNullOrWhiteSpace(line))
                   {
                       return true;
                   }

                   return !MarkdownEditorPatterns.HeadingRegex.IsMatch(line) &&
                          !MarkdownEditorPatterns.QuoteRegex.IsMatch(line) &&
                          !MarkdownEditorPatterns.OrderedListRegex.IsMatch(line) &&
                          !MarkdownEditorPatterns.UnorderedListRegex.IsMatch(line);
               });
    }

    private static bool IsHeadingActive(string[] lines, int level)
    {
        var nonEmptyLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        return nonEmptyLines.Length > 0 &&
               nonEmptyLines.All(line =>
               {
                   var match = MarkdownEditorPatterns.HeadingRegex.Match(line);
                   return match.Success && match.Groups[1].Length == level;
               });
    }

    private static bool IsWrappedSelection(MarkdownEditorState state, string prefix, string suffix)
    {
        if (state.Selection.IsCollapsed)
        {
            return false;
        }

        var start = state.Selection.Start;
        var end = state.Selection.End;
        return start >= prefix.Length &&
               end + suffix.Length <= state.Text.Length &&
               string.Equals(state.Text[(start - prefix.Length)..start], prefix, StringComparison.Ordinal) &&
               string.Equals(state.Text[end..(end + suffix.Length)], suffix, StringComparison.Ordinal);
    }

    private static bool IsWithinFenceBlock(MarkdownEditorState state)
    {
        if (state.Text.Length == 0)
        {
            return false;
        }

        var lines = MarkdownEditorStateUtilities.EnumerateLines(state.Text);
        var offset = Math.Min(state.Selection.End, Math.Max(0, state.Text.Length - 1));
        var targetIndex = MarkdownEditorStateUtilities.GetLineIndex(lines, offset);
        var inside = false;
        for (var index = 0; index <= targetIndex; index++)
        {
            var line = lines[index];
            if (!MarkdownEditorPatterns.FenceRegex.IsMatch(state.Text[line.Start..line.EndOffset]))
            {
                continue;
            }

            inside = !inside;
        }

        return inside;
    }
}
