using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuraMark.Core;

public sealed class IpcErrorPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("retryable")]
    public bool Retryable { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static bool TryParse(string? json, out IpcErrorPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<IpcErrorPayload>(json, JsonOptions);
            return payload is not null && !string.IsNullOrWhiteSpace(payload.Code);
        }
        catch
        {
            return false;
        }
    }
}
