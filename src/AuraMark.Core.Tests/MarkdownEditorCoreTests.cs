using AuraMark.Core.Editing;
using AuraMark.Core.Text;
using Xunit;

namespace AuraMark.Core.Tests;

public sealed class MarkdownEditorCoreTests
{
    private readonly MarkdownEditorReducer _reducer = new();
    private readonly MarkdownEditorActionStateEvaluator _evaluator = new();

    [Fact]
    public void TryReduce_HeadingAction_TransformsSelectedLine()
    {
        var state = new MarkdownEditorState("Title", TextSelection.FromStartAndLength(0, 5));

        var handled = _reducer.TryReduce(
            state,
            new MarkdownEditorAction("paragraph.heading", new Dictionary<string, object?> { ["level"] = 2 }),
            out var result);

        Assert.True(handled);
        Assert.NotNull(result);

        var next = Apply(state, result!);
        Assert.Equal("## Title", next.Text);
        Assert.Equal(0, next.Selection.Start);
        Assert.Equal("## Title".Length, next.Selection.End);
    }

    [Fact]
    public void TryReduce_FootnoteAction_AppendsReferenceDefinition()
    {
        var state = new MarkdownEditorState("Body", TextSelection.FromStartAndLength(0, 4));

        var handled = _reducer.TryReduce(state, new MarkdownEditorAction("paragraph.footnote"), out var result);

        Assert.True(handled);
        Assert.NotNull(result);

        var next = Apply(state, result!);
        Assert.Equal($"Body[^1]{Environment.NewLine}{Environment.NewLine}[^1]: note", next.Text);
        Assert.Equal("Body".Length, next.Selection.Start);
        Assert.Equal("Body[^1]".Length, next.Selection.End);
    }

    [Fact]
    public void TryReduce_ClearFormattingWithoutSelection_ReturnsFalse()
    {
        var state = new MarkdownEditorState("**bold**", TextSelection.Collapsed(4));

        var handled = _reducer.TryReduce(state, new MarkdownEditorAction("format.clear"), out var result);

        Assert.False(handled);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_CodeFenceState_ReportsCaretInsideFence()
    {
        var lineEnding = Environment.NewLine;
        var text = $"```{lineEnding}code{lineEnding}```";
        var state = new MarkdownEditorState(text, TextSelection.Collapsed(5));

        var snapshot = _evaluator.Evaluate(state);

        Assert.True(snapshot.IsActive("paragraph.code-fence"));
    }

    [Fact]
    public void Evaluate_WrappedBoldSelection_ReportsBoldActive()
    {
        var state = new MarkdownEditorState("**bold**", new TextSelection(2, 6));

        var snapshot = _evaluator.Evaluate(state);

        Assert.True(snapshot.IsActive("format.bold"));
    }

    private static MarkdownEditorState Apply(MarkdownEditorState state, MarkdownEditorEditResult result)
    {
        var text = state.Text;
        foreach (var replacement in result.Replacements.OrderByDescending(item => item.Start))
        {
            text = text.Remove(replacement.Start, replacement.Length)
                .Insert(replacement.Start, replacement.NewText);
        }

        return state with { Text = text, Selection = result.Selection };
    }
}
