using System.Collections.Immutable;
using System.Windows;
using Microsoft.CodeAnalysis.Text;
using AuraMark.Core.Layout;
using AuraMark.Core.Syntax;
using AuraMark.Core.Text;

namespace AuraMark.Core.Editing;

public interface IEditorAction { }
public sealed record InsertTextAction(TextPosition Position, string Text) : IEditorAction;
public sealed record ReplaceRangeAction(TextSpan Span, string Text) : IEditorAction;
public sealed record ReplaceSelectionWithTextAction(string Text) : IEditorAction;
public sealed record DeleteBackwardAction() : IEditorAction;
public sealed record DeleteForwardAction() : IEditorAction;
public sealed record InsertLineBreakAction() : IEditorAction;
public sealed record SetSelectionAction(SelectionRange Selection) : IEditorAction;
public sealed record MoveCaretAction(CaretMoveKind Kind, bool ExtendSelection) : IEditorAction;
public sealed record UndoAction() : IEditorAction;
public sealed record RedoAction() : IEditorAction;
public enum CaretMoveKind { Left, Right, Up, Down, LineStart, LineEnd, DocumentStart, DocumentEnd, WordLeft, WordRight }

public sealed record CaretVisualState(bool IsVisible, TextPosition Position, double PreferredX);
public sealed record SelectionVisualState(SelectionRange Selection);
public sealed record EditorVisualState(CaretVisualState Caret, SelectionVisualState Selection);
public sealed record UiTransientState(bool IsFocused, bool IsComposingIme, string? HoverLink, TextPosition? HoverPosition);
public sealed record EditorTransaction(string Name, DocumentSnapshot Before, DocumentSnapshot After);
public sealed record UndoRedoState(ImmutableStack<EditorTransaction> UndoStack, ImmutableStack<EditorTransaction> RedoStack)
{
    public static UndoRedoState Empty => new(ImmutableStack<EditorTransaction>.Empty, ImmutableStack<EditorTransaction>.Empty);
}
public sealed record EditorState(DocumentSnapshot Snapshot, ParseResult? Parse, LayoutDocument? Layout, ViewportState Viewport, UndoRedoState History, UiTransientState UiState, EditorVisualState VisualState);

public abstract record EditorEffect;
public sealed record ScrollIntoViewEffect(TextSpan Span) : EditorEffect;
public sealed record RequestRenderEffect() : EditorEffect;
public abstract record EditorInvalidation;
public sealed record SyntaxInvalidation(TextSpan Span) : EditorInvalidation;
public sealed record LayoutInvalidation(TextSpan Span) : EditorInvalidation;
public sealed record VisualInvalidation(TextSpan Span) : EditorInvalidation;
public sealed record ReduceResult(EditorState State, IReadOnlyList<EditorInvalidation> Invalidations, IReadOnlyList<EditorEffect> Effects);

public interface IEditorReducer { ReduceResult Reduce(EditorState oldState, IEditorAction action); }
public interface IEditorDispatcher { EditorState CurrentState { get; } event EventHandler<EditorState>? StateChanged; void Dispatch(IEditorAction action); }
public interface IUndoRedoService { UndoRedoState Push(UndoRedoState state, EditorTransaction tx); (UndoRedoState State, EditorTransaction? Tx) TryPopUndo(UndoRedoState state); (UndoRedoState State, EditorTransaction? Tx) TryPopRedo(UndoRedoState state); }
public interface ICaretGeometryAdapter { Rect GetCaretRect(LayoutDocument layout, TextPosition position); }
public interface IHitTestAdapter { TextPosition HitTest(LayoutDocument layout, Point point); }

public sealed class UndoRedoService : IUndoRedoService
{
    public UndoRedoState Push(UndoRedoState state, EditorTransaction tx) => state with { UndoStack = state.UndoStack.Push(tx), RedoStack = ImmutableStack<EditorTransaction>.Empty };
    public (UndoRedoState State, EditorTransaction? Tx) TryPopUndo(UndoRedoState state) => state.UndoStack.IsEmpty ? (state, null) : (state with { UndoStack = state.UndoStack.Pop(), RedoStack = state.RedoStack.Push(state.UndoStack.Peek()) }, state.UndoStack.Peek());
    public (UndoRedoState State, EditorTransaction? Tx) TryPopRedo(UndoRedoState state) => state.RedoStack.IsEmpty ? (state, null) : (state with { RedoStack = state.RedoStack.Pop(), UndoStack = state.UndoStack.Push(state.RedoStack.Peek()) }, state.RedoStack.Peek());
}

public sealed class EditorDispatcher : IEditorDispatcher
{
    private readonly IEditorReducer _reducer;
    public EditorState CurrentState { get; private set; }
    public event EventHandler<EditorState>? StateChanged;
    public EditorDispatcher(IEditorReducer reducer, EditorState initialState) { _reducer = reducer; CurrentState = initialState; }
    public void Dispatch(IEditorAction action) { var result = _reducer.Reduce(CurrentState, action); CurrentState = result.State; StateChanged?.Invoke(this, CurrentState); }
}

public sealed class EditorReducer : IEditorReducer
{
    private readonly ITextBufferService _textBuffer; private readonly IIncrementalMarkdownParser _parser; private readonly ILayoutEngine _layout; private readonly IUndoRedoService _undoRedo; private readonly ICaretGeometryAdapter _caretGeometry; private readonly IHitTestAdapter _hitTest;
    public EditorReducer(ITextBufferService textBuffer, IIncrementalMarkdownParser parser, ILayoutEngine layout, IUndoRedoService undoRedo, ICaretGeometryAdapter caretGeometry, IHitTestAdapter hitTest) { _textBuffer = textBuffer; _parser = parser; _layout = layout; _undoRedo = undoRedo; _caretGeometry = caretGeometry; _hitTest = hitTest; }
    public ReduceResult Reduce(EditorState oldState, IEditorAction action) => action switch
    {
        InsertTextAction insert => ReduceInsert(oldState, insert),
        ReplaceSelectionWithTextAction replaceSel => ReduceReplaceSelectionWithText(oldState, replaceSel),
        ReplaceRangeAction replace => ReduceReplace(oldState, replace),
        SetSelectionAction select => ReduceSelection(oldState, select),
        MoveCaretAction move => ReduceMoveCaret(oldState, move),
        DeleteBackwardAction => ReduceDeleteBackward(oldState),
        DeleteForwardAction => ReduceDeleteForward(oldState),
        InsertLineBreakAction => ReduceInsertLineBreak(oldState),
        UndoAction => ReduceUndo(oldState),
        RedoAction => ReduceRedo(oldState),
        _ => new ReduceResult(oldState, Array.Empty<EditorInvalidation>(), Array.Empty<EditorEffect>())
    };

    private ReduceResult ReduceInsert(EditorState state, InsertTextAction action) => ReplaceCurrentSelection(state, action.Text, "Insert Text");
    private ReduceResult ReduceReplaceSelectionWithText(EditorState state, ReplaceSelectionWithTextAction action) => ReplaceCurrentSelection(state, action.Text, "Replace Selection");
    private ReduceResult ReduceReplace(EditorState state, ReplaceRangeAction action) { var apply = _textBuffer.Apply(state.Snapshot, new[] { new TextEdit(action.Span, action.Text) }); return RebuildAfterTextChange(state, apply.Snapshot, apply.Changes, apply.DirtySpan, SelectionRange.Collapsed(new TextPosition(action.Span.Start + action.Text.Length)), "Replace Range"); }
    private static ReduceResult ReduceSelection(EditorState state, SetSelectionAction action) => new(state with { Snapshot = state.Snapshot with { Selection = action.Selection }, VisualState = state.VisualState with { Selection = new SelectionVisualState(action.Selection), Caret = state.VisualState.Caret with { Position = action.Selection.Active } } }, new EditorInvalidation[] { new VisualInvalidation(action.Selection.AsTextSpan()) }, new EditorEffect[] { new RequestRenderEffect() });
    private ReduceResult ReplaceCurrentSelection(EditorState state, string text, string txName)
    {
        var selection = state.Snapshot.Selection; TextEdit edit; int newCaretOffset;
        if (!selection.IsCollapsed) { edit = new TextEdit(selection.AsTextSpan(), text); newCaretOffset = selection.Start + text.Length; }
        else { edit = new TextEdit(new TextSpan(selection.Active.Offset, 0), text); newCaretOffset = selection.Active.Offset + text.Length; }
        var apply = _textBuffer.Apply(state.Snapshot, new[] { edit });
        return RebuildAfterTextChange(state, apply.Snapshot, apply.Changes, apply.DirtySpan, SelectionRange.Collapsed(new TextPosition(newCaretOffset)), txName);
    }
    private ReduceResult ReduceDeleteBackward(EditorState state) { var selection = state.Snapshot.Selection; if (!selection.IsCollapsed) return DeleteSpan(state, selection.AsTextSpan(), "Delete Backward"); int caret = selection.Active.Offset; if (caret <= 0) return new ReduceResult(state, Array.Empty<EditorInvalidation>(), Array.Empty<EditorEffect>()); return DeleteSpan(state, new TextSpan(caret - 1, 1), "Delete Backward"); }
    private ReduceResult ReduceDeleteForward(EditorState state) { var selection = state.Snapshot.Selection; if (!selection.IsCollapsed) return DeleteSpan(state, selection.AsTextSpan(), "Delete Forward"); int caret = selection.Active.Offset; if (caret >= state.Snapshot.Text.Length) return new ReduceResult(state, Array.Empty<EditorInvalidation>(), Array.Empty<EditorEffect>()); return DeleteSpan(state, new TextSpan(caret, 1), "Delete Forward"); }
    private ReduceResult DeleteSpan(EditorState state, TextSpan span, string txName) { var apply = _textBuffer.Apply(state.Snapshot, new[] { new TextEdit(span, string.Empty) }); return RebuildAfterTextChange(state, apply.Snapshot, apply.Changes, apply.DirtySpan, SelectionRange.Collapsed(new TextPosition(span.Start)), txName); }
    private ReduceResult ReduceInsertLineBreak(EditorState state) { var selection = state.Snapshot.Selection; string nl = Environment.NewLine; TextEdit edit; int newCaretOffset; if (!selection.IsCollapsed) { edit = new TextEdit(selection.AsTextSpan(), nl); newCaretOffset = selection.Start + nl.Length; } else { edit = new TextEdit(new TextSpan(selection.Active.Offset, 0), nl); newCaretOffset = selection.Active.Offset + nl.Length; } var apply = _textBuffer.Apply(state.Snapshot, new[] { edit }); return RebuildAfterTextChange(state, apply.Snapshot, apply.Changes, apply.DirtySpan, SelectionRange.Collapsed(new TextPosition(newCaretOffset)), "Insert Line Break"); }
    private ReduceResult ReduceMoveCaret(EditorState state, MoveCaretAction action)
    {
        var snapshot = state.Snapshot; var selection = snapshot.Selection; var textLength = snapshot.Text.Length;
        TextPosition next = action.Kind switch
        {
            CaretMoveKind.Left => !selection.IsCollapsed ? new TextPosition(selection.Start) : new TextPosition(Math.Max(0, selection.Active.Offset - 1)),
            CaretMoveKind.Right => !selection.IsCollapsed ? new TextPosition(selection.End) : new TextPosition(Math.Min(textLength, selection.Active.Offset + 1)),
            CaretMoveKind.DocumentStart => new TextPosition(0),
            CaretMoveKind.DocumentEnd => new TextPosition(textLength),
            CaretMoveKind.LineStart => new TextPosition(snapshot.Text.Lines.GetLineFromPosition(selection.Active.Offset).Start),
            CaretMoveKind.LineEnd => new TextPosition(snapshot.Text.Lines.GetLineFromPosition(selection.Active.Offset).End),
            CaretMoveKind.Up => MoveVertical(state, selection.Active, true),
            CaretMoveKind.Down => MoveVertical(state, selection.Active, false),
            _ => selection.Active
        };
        var newSelection = action.ExtendSelection ? new SelectionRange(selection.Anchor, next) : SelectionRange.Collapsed(next);
        var newState = state with { Snapshot = snapshot with { Selection = newSelection }, VisualState = state.VisualState with { Caret = state.VisualState.Caret with { Position = next }, Selection = new SelectionVisualState(newSelection) } };
        return new ReduceResult(newState, new EditorInvalidation[] { new VisualInvalidation(newSelection.AsTextSpan()) }, new EditorEffect[] { new RequestRenderEffect(), new ScrollIntoViewEffect(newSelection.AsTextSpan()) });
    }
    private TextPosition MoveVertical(EditorState state, TextPosition current, bool up)
    {
        var layout = state.Layout; if (layout is null) return current; var caretRect = _caretGeometry.GetCaretRect(layout, current); double targetY = up ? caretRect.Top - Math.Max(1, caretRect.Height * 0.5) : caretRect.Bottom + Math.Max(1, caretRect.Height * 0.5); double targetX = state.VisualState.Caret.PreferredX > 0 ? state.VisualState.Caret.PreferredX : caretRect.X; return _hitTest.HitTest(layout, new Point(targetX, targetY));
    }
    private ReduceResult ReduceUndo(EditorState state)
    {
        var (history, tx) = _undoRedo.TryPopUndo(state.History); if (tx is null) return new ReduceResult(state, Array.Empty<EditorInvalidation>(), Array.Empty<EditorEffect>());
        var restored = tx.Before; var dirty = new TextSpan(0, Math.Max(state.Snapshot.Text.Length, restored.Text.Length)); var parse = _parser.Parse(restored, state.Parse, dirty); var layout = _layout.Build(new LayoutBuildRequest(parse, state.Viewport, dirty), state.Layout);
        var newState = state with { Snapshot = restored, Parse = parse, Layout = layout.Document, History = history };
        return new ReduceResult(newState, new EditorInvalidation[] { new SyntaxInvalidation(dirty), new LayoutInvalidation(dirty), new VisualInvalidation(dirty) }, new EditorEffect[] { new RequestRenderEffect(), new ScrollIntoViewEffect(restored.Selection.AsTextSpan()) });
    }
    private ReduceResult ReduceRedo(EditorState state)
    {
        var (history, tx) = _undoRedo.TryPopRedo(state.History); if (tx is null) return new ReduceResult(state, Array.Empty<EditorInvalidation>(), Array.Empty<EditorEffect>());
        var restored = tx.After; var dirty = new TextSpan(0, Math.Max(state.Snapshot.Text.Length, restored.Text.Length)); var parse = _parser.Parse(restored, state.Parse, dirty); var layout = _layout.Build(new LayoutBuildRequest(parse, state.Viewport, dirty), state.Layout);
        var newState = state with { Snapshot = restored, Parse = parse, Layout = layout.Document, History = history };
        return new ReduceResult(newState, new EditorInvalidation[] { new SyntaxInvalidation(dirty), new LayoutInvalidation(dirty), new VisualInvalidation(dirty) }, new EditorEffect[] { new RequestRenderEffect(), new ScrollIntoViewEffect(restored.Selection.AsTextSpan()) });
    }
    private EditorState PushTransaction(EditorState oldState, EditorState newState, string name) => newState with { History = _undoRedo.Push(oldState.History, new EditorTransaction(name, oldState.Snapshot, newState.Snapshot)) };
    private ReduceResult RebuildAfterTextChange(EditorState oldState, DocumentSnapshot changedSnapshot, IReadOnlyList<TextChange> changes, TextSpan dirtySpan, SelectionRange newSelection, string transactionName)
    {
        var finalSnapshot = changedSnapshot with { Selection = newSelection }; var parse = _parser.Parse(finalSnapshot, oldState.Parse, dirtySpan); var layout = _layout.Build(new LayoutBuildRequest(parse, oldState.Viewport, dirtySpan), oldState.Layout);
        var rebuilt = oldState with { Snapshot = finalSnapshot, Parse = parse, Layout = layout.Document, VisualState = oldState.VisualState with { Caret = oldState.VisualState.Caret with { Position = newSelection.Active }, Selection = new SelectionVisualState(newSelection) } };
        var withHistory = PushTransaction(oldState, rebuilt, transactionName);
        return new ReduceResult(withHistory, new EditorInvalidation[] { new SyntaxInvalidation(dirtySpan), new LayoutInvalidation(dirtySpan), new VisualInvalidation(dirtySpan) }, new EditorEffect[] { new RequestRenderEffect(), new ScrollIntoViewEffect(newSelection.AsTextSpan()) });
    }
}
