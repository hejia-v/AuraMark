using System.Net;
using System.Text;

namespace AuraMark.Core;

public static class MetadataHtmlRenderer
{
    public static string Render(IReadOnlyList<MetadataEntry>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("<section class=\"frontmatter-panel\">");

        foreach (var entry in metadata)
        {
            builder.AppendLine("  <div class=\"frontmatter-row\">");
            builder.Append("    <div class=\"frontmatter-key\">")
                .Append(WebUtility.HtmlEncode(entry.Key))
                .AppendLine("</div>");
            builder.AppendLine("    <div class=\"frontmatter-value\">");

            switch (entry.Kind)
            {
                case "list" when entry.Items is { Count: > 0 }:
                    builder.AppendLine("      <div class=\"frontmatter-chips\">");
                    foreach (var item in entry.Items)
                    {
                        builder.Append("        <span class=\"frontmatter-chip\">")
                            .Append(WebUtility.HtmlEncode(item))
                            .AppendLine("</span>");
                    }

                    builder.AppendLine("      </div>");
                    break;
                case "object":
                    builder.Append("      <pre class=\"frontmatter-structured\">")
                        .Append(WebUtility.HtmlEncode(entry.StructuredText ?? "null"))
                        .AppendLine("</pre>");
                    break;
                default:
                    builder.Append("      <span class=\"frontmatter-text\">")
                        .Append(WebUtility.HtmlEncode(entry.DisplayText ?? string.Empty))
                        .AppendLine("</span>");
                    break;
            }

            builder.AppendLine("    </div>");
            builder.AppendLine("  </div>");
        }

        builder.AppendLine("</section>");
        return builder.ToString();
    }
}
