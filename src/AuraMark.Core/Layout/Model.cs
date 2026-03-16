using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using AuraMark.Core.Syntax;
using AuraMark.Core.Text;
using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Layout;

public interface ILayoutTextLine
{
    TextSpan Span { get; }
    Rect Bounds { get; }
    System.Windows.Media.TextFormatting.TextLine TextLine { get; }
    int StartOffset { get; }
    int Length { get; }
}

public abstract record LayoutBlock(TextSpan Span, Rect Bounds);

public sealed record TextLineLayout(
    TextSpan Span,
    Rect Bounds,
    int LineIndex,
    System.Windows.Media.TextFormatting.TextLine TextLine,
    int StartOffset,
    int Length) : ILayoutTextLine
{
    public void Dispose() => TextLine.Dispose();
}

public sealed record CodeLineLayout(
    TextSpan Span,
    Rect Bounds,
    int LineIndex,
    System.Windows.Media.TextFormatting.TextLine TextLine,
    int StartOffset,
    int Length,
    Rect GutterBounds) : ILayoutTextLine
{
    public void Dispose() => TextLine.Dispose();
}

public sealed record LayoutParagraphBlock(
    TextSpan Span,
    Rect Bounds,
    IReadOnlyList<TextLineLayout> Lines)
    : LayoutBlock(Span, Bounds);

public sealed record LayoutHeadingBlock(
    TextSpan Span,
    Rect Bounds,
    int Level,
    IReadOnlyList<TextLineLayout> Lines)
    : LayoutBlock(Span, Bounds);

public sealed record LayoutQuoteBlock(
    TextSpan Span,
    Rect Bounds,
    Rect StripeBounds,
    IReadOnlyList<LayoutBlock> Children)
    : LayoutBlock(Span, Bounds);

public sealed record LayoutListItemLayout(
    TextSpan Span,
    Rect Bounds,
    string MarkerText,
    Rect MarkerBounds,
    IReadOnlyList<LayoutBlock> Children);

public sealed record LayoutListBlock(
    TextSpan Span,
    Rect Bounds,
    bool Ordered,
    IReadOnlyList<LayoutListItemLayout> Items)
    : LayoutBlock(Span, Bounds);

public sealed record LayoutCodeFenceBlock(
    TextSpan Span,
    Rect Bounds,
    Rect ToolbarBounds,
    Rect ContentBounds,
    IReadOnlyList<CodeLineLayout> Lines,
    string? Language)
    : LayoutBlock(Span, Bounds);

public sealed record LayoutDocument(
    long Version,
    IReadOnlyList<LayoutBlock> Blocks,
    double TotalHeight)
{
    public void DisposeLines()
    {
        foreach (var block in Blocks)
        {
            foreach (var line in LayoutLineEnumerator.EnumerateLines(block))
            {
                switch (line)
                {
                    case TextLineLayout textLine:
                        textLine.Dispose();
                        break;
                    case CodeLineLayout codeLine:
                        codeLine.Dispose();
                        break;
                }
            }
        }
    }
}

public sealed record LayoutBuildRequest(ParseResult Parse, ViewportState Viewport, TextSpan DirtySpan);

public sealed record LayoutBuildResult(LayoutDocument Document);

public sealed record ThemeMetrics(
    double PageWidth,
    double ParagraphSpacing,
    double BlockSpacing,
    double CodeBlockPadding,
    double CodeToolbarHeight,
    Typeface BodyTypeface,
    double BodyFontSize,
    Typeface CodeTypeface,
    double CodeFontSize)
{
    public static ThemeMetrics Create(EditorOptions options, ViewportState viewport)
    {
        var pageWidth = viewport.Width > 0
            ? Math.Min(options.PageWidth, viewport.Width)
            : options.PageWidth;

        return new ThemeMetrics(
            Math.Max(240, pageWidth),
            options.ShowParagraphSpacing ? 10 : 4,
            18,
            12,
            32,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            16,
            new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            14);
    }
}

public interface ILayoutEngine
{
    LayoutBuildResult Build(LayoutBuildRequest request, LayoutDocument? previous);
}

public sealed class NullLayoutEngine : ILayoutEngine
{
    public LayoutBuildResult Build(LayoutBuildRequest request, LayoutDocument? previous)
    {
        return new LayoutBuildResult(
            new LayoutDocument(
                request.Parse.Snapshot.Version.Value,
                Array.Empty<LayoutBlock>(),
                0));
    }
}

public static class LayoutLineEnumerator
{
    public static IEnumerable<ILayoutTextLine> EnumerateLines(LayoutDocument? document)
    {
        if (document is null)
        {
            yield break;
        }

        foreach (var block in document.Blocks)
        {
            foreach (var line in EnumerateLines(block))
            {
                yield return line;
            }
        }
    }

    public static IEnumerable<ILayoutTextLine> EnumerateLines(LayoutBlock block)
    {
        switch (block)
        {
            case LayoutParagraphBlock paragraph:
                foreach (var line in paragraph.Lines)
                {
                    yield return line;
                }

                break;
            case LayoutHeadingBlock heading:
                foreach (var line in heading.Lines)
                {
                    yield return line;
                }

                break;
            case LayoutQuoteBlock quote:
                foreach (var child in quote.Children)
                {
                    foreach (var line in EnumerateLines(child))
                    {
                        yield return line;
                    }
                }

                break;
            case LayoutListBlock list:
                foreach (var item in list.Items)
                {
                    foreach (var child in item.Children)
                    {
                        foreach (var line in EnumerateLines(child))
                        {
                            yield return line;
                        }
                    }
                }

                break;
            case LayoutCodeFenceBlock codeFence:
                foreach (var line in codeFence.Lines)
                {
                    yield return line;
                }

                break;
        }
    }
}
