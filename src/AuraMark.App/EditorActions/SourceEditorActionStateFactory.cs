using AuraMark.Core.Editing;

namespace AuraMark.App;

internal static class SourceEditorActionStateFactory
{
    public static Dictionary<string, EditorActionState> Create(
        MarkdownEditorActionStateSnapshot snapshot,
        bool enabled,
        Func<string, string?, string> resolveShortcut)
    {
        var actions = new Dictionary<string, EditorActionState>(StringComparer.Ordinal);
        foreach (var descriptor in EditorActionCatalog.SourceActionDescriptors)
        {
            actions[descriptor.StateId] = new EditorActionState
            {
                Enabled = enabled,
                Active = snapshot.IsActive(descriptor.StateId),
                Shortcut = resolveShortcut(descriptor.StateId, descriptor.DefaultShortcut),
            };
        }

        return actions;
    }
}
