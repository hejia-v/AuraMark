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
