namespace AuraMark.Core.Text;

public sealed record DocumentMeta(string? FilePath, string? Title, bool IsDirty, DateTimeOffset? LastSavedAt);
