using AuraMark.Core.Layout;
using AuraMark.Core.Syntax;
using AuraMark.Core.Text;

namespace AuraMark.Core.Editing;

public sealed record EditorState(
    DocumentSnapshot Snapshot,
    ParseResult? Parse,
    LayoutDocument? Layout,
    ViewportState Viewport,
    UndoRedoState History,
    UiTransientState UiState,
    EditorVisualState VisualState);
