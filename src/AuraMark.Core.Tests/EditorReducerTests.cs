using AuraMark.Core.Editing;
using AuraMark.Core.Layout;
using AuraMark.Core.Syntax;
using AuraMark.Core.Text;
using Xunit;

namespace AuraMark.Core.Tests;

public sealed class EditorReducerTests
{
    private readonly EditorReducer _reducer = new(
        new SourceTextBufferService(),
        new MarkdownParser(),
        new NullLayoutEngine(),
        new UndoRedoService());

    [Fact]
    public void Reduce_ReplaceSelectionWithText_ReplacesSelectedRangeAndPushesHistory()
    {
        var state = CreateState("hello world", 6, 11);

        var result = _reducer.Reduce(state, new ReplaceSelectionWithTextAction("AuraMark"));

        Assert.Equal("hello AuraMark", result.State.Snapshot.Text.ToString());
        Assert.Equal(14, result.State.Snapshot.Selection.Active.Offset);
        Assert.False(result.State.History.UndoStack.IsEmpty);
        Assert.Contains(result.Invalidations, invalidation => invalidation is SyntaxInvalidation);
    }

    [Fact]
    public void Reduce_InsertLineBreak_InsertsEnvironmentLineEnding()
    {
        var state = CreateState("hello", 5, 5);

        var result = _reducer.Reduce(state, new InsertLineBreakAction());

        Assert.Equal($"hello{Environment.NewLine}", result.State.Snapshot.Text.ToString());
    }

    [Fact]
    public void Reduce_DeleteBackward_RemovesPreviousCharacterWhenSelectionCollapsed()
    {
        var state = CreateState("abcd", 2, 2);

        var result = _reducer.Reduce(state, new DeleteBackwardAction());

        Assert.Equal("acd", result.State.Snapshot.Text.ToString());
        Assert.Equal(1, result.State.Snapshot.Selection.Active.Offset);
    }

    [Fact]
    public void Reduce_UndoAndRedo_RestoreSnapshots()
    {
        var state = CreateState("abc", 3, 3);
        var edited = _reducer.Reduce(state, new ReplaceSelectionWithTextAction("d")).State;

        var undone = _reducer.Reduce(edited, new UndoAction()).State;
        var redone = _reducer.Reduce(undone, new RedoAction()).State;

        Assert.Equal("abc", undone.Snapshot.Text.ToString());
        Assert.Equal("abcd", redone.Snapshot.Text.ToString());
    }

    private static EditorState CreateState(string text, int anchor, int active)
    {
        var parser = new MarkdownParser();
        var state = EditorStateFactory.Create(text, parser, new NullLayoutEngine());
        var selection = new SelectionRange(new TextPosition(anchor), new TextPosition(active));
        return state with
        {
            Snapshot = state.Snapshot with { Selection = selection },
            VisualState = new EditorVisualState(
                new CaretVisualState(selection.Active),
                new SelectionVisualState(selection)),
        };
    }
}
