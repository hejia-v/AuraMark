namespace AuraMark.Core.Editing;

public sealed record ReduceResult(
    EditorState State,
    IReadOnlyList<EditorInvalidation> Invalidations,
    IReadOnlyList<EditorEffect> Effects);
