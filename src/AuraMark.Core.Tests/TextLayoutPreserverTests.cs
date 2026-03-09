using Xunit;

namespace AuraMark.Core.Tests;

public sealed class TextLayoutPreserverTests
{
    [Fact]
    public void MergePreservingLayout_ReturnsOriginalWhenOnlyLineEndingsDiffer()
    {
        const string original = "alpha\r\nbeta\r\ngamma";
        const string edited = "alpha\nbeta\ngamma";

        var merged = TextLayoutPreserver.MergePreservingLayout(original, edited);

        Assert.Equal(original, merged);
    }

    [Fact]
    public void MergePreservingLayout_PreservesOriginalLineEndingsForUnchangedLines()
    {
        const string original = "title\r\n\r\nbody line\r\nlast";
        const string edited = "title\n\nbody line updated\nlast";
        const string expected = "title\r\n\r\nbody line updated\r\nlast";

        var merged = TextLayoutPreserver.MergePreservingLayout(original, edited);

        Assert.Equal(expected, merged);
    }

    [Fact]
    public void MergePreservingLayout_KeepsUntouchedMixedLineEndings()
    {
        const string original = "a\r\nb\nc\rd";
        const string edited = "ax\nb\nc\rdz";
        const string expected = "ax\r\nb\nc\rdz";

        var merged = TextLayoutPreserver.MergePreservingLayout(original, edited);

        Assert.Equal(expected, merged);
    }

    [Fact]
    public void MergePreservingLayout_UsesOriginalLineEndingStyleForInsertedLines()
    {
        const string original = "first\r\nsecond";
        const string edited = "first\ninserted\nsecond";
        const string expected = "first\r\ninserted\r\nsecond";

        var merged = TextLayoutPreserver.MergePreservingLayout(original, edited);

        Assert.Equal(expected, merged);
    }

    [Fact]
    public void MergePreservingLayout_PreservesFormattingOnlyBlankLinesAndListMarkers()
    {
        const string original = "intro\r\n\r\n\r\nparagraph\r\n\r\n2. second\r\n3. third\r\n";
        const string edited = "intro\nparagraph\n-\n1. second\n1. third\n";
        const string expected = "intro\r\n\r\n\r\nparagraph\r\n-\r\n2. second\r\n3. third\r\n";

        var merged = TextLayoutPreserver.MergePreservingLayout(original, edited);

        Assert.Equal(expected, merged);
    }
}
