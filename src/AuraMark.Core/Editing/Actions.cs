using AuraMark.Core.Text;
using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Editing;

public interface IEditorAction
{
}

public enum CaretMoveKind
{
    Left,
    Right,
    Up,
    Down,
    LineStart,
    LineEnd,
    DocumentStart,
    DocumentEnd,
}

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
