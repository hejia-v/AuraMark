using System.Text.Json.Serialization;

namespace AuraMark.Core;

public sealed class WebMessagePayload
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = IpcTypes.Update;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
