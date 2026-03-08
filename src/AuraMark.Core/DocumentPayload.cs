using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuraMark.Core;

public sealed class MetadataEntry
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "scalar";

    [JsonPropertyName("displayText")]
    public string? DisplayText { get; set; }

    [JsonPropertyName("items")]
    public List<string>? Items { get; set; }

    [JsonPropertyName("structuredText")]
    public string? StructuredText { get; set; }
}

public sealed class DocumentPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [JsonPropertyName("rawMarkdown")]
    public string RawMarkdown { get; set; } = string.Empty;

    [JsonPropertyName("frontMatterRaw")]
    public string FrontMatterRaw { get; set; } = string.Empty;

    [JsonPropertyName("bodyMarkdown")]
    public string BodyMarkdown { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public List<MetadataEntry> Metadata { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public bool TryGetScalarValue(string key, out string value)
    {
        value = string.Empty;

        var entry = Metadata.FirstOrDefault(item =>
            item.Kind.Equals("scalar", StringComparison.OrdinalIgnoreCase) &&
            item.Key.Equals(key, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.DisplayText));

        if (entry is null || string.IsNullOrWhiteSpace(entry.DisplayText))
        {
            return false;
        }

        value = entry.DisplayText;
        return true;
    }

    public static DocumentPayload CreateRaw(string? markdown)
    {
        var rawMarkdown = markdown ?? string.Empty;
        return new DocumentPayload
        {
            RawMarkdown = rawMarkdown,
            BodyMarkdown = rawMarkdown,
        };
    }

    public static bool TryParse(string? json, out DocumentPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<DocumentPayload>(json, JsonOptions);
            return payload is not null;
        }
        catch
        {
            return false;
        }
    }
}
