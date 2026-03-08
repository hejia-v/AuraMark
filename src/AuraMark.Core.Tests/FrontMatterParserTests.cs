using Xunit;

namespace AuraMark.Core.Tests;

public sealed class FrontMatterParserTests
{
    [Fact]
    public void Parse_ExtractsScalarListAndObjectMetadata()
    {
        const string markdown = """
---
name: sample-skill
tags:
  - wpf
  - markdown
config:
  theme: nord
  retries: 2
---
# Title

Body text.
""";

        var document = FrontMatterParser.Parse(markdown);

        Assert.Equal(markdown, document.RawMarkdown);
        Assert.StartsWith("---", document.FrontMatterRaw);
        Assert.Equal("# Title\n\nBody text.", document.BodyMarkdown.Replace("\r\n", "\n"));
        Assert.Collection(
            document.Metadata,
            entry =>
            {
                Assert.Equal("name", entry.Key);
                Assert.Equal("scalar", entry.Kind);
                Assert.Equal("sample-skill", entry.DisplayText);
            },
            entry =>
            {
                Assert.Equal("tags", entry.Key);
                Assert.Equal("list", entry.Kind);
                Assert.Equal(["wpf", "markdown"], entry.Items);
            },
            entry =>
            {
                Assert.Equal("config", entry.Key);
                Assert.Equal("object", entry.Kind);
                Assert.Contains("theme:", entry.StructuredText);
            });
    }

    [Fact]
    public void Parse_LeavesMarkdownUntouchedWhenThereIsNoFrontMatter()
    {
        const string markdown = "# Heading\n\nBody";

        var document = FrontMatterParser.Parse(markdown);

        Assert.Equal(markdown, document.RawMarkdown);
        Assert.Equal(string.Empty, document.FrontMatterRaw);
        Assert.Equal(markdown, document.BodyMarkdown);
        Assert.Empty(document.Metadata);
    }

    [Fact]
    public void Parse_FallsBackWhenFrontMatterIsUnterminated()
    {
        const string markdown = """
---
name: broken
# Heading
""";

        var document = FrontMatterParser.Parse(markdown);

        Assert.Equal(markdown, document.BodyMarkdown);
        Assert.Equal(string.Empty, document.FrontMatterRaw);
        Assert.Empty(document.Metadata);
    }

    [Fact]
    public void Parse_FallsBackWhenYamlIsInvalid()
    {
        const string markdown = """
---
name: [broken
---
# Heading
""";

        var document = FrontMatterParser.Parse(markdown);

        Assert.Equal(markdown, document.BodyMarkdown);
        Assert.Equal(string.Empty, document.FrontMatterRaw);
        Assert.Empty(document.Metadata);
    }

    [Fact]
    public void Parse_ExposesNameMetadataEvenWhenHeadingExists()
    {
        const string markdown = """
---
name: metadata-name
---
# Heading Name
""";

        var document = FrontMatterParser.Parse(markdown);

        Assert.True(document.TryGetScalarValue("name", out var name));
        Assert.Equal("metadata-name", name);
        Assert.Contains("# Heading Name", document.BodyMarkdown);
    }
}
