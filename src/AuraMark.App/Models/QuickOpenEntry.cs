namespace AuraMark.App.Models;

public sealed class QuickOpenEntry
{
    public string FullPath { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string DetailText { get; init; } = string.Empty;

    public string SourceLabel { get; init; } = string.Empty;

    public string SearchText { get; init; } = string.Empty;

    public int SortRank { get; init; }

    public bool IsCurrent { get; init; }
}
