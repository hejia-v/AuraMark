using AuraMark.Core.Text;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace AuraMark.Core.Tests;

public sealed class SourceTextBufferServiceTests
{
    private readonly SourceTextBufferService _service = new();

    [Fact]
    public void Apply_ReplacesSelectionAndAdvancesVersion()
    {
        var snapshot = CreateSnapshot("alpha beta");

        var result = _service.Apply(
            snapshot,
            [new TextEdit(new TextSpan(6, 4), "gamma")]);

        Assert.Equal("alpha gamma", result.Snapshot.Text.ToString());
        Assert.Equal(2, result.Snapshot.Version.Value);
        Assert.True(result.Snapshot.Meta.IsDirty);
        Assert.Equal(6, result.DirtySpan.Start);
        Assert.Equal(11, result.DirtySpan.End);
    }

    [Fact]
    public void Apply_UsesDescendingOrderForMultipleEdits()
    {
        var snapshot = CreateSnapshot("abcd");

        var result = _service.Apply(
            snapshot,
            [
                new TextEdit(new TextSpan(1, 1), "B"),
                new TextEdit(new TextSpan(3, 1), "D"),
            ]);

        Assert.Equal("aBcD", result.Snapshot.Text.ToString());
        Assert.Equal(1, result.DirtySpan.Start);
        Assert.Equal(4, result.DirtySpan.End);
    }

    private static DocumentSnapshot CreateSnapshot(string text)
    {
        return new DocumentSnapshot(
            SourceText.From(text),
            new DocumentVersion(1),
            SelectionRange.Collapsed(TextPosition.Zero),
            new EditorOptions(),
            new DocumentMeta(null, null, false, null));
    }
}
