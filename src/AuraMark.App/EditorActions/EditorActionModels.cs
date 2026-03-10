namespace AuraMark.App;

internal sealed class EditorActionMenuDefinition
{
    public required string HeaderKey { get; init; }
    public string? ActionId { get; init; }
    public string? StateId { get; init; }
    public IReadOnlyDictionary<string, object?>? Args { get; init; }
    public string? Shortcut { get; init; }
    public bool IsCheckable { get; init; }
    public bool IsSeparator { get; init; }
    public IReadOnlyList<EditorActionMenuDefinition>? Children { get; init; }
}

internal sealed class EditorActionMenuBinding
{
    public required string Id { get; init; }
    public required string StateId { get; init; }
    public IReadOnlyDictionary<string, object?>? Args { get; init; }
    public string? Shortcut { get; init; }
}

internal sealed class ExecuteEditorActionPayload
{
    public required string Id { get; init; }
    public IReadOnlyDictionary<string, object?>? Args { get; init; }
}

internal sealed class EditorActionStateSnapshot
{
    public Dictionary<string, EditorActionState>? Actions { get; init; }
}

internal sealed class EditorActionState
{
    public bool Enabled { get; init; } = true;
    public bool Active { get; init; }
    public string? Shortcut { get; init; }
}

internal sealed record EditorActionDescriptor(string StateId, string DefaultShortcut);
