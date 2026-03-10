using AuraMark.Core.Text;

namespace AuraMark.Core.Editing;

public sealed record MarkdownEditorState(string Text, TextSelection Selection);

public sealed record MarkdownEditorAction(string Id, IReadOnlyDictionary<string, object?>? Args = null);

public sealed record MarkdownEditorReplacement(int Start, int Length, string NewText);

public sealed record MarkdownEditorEditResult(
    IReadOnlyList<MarkdownEditorReplacement> Replacements,
    TextSelection Selection);

public sealed record MarkdownEditorActionState(bool Active);

public sealed record MarkdownEditorActionStateSnapshot(IReadOnlyDictionary<string, MarkdownEditorActionState> Actions)
{
    public bool IsActive(string actionId) =>
        Actions.TryGetValue(actionId, out var state) && state.Active;
}

public static class MarkdownEditorTextUtilities
{
    private static readonly System.Text.RegularExpressions.Regex LineEndingRegex =
        new(@"\r\n|\n|\r", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string DetectLineEnding(string text)
    {
        var match = LineEndingRegex.Match(text ?? string.Empty);
        return match.Success ? match.Value : Environment.NewLine;
    }
}
