using System.Text.RegularExpressions;
using AuraMark.Core.Text;

namespace AuraMark.Core.Editing;

public sealed class MarkdownEditorReducer
{
    public bool TryReduce(MarkdownEditorState state, MarkdownEditorAction action, out MarkdownEditorEditResult? result)
    {
        state = MarkdownEditorStateUtilities.NormalizeState(state);
        result = action.Id switch
        {
            "paragraph.paragraph" => MutateSelectedLines(state, lines =>
                lines.Select(line =>
                {
                    var leading = line[..(line.Length - line.TrimStart().Length)];
                    var body = line.TrimStart();
                    body = MarkdownEditorPatterns.HeadingRegex.Replace(body, string.Empty);
                    body = MarkdownEditorPatterns.QuoteRegex.Replace(body, string.Empty);
                    body = MarkdownEditorPatterns.TaskListRegex.Replace(body, string.Empty);
                    body = MarkdownEditorPatterns.OrderedListRegex.Replace(body, string.Empty);
                    body = MarkdownEditorPatterns.UnorderedListRegex.Replace(body, string.Empty);
                    return body.Length > 0 ? $"{leading}{body}" : line;
                }).ToArray()),
            "paragraph.heading" => RunHeading(state, MarkdownEditorStateUtilities.GetHeadingLevelArg(action.Args)),
            "paragraph.heading.increase" => MutateSelectedLines(state, lines => lines.Select(line =>
            {
                var match = MarkdownEditorPatterns.HeadingRegex.Match(line);
                if (!match.Success)
                {
                    return $"# {line.Trim()}";
                }

                var nextLevel = Math.Min(6, match.Groups[1].Length + 1);
                return $"{new string('#', nextLevel)} {MarkdownEditorPatterns.HeadingRegex.Replace(line.TrimStart(), string.Empty)}";
            }).ToArray()),
            "paragraph.heading.decrease" => MutateSelectedLines(state, lines => lines.Select(line =>
            {
                var match = MarkdownEditorPatterns.HeadingRegex.Match(line);
                if (!match.Success)
                {
                    return line;
                }

                var nextLevel = match.Groups[1].Length - 1;
                var body = MarkdownEditorPatterns.HeadingRegex.Replace(line.TrimStart(), string.Empty);
                return nextLevel <= 0 ? body : $"{new string('#', nextLevel)} {body}";
            }).ToArray()),
            "paragraph.quote" => MutateSelectedLines(state, lines =>
            {
                var nonEmptyLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                var shouldRemove = nonEmptyLines.Length > 0 && nonEmptyLines.All(line => MarkdownEditorPatterns.QuoteRegex.IsMatch(line));
                return lines.Select(line =>
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        return line;
                    }

                    return shouldRemove ? MarkdownEditorPatterns.QuoteRegex.Replace(line, string.Empty) : $"> {line}";
                }).ToArray();
            }),
            "paragraph.ordered-list" => MutateSelectedLines(state, lines => lines.Select((line, index) =>
                string.IsNullOrWhiteSpace(line) ? line : $"{index + 1}. {StripListMarkers(line)}").ToArray()),
            "paragraph.unordered-list" => MutateSelectedLines(state, lines => lines.Select(line =>
                string.IsNullOrWhiteSpace(line) ? line : $"- {StripListMarkers(line)}").ToArray()),
            "paragraph.task-list" => MutateSelectedLines(state, lines => lines.Select(line =>
                string.IsNullOrWhiteSpace(line) ? line : $"- [ ] {StripListMarkers(line)}").ToArray()),
            "paragraph.code-fence" => WrapSelection(state, "\n```text\n", "\n```\n", "code"),
            "paragraph.math-block" => WrapSelection(state, "\n$$\n", "\n$$\n", "math"),
            "paragraph.table" => ReplaceSelection(
                state,
                "| Column 1 | Column 2 | Column 3 |\n| --- | --- | --- |\n| Value 1 | Value 2 | Value 3 |"),
            "paragraph.footnote" => RunFootnote(state),
            "paragraph.horizontal-rule" => ReplaceSelection(state, "\n\n---\n\n"),
            "format.bold" => WrapSelection(state, "**", "**", "bold"),
            "format.italic" => WrapSelection(state, "*", "*", "italic"),
            "format.underline" => WrapSelection(state, "<u>", "</u>", "underlined"),
            "format.strikethrough" => WrapSelection(state, "~~", "~~", "struck"),
            "format.inline-code" => WrapSelection(state, "`", "`", "code"),
            "format.inline-math" => WrapSelection(state, "$", "$", "x"),
            "format.link" => RunLink(state),
            "format.image" => ReplaceSelection(state, "![alt text](path/to/image.png)", 2, 10),
            "format.highlight" => WrapSelection(state, "<mark>", "</mark>", "highlight"),
            "format.superscript" => WrapSelection(state, "<sup>", "</sup>", "sup"),
            "format.subscript" => WrapSelection(state, "<sub>", "</sub>", "sub"),
            "format.clear" => state.Selection.IsCollapsed ? null : RunClearFormatting(state),
            _ => null,
        };

        return result is not null;
    }

    private static MarkdownEditorEditResult RunHeading(MarkdownEditorState state, int level)
    {
        return MutateSelectedLines(state, lines =>
        {
            var prefix = $"{new string('#', level)} ";
            return lines.Select(line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return line;
                }

                var body = MarkdownEditorPatterns.HeadingRegex.Replace(line.TrimStart(), string.Empty);
                body = MarkdownEditorPatterns.QuoteRegex.Replace(body, string.Empty);
                return $"{prefix}{body}";
            }).ToArray();
        });
    }

    private static MarkdownEditorEditResult RunFootnote(MarkdownEditorState state)
    {
        var selectedText = MarkdownEditorStateUtilities.GetSelectedTextOrPlaceholder(state, "footnote");
        var marker = $"[^{MarkdownEditorStateUtilities.FindNextFootnoteIndex(state.Text)}]";
        var noteId = marker[2..^1];
        var lineEnding = MarkdownEditorTextUtilities.DetectLineEnding(state.Text);
        var notePrefix = state.Text.Length == 0 || state.Text.EndsWith($"{lineEnding}{lineEnding}", StringComparison.Ordinal)
            ? string.Empty
            : $"{lineEnding}{lineEnding}";

        var start = state.Selection.Start;
        var length = state.Selection.Length;
        var markerStart = start + selectedText.Length;

        return new MarkdownEditorEditResult(
            [
                new MarkdownEditorReplacement(state.Text.Length, 0, $"{notePrefix}[^{noteId}]: note"),
                new MarkdownEditorReplacement(start, length, $"{selectedText}{marker}"),
            ],
            new TextSelection(markerStart, markerStart + marker.Length));
    }

    private static MarkdownEditorEditResult RunLink(MarkdownEditorState state)
    {
        var selectedText = MarkdownEditorStateUtilities.GetSelectedTextOrPlaceholder(state, "link text");
        return ReplaceSelection(
            state,
            $"[{selectedText}](https://example.com)",
            selectedText.Length + 3,
            selectedText.Length + 22);
    }

    private static MarkdownEditorEditResult RunClearFormatting(MarkdownEditorState state)
    {
        if (state.Selection.IsCollapsed)
        {
            return ReplaceSelection(state, string.Empty);
        }

        var selectedText = MarkdownEditorStateUtilities.GetSelectedText(state)
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("~~", string.Empty, StringComparison.Ordinal);

        selectedText = Regex.Replace(selectedText, @"^\*(.*)\*$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^`(.*)`$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^\$(.*)\$$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^<u>(.*)</u>$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^<mark>(.*)</mark>$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^<sup>(.*)</sup>$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^<sub>(.*)</sub>$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^\[(.*)\]\((.*)\)$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^!\[(.*)\]\((.*)\)$", "$1", RegexOptions.Singleline);
        return ReplaceSelection(state, selectedText);
    }

    private static MarkdownEditorEditResult WrapSelection(
        MarkdownEditorState state,
        string prefix,
        string suffix,
        string placeholder)
    {
        var selectedText = MarkdownEditorStateUtilities.GetSelectedTextOrPlaceholder(state, placeholder);
        return ReplaceSelection(
            state,
            $"{prefix}{selectedText}{suffix}",
            prefix.Length,
            prefix.Length + selectedText.Length);
    }

    private static MarkdownEditorEditResult ReplaceSelection(
        MarkdownEditorState state,
        string replacement,
        int? selectionStartOffset = null,
        int? selectionEndOffset = null)
    {
        return ReplaceRange(
            state.Selection.Start,
            state.Selection.Length,
            replacement,
            selectionStartOffset ?? replacement.Length,
            selectionEndOffset ?? replacement.Length);
    }

    private static MarkdownEditorEditResult ReplaceRange(
        int start,
        int length,
        string replacement,
        int selectionStartOffset,
        int selectionEndOffset)
    {
        return new MarkdownEditorEditResult(
            [new MarkdownEditorReplacement(start, length, replacement)],
            new TextSelection(start + selectionStartOffset, start + selectionEndOffset));
    }

    private static MarkdownEditorEditResult MutateSelectedLines(
        MarkdownEditorState state,
        Func<string[], string[]> mutateLines)
    {
        var selection = MarkdownEditorStateUtilities.GetLineSelection(state);
        var lineEnding = MarkdownEditorTextUtilities.DetectLineEnding(state.Text);
        var nextBlock = string.Join(lineEnding, mutateLines(selection.Lines));
        return ReplaceRange(selection.StartOffset, selection.Length, nextBlock, 0, nextBlock.Length);
    }

    private static string StripListMarkers(string line)
    {
        var body = line.TrimStart();
        body = MarkdownEditorPatterns.TaskListRegex.Replace(body, string.Empty);
        body = MarkdownEditorPatterns.OrderedListRegex.Replace(body, string.Empty);
        body = MarkdownEditorPatterns.UnorderedListRegex.Replace(body, string.Empty);
        body = MarkdownEditorPatterns.QuoteRegex.Replace(body, string.Empty);
        body = MarkdownEditorPatterns.HeadingRegex.Replace(body, string.Empty);
        return body;
    }
}
