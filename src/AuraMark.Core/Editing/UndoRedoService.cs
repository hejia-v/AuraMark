namespace AuraMark.Core.Editing;

public sealed class UndoRedoService
{
    public UndoRedoState Push(UndoRedoState state, EditorTransaction transaction) =>
        state with
        {
            UndoStack = state.UndoStack.Push(transaction),
            RedoStack = state.RedoStack.Clear(),
        };

    public bool TryUndo(UndoRedoState state, out EditorTransaction transaction, out UndoRedoState nextState)
    {
        if (state.UndoStack.IsEmpty)
        {
            transaction = default!;
            nextState = state;
            return false;
        }

        transaction = state.UndoStack.Peek();
        nextState = new UndoRedoState(
            state.UndoStack.Pop(),
            state.RedoStack.Push(transaction));
        return true;
    }

    public bool TryRedo(UndoRedoState state, out EditorTransaction transaction, out UndoRedoState nextState)
    {
        if (state.RedoStack.IsEmpty)
        {
            transaction = default!;
            nextState = state;
            return false;
        }

        transaction = state.RedoStack.Peek();
        nextState = new UndoRedoState(
            state.UndoStack.Push(transaction),
            state.RedoStack.Pop());
        return true;
    }
}
