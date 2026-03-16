using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using AuraMark.Core.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Layout;

public sealed class LayoutEngine : ILayoutEngine
{
    private readonly TextFormatter _formatter;

    public LayoutEngine()
    {
        _formatter = TextFormatter.Create();
    }

    public LayoutBuildResult Build(LayoutBuildRequest request, LayoutDocument? previous)
    {
        previous?.DisposeLines();

        var metrics = ThemeMetrics.Create(request.Parse.Snapshot.Options, request.Viewport);
        var propertyFactory = new MarkdownTextRunPropertyFactory(metrics);
        var width = metrics.PageWidth;
        var x = 0d;
        var y = 0d;
        var blocks = new List<LayoutBlock>(request.Parse.Blocks.Count);

        foreach (var block in request.Parse.Blocks)
        {
            var layoutBlock = LayoutMarkdownBlock(block, request.Parse.Snapshot.Text, propertyFactory, metrics, x, y, width);

            if (layoutBlock is null)
            {
                continue;
            }

            blocks.Add(layoutBlock);
            y = layoutBlock.Bounds.Bottom + metrics.BlockSpacing;
        }

        var totalHeight = blocks.Count == 0 ? 0 : Math.Max(0, y - metrics.BlockSpacing);
        return new LayoutBuildResult(new LayoutDocument(request.Parse.Snapshot.Version.Value, blocks, totalHeight));
    }

    private LayoutBlock? LayoutMarkdownBlock(
        MdBlock block,
        SourceText text,
        MarkdownTextRunPropertyFactory propertyFactory,
        ThemeMetrics metrics,
        double x,
        double y,
        double width)
    {
        return block switch
        {
            ParagraphBlock paragraph => LayoutParagraph(paragraph, text, propertyFactory, metrics, x, y, width),
            HeadingBlock heading => LayoutHeading(heading, text, propertyFactory, metrics, x, y, width),
            QuoteBlock quote => LayoutQuote(quote, text, propertyFactory, metrics, x, y, width),
            ListBlock list => LayoutList(list, text, propertyFactory, metrics, x, y, width),
            CodeFenceBlock codeFence => LayoutCodeFence(codeFence, text, propertyFactory, metrics, x, y, width),
            _ => null,
        };
    }

    private LayoutParagraphBlock LayoutParagraph(
        ParagraphBlock block,
        SourceText text,
        MarkdownTextRunPropertyFactory propertyFactory,
        ThemeMetrics metrics,
        double x,
        double y,
        double width)
    {
        var source = ParagraphTextSource.Create(block, text, propertyFactory);
        var paragraphProps = new BasicParagraphProperties(propertyFactory.CreateBody());
        var lines = FormatTextLines(source, block.Span.Start, block.Span.End, x, y, width, paragraphProps);
        var height = Math.Max(0, lines.Count == 0 ? 0 : lines[^1].Bounds.Bottom - y);
        return new LayoutParagraphBlock(block.Span, new Rect(x, y, width, height + metrics.ParagraphSpacing), lines);
    }

    private LayoutHeadingBlock LayoutHeading(
        HeadingBlock block,
        SourceText text,
        MarkdownTextRunPropertyFactory propertyFactory,
        ThemeMetrics metrics,
        double x,
        double y,
        double width)
    {
        var source = ParagraphTextSource.Create(block, text, propertyFactory);
        var paragraphProps = new BasicParagraphProperties(propertyFactory.CreateHeading(block.Level));
        var lines = FormatTextLines(source, block.Span.Start, block.Span.End, x, y, width, paragraphProps);
        var extraSpacing = block.Level <= 2 ? metrics.ParagraphSpacing + 6 : metrics.ParagraphSpacing;
        var height = Math.Max(0, lines.Count == 0 ? 0 : lines[^1].Bounds.Bottom - y);
        return new LayoutHeadingBlock(block.Span, new Rect(x, y, width, height + extraSpacing), block.Level, lines);
    }

    private LayoutCodeFenceBlock LayoutCodeFence(
        CodeFenceBlock block,
        SourceText text,
        MarkdownTextRunPropertyFactory propertyFactory,
        ThemeMetrics metrics,
        double x,
        double y,
        double width)
    {
        var padding = metrics.CodeBlockPadding;
        var toolbarBounds = new Rect(x, y, width, metrics.CodeToolbarHeight);
        var gutterWidth = 44d;
        var contentX = x + padding + gutterWidth;
        var contentY = toolbarBounds.Bottom + padding;
        var contentWidth = Math.Max(120, width - gutterWidth - padding * 2);
        var lines = new List<CodeLineLayout>();
        var lineSpans = GetCodeLineSpans(text, block.ContentSpan);
        var lineY = contentY;

        foreach (var (lineSpan, lineIndex) in lineSpans.Select((span, index) => (span, index)))
        {
            var lineText = text.ToString(lineSpan);
            var source = CodeLineTextSource.Create(lineText, lineSpan.Start, propertyFactory);
            var textLine = _formatter.FormatLine(
                source,
                lineSpan.Start,
                Math.Max(contentWidth, 4096),
                new BasicParagraphProperties(propertyFactory.CreateCodeLine(), wrap: false, defaultIncrementalTab: 4 * metrics.CodeFontSize),
                previousLineBreak: null);

            var lineBounds = new Rect(
                contentX,
                lineY,
                Math.Max(0, Math.Min(contentWidth, textLine.WidthIncludingTrailingWhitespace)),
                textLine.Height);

            var gutterBounds = new Rect(x + padding, lineY, gutterWidth - 8, textLine.Height);
            lines.Add(new CodeLineLayout(lineSpan, lineBounds, lineIndex, textLine, lineSpan.Start, lineSpan.Length, gutterBounds));
            lineY += textLine.Height;
        }

        if (lines.Count == 0)
        {
            lineY = contentY + metrics.CodeFontSize * 1.8;
        }

        var contentBounds = new Rect(x + padding, toolbarBounds.Bottom, width - padding * 2, Math.Max(0, lineY - toolbarBounds.Bottom + padding));
        var cardBounds = new Rect(x, y, width, contentBounds.Bottom - y);
        return new LayoutCodeFenceBlock(block.Span, cardBounds, toolbarBounds, contentBounds, lines, block.Language);
    }

    private LayoutQuoteBlock LayoutQuote(
        QuoteBlock block,
        SourceText text,
        MarkdownTextRunPropertyFactory propertyFactory,
        ThemeMetrics metrics,
        double x,
        double y,
        double width)
    {
        const double quoteIndent = 24;
        const double quoteTopPadding = 4;
        const double quoteBottomPadding = 6;

        var childX = x + quoteIndent;
        var childY = y + quoteTopPadding;
        var childWidth = Math.Max(120, width - quoteIndent);
        var children = new List<LayoutBlock>(block.Children.Count);

        foreach (var child in block.Children)
        {
            var childLayout = LayoutMarkdownBlock(child, text, propertyFactory, metrics, childX, childY, childWidth);
            if (childLayout is null)
            {
                continue;
            }

            children.Add(childLayout);
            childY = childLayout.Bounds.Bottom + metrics.BlockSpacing;
        }

        var contentBottom = children.Count == 0
            ? y + metrics.BodyFontSize * 1.5
            : children[^1].Bounds.Bottom;
        var bounds = new Rect(x, y, width, Math.Max(metrics.BodyFontSize * 1.5, contentBottom - y + quoteBottomPadding));
        var stripeBounds = new Rect(x + 6, y + 4, 4, Math.Max(12, bounds.Height - 8));
        return new LayoutQuoteBlock(block.Span, bounds, stripeBounds, children);
    }

    private LayoutListBlock LayoutList(
        ListBlock block,
        SourceText text,
        MarkdownTextRunPropertyFactory propertyFactory,
        ThemeMetrics metrics,
        double x,
        double y,
        double width)
    {
        const double markerWidth = 28;
        const double contentIndent = 32;

        var items = new List<LayoutListItemLayout>(block.Items.Count);
        var currentY = y;

        foreach (var (item, itemIndex) in block.Items.Select((value, index) => (value, index)))
        {
            var childX = x + contentIndent;
            var childY = currentY;
            var childWidth = Math.Max(120, width - contentIndent);
            var childLayouts = new List<LayoutBlock>(item.Children.Count);

            foreach (var child in item.Children)
            {
                var childLayout = LayoutMarkdownBlock(child, text, propertyFactory, metrics, childX, childY, childWidth);
                if (childLayout is null)
                {
                    continue;
                }

                childLayouts.Add(childLayout);
                childY = childLayout.Bounds.Bottom + metrics.ParagraphSpacing;
            }

            var contentBottom = childLayouts.Count == 0
                ? currentY + metrics.BodyFontSize * 1.5
                : childLayouts[^1].Bounds.Bottom;
            var itemHeight = Math.Max(metrics.BodyFontSize * 1.5, contentBottom - currentY);
            var markerBounds = new Rect(x, currentY, markerWidth, itemHeight);
            var itemBounds = new Rect(x, currentY, width, itemHeight + metrics.ParagraphSpacing);
            items.Add(new LayoutListItemLayout(
                item.Span,
                itemBounds,
                block.Ordered ? $"{itemIndex + 1}." : "•",
                markerBounds,
                childLayouts));

            currentY = itemBounds.Bottom;
        }

        var totalHeight = items.Count == 0 ? 0 : items[^1].Bounds.Bottom - y;
        return new LayoutListBlock(block.Span, new Rect(x, y, width, totalHeight), block.Ordered, items);
    }

    private List<TextLineLayout> FormatTextLines(
        TextSource source,
        int start,
        int end,
        double x,
        double y,
        double width,
        TextParagraphProperties paragraphProperties)
    {
        var lines = new List<TextLineLayout>();
        var currentY = y;
        var absolutePosition = start;
        var lineIndex = 0;

        while (absolutePosition < end)
        {
            var textLine = _formatter.FormatLine(source, absolutePosition, width, paragraphProperties, previousLineBreak: null);
            var consumed = textLine.Length;
            if (consumed <= 0)
            {
                textLine.Dispose();
                break;
            }

            var bounds = new Rect(
                x,
                currentY,
                Math.Max(0, Math.Min(width, textLine.WidthIncludingTrailingWhitespace)),
                textLine.Height);

            lines.Add(new TextLineLayout(
                TextSpan.FromBounds(absolutePosition, Math.Min(end, absolutePosition + consumed)),
                bounds,
                lineIndex,
                textLine,
                absolutePosition,
                consumed));

            absolutePosition += consumed;
            currentY += textLine.Height;
            lineIndex++;
        }

        return lines;
    }

    private static IReadOnlyList<TextSpan> GetCodeLineSpans(SourceText text, TextSpan contentSpan)
    {
        var spans = new List<TextSpan>();
        if (contentSpan.Length == 0)
        {
            return spans;
        }

        var lineIndex = text.Lines.GetLineFromPosition(contentSpan.Start).LineNumber;
        while (lineIndex < text.Lines.Count)
        {
            var line = text.Lines[lineIndex];
            var start = Math.Max(contentSpan.Start, line.Start);
            var end = Math.Min(contentSpan.End, line.End);
            spans.Add(TextSpan.FromBounds(start, end));

            if (line.EndIncludingLineBreak >= contentSpan.End)
            {
                break;
            }

            lineIndex++;
        }

        return spans;
    }
}
