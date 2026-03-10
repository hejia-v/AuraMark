using AuraMark.Core.Layout;
using AuraMark.Core.Syntax;
using AuraMark.Core.Text;
using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Editing;

public sealed class EditorReducer
{
    private static readonly EditorInvalidation[] NoInvalidations = Array.Empty<EditorInvalidation>();
    private static readonly EditorEffect[] NoEffects = Array.Empty<EditorEffect>();
    private static readonly EditorEffect[] RenderOnlyEffects = [new RequestRenderEffect()];

    private readonly ITextBufferService _textBuffer;
    private readonly IIncrementalMarkdownParser _parser;
    private readonly ILayoutEngine _layout;
    private readonly UndoRedoService _history;

    public EditorReducer(
        ITextBufferService textBuffer,
        IIncrementalMarkdownParser parser,
        ILayoutEngine layout,
        UndoRedoService history)
    {
        _textBuffer = textBuffer;
        _parser = parser;
        _layout = layout;
        _history = history;
    }

    public ReduceResult Reduce(EditorState state, IEditorAction action) =>
        action switch
        {
            InsertTextAction insertText => InsertText(state, insertText),
            ReplaceRangeAction replaceRange => ReplaceRange(state, replaceRange),
            ReplaceSelectionWithTextAction replaceSelection => ReplaceCurrentSelection(state, replaceSelection.Text, "Replace Selection"),
            DeleteBackwardAction => DeleteBackward(state),
            DeleteForwardAction => DeleteForward(state),
            InsertLineBreakAction => ReplaceCurrentSelection(state, Environment.NewLine, "Insert Line Break"),
            SetSelectionAction setSelection => SetSelection(state, setSelection.Selection),
            MoveCaretAction moveCaret => MoveCaret(state, moveCaret),
            UndoAction => Undo(state),
            RedoAction => Redo(state),
            _ => NoChange(state),
        };

    private ReduceResult InsertText(EditorState state, InsertTextAction action)
    {
        var span = new TextSpan(Math.Clamp(action.Position.Offset, 0, state.Snapshot.Text.Length), 0);
        return ApplyEdit(state, new TextEdit(span, action.Text), span.Start + action.Text.Length, "Insert Text");
    }

    private ReduceResult ReplaceRange(EditorState state, ReplaceRangeAction action)
    {
        var start = Math.Clamp(action.Span.Start, 0, state.Snapshot.Text.Length);
        var end = Math.Clamp(action.Span.End, start, state.Snapshot.Text.Length);
        var span = TextSpan.FromBounds(start, end);
        return ApplyEdit(state, new TextEdit(span, action.Text), span.Start + action.Text.Length, "Replace Range");
    }

    private ReduceResult DeleteBackward(EditorState state)
    {
        var selection = state.Snapshot.Selection;
        if (!selection.IsCollapsed)
        {
            return ReplaceCurrentSelection(state, string.Empty, "Delete Backward");
        }

        if (selection.Active.Offset == 0)
        {
            return NoChange(state);
        }

        var span = new TextSpan(selection.Active.Offset - 1, 1);
        return ApplyEdit(state, new TextEdit(span, string.Empty), span.Start, "Delete Backward");
    }

    private ReduceResult DeleteForward(EditorState state)
    {
        var selection = state.Snapshot.Selection;
        if (!selection.IsCollapsed)
        {
            return ReplaceCurrentSelection(state, string.Empty, "Delete Forward");
        }

        if (selection.Active.Offset >= state.Snapshot.Text.Length)
        {
            return NoChange(state);
        }

        var span = new TextSpan(selection.Active.Offset, 1);
        return ApplyEdit(state, new TextEdit(span, string.Empty), span.Start, "Delete Forward");
    }

    private ReduceResult SetSelection(EditorState state, SelectionRange selection)
    {
        selection = NormalizeSelection(selection, state.Snapshot.Text.Length);
        var snapshot = state.Snapshot with { Selection = selection };
        var nextState = state with
        {
            Snapshot = snapshot,
            VisualState = CreateVisualState(selection),
        };

        return new ReduceResult(
            nextState,
            [new VisualInvalidation(selection.AsTextSpan())],
            RenderOnlyEffects);
    }

    private ReduceResult MoveCaret(EditorState state, MoveCaretAction action)
    {
        var nextPosition = action.Kind switch
        {
            CaretMoveKind.Left => new TextPosition(Math.Max(0, state.Snapshot.Selection.Active.Offset - 1)),
            CaretMoveKind.Right => new TextPosition(Math.Min(state.Snapshot.Text.Length, state.Snapshot.Selection.Active.Offset + 1)),
            CaretMoveKind.LineStart => new TextPosition(GetCurrentLine(state.Snapshot.Text, state.Snapshot.Selection.Active.Offset).Start),
            CaretMoveKind.LineEnd => new TextPosition(GetCurrentLine(state.Snapshot.Text, state.Snapshot.Selection.Active.Offset).End),
            CaretMoveKind.DocumentStart => TextPosition.Zero,
            CaretMoveKind.DocumentEnd => new TextPosition(state.Snapshot.Text.Length),
            CaretMoveKind.Up => MoveVertically(state.Snapshot.Text, state.Snapshot.Selection.Active, -1),
            CaretMoveKind.Down => MoveVertically(state.Snapshot.Text, state.Snapshot.Selection.Active, 1),
            _ => state.Snapshot.Selection.Active,
        };

        var selection = action.ExtendSelection
            ? NormalizeSelection(new SelectionRange(state.Snapshot.Selection.Anchor, nextPosition), state.Snapshot.Text.Length)
            : SelectionRange.Collapsed(nextPosition);

        return SetSelection(state, selection);
    }

    private ReduceResult Undo(EditorState state)
    {
        if (!_history.TryUndo(state.History, out var transaction, out var nextHistory))
        {
            return NoChange(state);
        }

        return RestoreSnapshot(state, transaction.Before, nextHistory);
    }

    private ReduceResult Redo(EditorState state)
    {
        if (!_history.TryRedo(state.History, out var transaction, out var nextHistory))
        {
            return NoChange(state);
        }

        return RestoreSnapshot(state, transaction.After, nextHistory);
    }

    private ReduceResult RestoreSnapshot(EditorState state, DocumentSnapshot snapshot, UndoRedoState history)
    {
        var dirtySpan = new TextSpan(0, snapshot.Text.Length);
        var parse = _parser.Parse(snapshot, state.Parse, dirtySpan);
        var layout = _layout.Build(new LayoutBuildRequest(parse, state.Viewport, dirtySpan), state.Layout);
        var nextState = state with
        {
            Snapshot = snapshot,
            Parse = parse,
            Layout = layout.Document,
            History = history,
            VisualState = CreateVisualState(snapshot.Selection),
        };

        return new ReduceResult(
            nextState,
            [
                new SyntaxInvalidation(dirtySpan),
                new LayoutInvalidation(dirtySpan),
                new VisualInvalidation(dirtySpan),
            ],
            [
                new RequestRenderEffect(),
                new ScrollIntoViewEffect(snapshot.Selection.AsTextSpan()),
            ]);
    }

    private ReduceResult ReplaceCurrentSelection(EditorState state, string text, string transactionName)
    {
        var selection = state.Snapshot.Selection;
        var edit = new TextEdit(selection.IsCollapsed
            ? new TextSpan(selection.Active.Offset, 0)
            : selection.AsTextSpan(), text);
        var newCaretOffset = selection.IsCollapsed
            ? selection.Active.Offset + text.Length
            : selection.Start + text.Length;
        return ApplyEdit(state, edit, newCaretOffset, transactionName);
    }

    private ReduceResult ApplyEdit(EditorState state, TextEdit edit, int newCaretOffset, string transactionName)
    {
        var apply = _textBuffer.Apply(state.Snapshot, [edit]);
        return RebuildAfterTextChange(
            state,
            apply.Snapshot,
            apply.Changes,
            apply.DirtySpan,
            SelectionRange.Collapsed(new TextPosition(newCaretOffset)),
            transactionName);
    }

    private ReduceResult RebuildAfterTextChange(
        EditorState oldState,
        DocumentSnapshot changedSnapshot,
        IReadOnlyList<TextChange> changes,
        TextSpan dirtySpan,
        SelectionRange newSelection,
        string transactionName)
    {
        _ = changes;

        var finalSnapshot = changedSnapshot with { Selection = NormalizeSelection(newSelection, changedSnapshot.Text.Length) };
        var parse = _parser.Parse(finalSnapshot, oldState.Parse, dirtySpan);
        var layout = _layout.Build(new LayoutBuildRequest(parse, oldState.Viewport, dirtySpan), oldState.Layout);

        var rebuilt = oldState with
        {
            Snapshot = finalSnapshot,
            Parse = parse,
            Layout = layout.Document,
            VisualState = CreateVisualState(finalSnapshot.Selection),
        };

        var withHistory = rebuilt with
        {
            History = _history.Push(
                oldState.History,
                new EditorTransaction(transactionName, oldState.Snapshot, finalSnapshot)),
        };

        return new ReduceResult(
            withHistory,
            [
                new SyntaxInvalidation(dirtySpan),
                new LayoutInvalidation(dirtySpan),
                new VisualInvalidation(dirtySpan),
            ],
            [
                new RequestRenderEffect(),
                new ScrollIntoViewEffect(finalSnapshot.Selection.AsTextSpan()),
            ]);
    }

    private static ReduceResult NoChange(EditorState state) => new(state, NoInvalidations, NoEffects);

    private static EditorVisualState CreateVisualState(SelectionRange selection) =>
        new(
            new CaretVisualState(selection.Active),
            new SelectionVisualState(selection));

    private static SelectionRange NormalizeSelection(SelectionRange selection, int textLength)
    {
        var anchor = new TextPosition(Math.Clamp(selection.Anchor.Offset, 0, textLength));
        var active = new TextPosition(Math.Clamp(selection.Active.Offset, 0, textLength));
        return new SelectionRange(anchor, active);
    }

    private static TextLine GetCurrentLine(SourceText text, int offset)
    {
        if (text.Length == 0)
        {
            return text.Lines[0];
        }

        var safeOffset = Math.Clamp(offset, 0, Math.Max(0, text.Length - 1));
        return text.Lines.GetLineFromPosition(safeOffset);
    }

    private static TextPosition MoveVertically(SourceText text, TextPosition current, int delta)
    {
        if (text.Length == 0)
        {
            return TextPosition.Zero;
        }

        var currentLine = GetCurrentLine(text, current.Offset);
        var currentIndex = currentLine.LineNumber;
        var targetIndex = Math.Clamp(currentIndex + delta, 0, text.Lines.Count - 1);
        var targetLine = text.Lines[targetIndex];
        var column = Math.Clamp(current.Offset - currentLine.Start, 0, currentLine.End - currentLine.Start);
        return new TextPosition(Math.Min(targetLine.End, targetLine.Start + column));
    }
}
