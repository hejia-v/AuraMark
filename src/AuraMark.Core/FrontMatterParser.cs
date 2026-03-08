using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace AuraMark.Core;

public static partial class FrontMatterParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer Serializer = new SerializerBuilder().Build();

    public static DocumentPayload Parse(string? markdown)
    {
        var rawMarkdown = markdown ?? string.Empty;
        var fallback = DocumentPayload.CreateRaw(rawMarkdown);
        var match = FrontMatterRegex().Match(rawMarkdown);
        if (!match.Success)
        {
            return fallback;
        }

        var yamlText = match.Groups["yaml"].Value;
        object? root;

        try
        {
            root = Deserializer.Deserialize(new StringReader(yamlText));
        }
        catch
        {
            return fallback;
        }

        List<KeyValuePair<string, object?>>? entries = null;
        if (root is not null && !TryEnumerateMapping(root, out entries))
        {
            return fallback;
        }

        var metadata = new List<MetadataEntry>();
        if (entries is not null)
        {
            foreach (var (key, value) in entries)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                metadata.Add(CreateMetadataEntry(key, value));
            }
        }

        var frontMatterRaw = rawMarkdown[..match.Length];
        var bodyMarkdown = rawMarkdown[match.Length..];

        return new DocumentPayload
        {
            RawMarkdown = rawMarkdown,
            FrontMatterRaw = frontMatterRaw,
            BodyMarkdown = bodyMarkdown,
            Metadata = metadata,
        };
    }

    private static MetadataEntry CreateMetadataEntry(string key, object? value)
    {
        if (TryGetScalarText(value, out var scalarText))
        {
            return new MetadataEntry
            {
                Key = key,
                Kind = "scalar",
                DisplayText = scalarText,
            };
        }

        if (TryGetScalarList(value, out var items))
        {
            return new MetadataEntry
            {
                Key = key,
                Kind = "list",
                Items = items,
            };
        }

        return new MetadataEntry
        {
            Key = key,
            Kind = "object",
            StructuredText = SerializeValue(value),
        };
    }

    private static bool TryGetScalarList(object? value, out List<string> items)
    {
        items = [];
        if (value is null || value is string || value is IDictionary)
        {
            return false;
        }

        if (value is not IEnumerable enumerable)
        {
            return false;
        }

        foreach (var item in enumerable)
        {
            if (!TryGetScalarText(item, out var text))
            {
                items = [];
                return false;
            }

            items.Add(text);
        }

        return true;
    }

    private static bool TryGetScalarText(object? value, out string text)
    {
        text = string.Empty;
        if (value is null)
        {
            text = "null";
            return true;
        }

        switch (value)
        {
            case string stringValue:
                text = stringValue;
                return true;
            case bool boolValue:
                text = boolValue ? "true" : "false";
                return true;
            case DateTime dateTime:
                text = dateTime.ToString("O", CultureInfo.InvariantCulture);
                return true;
            case DateTimeOffset dateTimeOffset:
                text = dateTimeOffset.ToString("O", CultureInfo.InvariantCulture);
                return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                return true;
            default:
                return false;
        }
    }

    private static string SerializeValue(object? value)
    {
        var serialized = Serializer.Serialize(value ?? "null").Trim();
        return string.IsNullOrWhiteSpace(serialized) ? "null" : serialized;
    }

    private static bool TryEnumerateMapping(object value, out List<KeyValuePair<string, object?>>? entries)
    {
        switch (value)
        {
            case IDictionary<string, object?> typedDictionary:
                entries = typedDictionary
                    .Select(pair => new KeyValuePair<string, object?>(pair.Key, pair.Value))
                    .ToList();
                return true;
            case IDictionary dictionary:
                entries = [];
                foreach (DictionaryEntry pair in dictionary)
                {
                    var key = Convert.ToString(pair.Key, CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    entries.Add(new KeyValuePair<string, object?>(key, pair.Value));
                }

                return true;
            default:
                entries = null;
                return false;
        }
    }

    [GeneratedRegex(@"\A---[ \t]*\r?\n(?<yaml>.*?)(?:\r?\n)---[ \t]*(?:\r?\n|$)", RegexOptions.Singleline)]
    private static partial Regex FrontMatterRegex();
}
