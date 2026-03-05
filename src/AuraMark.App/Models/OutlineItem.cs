namespace AuraMark.App.Models;

public sealed class OutlineItem
{
    public int Index { get; init; }

    public int Level { get; init; }

    public string Text { get; init; } = string.Empty;

    public string DisplayText { get; init; } = string.Empty;
}
