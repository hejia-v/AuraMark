using AuraMark.Core.Text;

namespace AuraMark.Core.Editing;

public sealed record CaretVisualState(TextPosition Position, double? PreferredX = null);

public sealed record SelectionVisualState(SelectionRange Range);

public sealed record EditorVisualState(CaretVisualState Caret, SelectionVisualState Selection);

public sealed record UiTransientState(bool IsPointerSelecting = false);
