using AuraMark.Core.Syntax;
using AuraMark.Core.Text;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace AuraMark.Core.Tests;

public sealed class MarkdownParserTests
{
    private readonly MarkdownParser _parser = new();

    [Fact]
    public void Parse_BuildsHeadingParagraphAndFenceBlocks()
    {
        var snapshot = CreateSnapshot(
            """
            # Title

            Body

            ```csharp
            Console.WriteLine();
            ```
            """);

        var result = _parser.Parse(snapshot);

        Assert.Collection(
            result.Blocks,
            block => Assert.IsType<HeadingBlock>(block),
            block => Assert.IsType<ParagraphBlock>(block),
            block => Assert.IsType<CodeFenceBlock>(block));
        Assert.Single(result.Outline);
        Assert.Equal("Title", result.Outline[0].Text);
    }

    [Fact]
    public void ExpandToSafeWindow_ExpandsFenceToWholeBlock()
    {
        var snapshot = CreateSnapshot(
            """
            Intro

            ```txt
            line 1
            line 2
            ```

            Tail
            """);

        var dirtyStart = snapshot.Text.ToString().IndexOf("line 2", StringComparison.Ordinal);
        var window = _parser.ExpandToSafeWindow(snapshot.Text, previous: null, new TextSpan(dirtyStart, 1));
        var expanded = snapshot.Text.ToString(window.ExpandedSpan);

        Assert.Contains("```txt", expanded);
        Assert.Contains("```", expanded);
    }

    [Fact]
    public void Parse_TerminatesWhenFinalParagraphHasNoTrailingLineBreak()
    {
        var snapshot = CreateSnapshot("Plain paragraph without trailing newline");

        var result = _parser.Parse(snapshot);

        var paragraph = Assert.Single(result.Blocks);
        Assert.IsType<ParagraphBlock>(paragraph);
        Assert.Equal(snapshot.Text.Length, paragraph.Span.End);
    }

    [Fact]
    public void Parse_RecognizesQuoteBlocksWithNestedChildren()
    {
        var snapshot = CreateSnapshot(
            """
            > ## Nested heading
            > > Deep quote
            """);

        var result = _parser.Parse(snapshot);

        var quote = Assert.IsType<QuoteBlock>(Assert.Single(result.Blocks));
        Assert.Collection(
            quote.Children,
            child =>
            {
                var heading = Assert.IsType<HeadingBlock>(child);
                Assert.Equal(2, heading.Level);
            },
            child =>
            {
                var nestedQuote = Assert.IsType<QuoteBlock>(child);
                var nestedParagraph = Assert.IsType<ParagraphBlock>(Assert.Single(nestedQuote.Children));
                Assert.Single(nestedParagraph.Inlines);
            });
    }

    [Fact]
    public void Parse_RecognizesQuoteBlocksWithFencedCode()
    {
        var snapshot = CreateSnapshot(
            """
            > ```csharp
            > Console.WriteLine();
            > ```
            """);

        var result = _parser.Parse(snapshot);

        var quote = Assert.IsType<QuoteBlock>(Assert.Single(result.Blocks));
        var fence = Assert.IsType<CodeFenceBlock>(Assert.Single(quote.Children));
        Assert.Equal("csharp", fence.Language);
    }

    [Fact]
    public void Parse_MergesMultiLineParagraphInsideQuote()
    {
        var snapshot = CreateSnapshot(
            """
            > first line
            > second line
            """);

        var result = _parser.Parse(snapshot);

        var quote = Assert.IsType<QuoteBlock>(Assert.Single(result.Blocks));
        Assert.IsType<ParagraphBlock>(Assert.Single(quote.Children));
    }

    [Fact]
    public void Parse_RecognizesBulletAndOrderedLists()
    {
        var snapshot = CreateSnapshot(
            """
            - first
            - second

            1. one
            2. two
            """);

        var result = _parser.Parse(snapshot);

        Assert.Collection(
            result.Blocks,
            block =>
            {
                var list = Assert.IsType<ListBlock>(block);
                Assert.False(list.Ordered);
                Assert.Equal(2, list.Items.Count);
            },
            block =>
            {
                var list = Assert.IsType<ListBlock>(block);
                Assert.True(list.Ordered);
                Assert.Equal(2, list.Items.Count);
            });
    }

    [Fact]
    public void Parse_MergesMultiLineParagraphInsideListItem()
    {
        var snapshot = CreateSnapshot(
            """
            - first line
              second line
            """);

        var result = _parser.Parse(snapshot);

        var list = Assert.IsType<ListBlock>(Assert.Single(result.Blocks));
        var item = Assert.Single(list.Items);
        Assert.IsType<ParagraphBlock>(Assert.Single(item.Children));
    }

    [Fact]
    public void Parse_RecognizesMultiDigitOrderedListMarkers()
    {
        var snapshot = CreateSnapshot(
            """
            10. first line
                continuation
            11. second line
            """);

        var result = _parser.Parse(snapshot);

        var list = Assert.IsType<ListBlock>(Assert.Single(result.Blocks));
        Assert.True(list.Ordered);
        Assert.Equal(2, list.Items.Count);
        Assert.IsType<ParagraphBlock>(Assert.Single(list.Items[0].Children));
    }

    [Fact]
    public void Parse_RecognizesListItemWithFencedCode()
    {
        var snapshot = CreateSnapshot(
            """
            - intro

              ```txt
              code
              ```
            """);

        var result = _parser.Parse(snapshot);

        var list = Assert.IsType<ListBlock>(Assert.Single(result.Blocks));
        var item = Assert.Single(list.Items);
        Assert.Collection(
            item.Children,
            child => Assert.IsType<ParagraphBlock>(child),
            child =>
            {
                var fence = Assert.IsType<CodeFenceBlock>(child);
                Assert.Equal("txt", fence.Language);
            });
    }

    [Fact]
    public void Parse_RecognizesInlineNodes()
    {
        var snapshot = CreateSnapshot("Alpha `code` **bold** *italic* [link](https://example.com)");

        var result = _parser.Parse(snapshot);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(result.Blocks));
        Assert.Collection(
            paragraph.Inlines,
            inline => Assert.IsType<TextInline>(inline),
            inline => Assert.IsType<CodeInline>(inline),
            inline => Assert.IsType<TextInline>(inline),
            inline =>
            {
                var strong = Assert.IsType<StrongInline>(inline);
                Assert.Single(strong.Children);
            },
            inline => Assert.IsType<TextInline>(inline),
            inline =>
            {
                var emphasis = Assert.IsType<EmphasisInline>(inline);
                Assert.Single(emphasis.Children);
            },
            inline => Assert.IsType<TextInline>(inline),
            inline =>
            {
                var link = Assert.IsType<LinkInline>(inline);
                Assert.Equal("https://example.com", link.Destination);
                Assert.Single(link.Children);
            });
    }

    [Fact]
    public void Parse_RecognizesNestedInlineStyles()
    {
        var snapshot = CreateSnapshot("**bold *italic***");

        var result = _parser.Parse(snapshot);

        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(result.Blocks));
        var strong = Assert.IsType<StrongInline>(Assert.Single(paragraph.Inlines));
        Assert.Collection(
            strong.Children,
            child => Assert.IsType<TextInline>(child),
            child =>
            {
                var emphasis = Assert.IsType<EmphasisInline>(child);
                Assert.Single(emphasis.Children);
            });
    }

    private static DocumentSnapshot CreateSnapshot(string text)
    {
        return new DocumentSnapshot(
            SourceText.From(text.Replace("\r\n", "\n")),
            new DocumentVersion(1),
            SelectionRange.Collapsed(TextPosition.Zero),
            new EditorOptions(),
            new DocumentMeta(null, null, false, null));
    }
}
