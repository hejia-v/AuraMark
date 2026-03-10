using AuraMark.Core.Syntax;
using Xunit;

namespace AuraMark.Core.Tests;

public sealed class MarkdownOutlineServiceTests
{
    private readonly MarkdownOutlineService _service = new();

    [Fact]
    public void ParseDocument_UsesSourceOffsetsAfterFrontMatter()
    {
        const string markdown = """
---
name: sample
---
# Title

## Child
""";

        var outline = _service.ParseDocument(markdown);

        Assert.Equal(markdown.IndexOf("# Title", StringComparison.Ordinal), outline.Headings[0].SourceOffset);
        Assert.Equal(markdown.IndexOf("## Child", StringComparison.Ordinal), outline.Headings[1].SourceOffset);
    }

    [Fact]
    public void ParseDocument_IgnoresHeadingsInsideCodeFences()
    {
        const string markdown = """
# Title

```md
## Not an outline heading
```

## Real Heading
""";

        var outline = _service.ParseDocument(markdown);

        Assert.Collection(
            outline.Headings,
            heading =>
            {
                Assert.Equal("Title", heading.Text);
                Assert.Equal(1, heading.Level);
            },
            heading =>
            {
                Assert.Equal("Real Heading", heading.Text);
                Assert.Equal(2, heading.Level);
            });
    }

    [Fact]
    public void ParseDocument_TrimsClosingHeadingMarkers()
    {
        const string markdown = """
# Title ###
""";

        var outline = _service.ParseDocument(markdown);

        Assert.Single(outline.Headings);
        Assert.Equal("Title", outline.Headings[0].Text);
    }
}
