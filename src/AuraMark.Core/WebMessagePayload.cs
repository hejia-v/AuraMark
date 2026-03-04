namespace AuraMark.Core;

public sealed class WebMessagePayload
{
    // "Init" | "Update" | "Command" | "Ack" | "Error"
    public string Type { get; set; } = "Update";

    // Markdown text or command payload
    public string Content { get; set; } = string.Empty;

    // epoch ms
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
