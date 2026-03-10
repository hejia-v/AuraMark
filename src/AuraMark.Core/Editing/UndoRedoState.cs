using System.Collections.Immutable;

namespace AuraMark.Core.Editing;

public sealed record UndoRedoState(
    ImmutableStack<EditorTransaction> UndoStack,
    ImmutableStack<EditorTransaction> RedoStack)
{
    public static UndoRedoState Empty =>
        new(ImmutableStack<EditorTransaction>.Empty, ImmutableStack<EditorTransaction>.Empty);
}
