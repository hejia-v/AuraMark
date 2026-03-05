namespace AuraMark.App.Models;

public sealed class RecentEntry
{
    public string Path { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public DateTime LastOpenedUtc { get; set; }
}
