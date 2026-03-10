using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using Microsoft.CodeAnalysis.Text;
using AuraMark.Core.Syntax;
using AuraMark.Core.Text;

namespace AuraMark.Core.Layout;

public sealed record LayoutDocument(long Version, IReadOnlyList<LayoutBlock> Blocks, double TotalHeight)
{
    public void DisposeLines()
    {
        foreach (var block in Blocks)
        {
            switch (block)
            {
                case LayoutParagraphBlock p:
                    foreach (var line in p.Lines) line.TextLine.Dispose();
                    break;
                case LayoutHeadingBlock h:
                    foreach (var line in h.Lines) line.TextLine.Dispose();
                    break;
                case LayoutCodeFenceBlock c:
                    foreach (var line in c.Lines) line.TextLine.Dispose();
                    break;
            }
        }
    }
}

public abstract record LayoutBlock(TextSpan Span, Rect Bounds, int ZIndex);
public sealed record LayoutParagraphBlock(TextSpan Span, Rect Bounds, IReadOnlyList<TextLineLayout> Lines) : LayoutBlock(Span, Bounds, 0);
public sealed record LayoutHeadingBlock(TextSpan Span, Rect Bounds, int Level, IReadOnlyList<TextLineLayout> Lines) : LayoutBlock(Span, Bounds, 0);
public sealed record LayoutCodeFenceBlock(TextSpan Span, Rect Bounds, string? Language, Rect ToolbarRect, IReadOnlyList<CodeLineLayout> Lines) : LayoutBlock(Span, Bounds, 1);
public sealed record TextLineLayout(TextSpan Span, Rect Bounds, int LineIndex, TextLine TextLine, int StartOffset, int Length);
public sealed record CodeLineLayout(TextSpan Span, Rect Bounds, int LineNumber, TextLine TextLine, int StartOffset, int Length);

public sealed record ThemeMetrics(double PageWidth, double ParagraphSpacing, double BlockSpacing, double CodeBlockPadding, double CodeToolbarHeight, Typeface BodyTypeface, double BodyFontSize, Typeface CodeTypeface, double CodeFontSize);
public sealed record ThemePalette(Brush Foreground, Brush MutedForeground, Brush CodeForeground, Brush CodeBackground, Brush LinkForeground, Brush SelectionBackground);

public readonly record struct InlineRunInfo(TextSpan Span, TextRunProperties Properties, InlineRunKind Kind);
public enum InlineRunKind { Text, Emphasis, Strong, Code }

public sealed class SimpleTextRunProperties : TextRunProperties
{
    public override Typeface Typeface { get; }
    public override double FontRenderingEmSize { get; }
    public override double FontHintingEmSize { get; }
    public override TextDecorationCollection? TextDecorations { get; }
    public override Brush ForegroundBrush { get; }
    public override Brush? BackgroundBrush { get; }
    public override CultureInfo CultureInfo { get; }
    public override TextEffectCollection? TextEffects { get; }
    public override BaselineAlignment BaselineAlignment { get; }
    public override TextRunTypographyProperties? TypographyProperties { get; }
    public override NumberSubstitution? NumberSubstitution { get; }

    public SimpleTextRunProperties(Typeface typeface, double fontSize, Brush foreground, Brush? background = null, TextDecorationCollection? decorations = null, CultureInfo? culture = null)
    {
        Typeface = typeface; FontRenderingEmSize = fontSize; FontHintingEmSize = fontSize; ForegroundBrush = foreground; BackgroundBrush = background; TextDecorations = decorations; CultureInfo = culture ?? CultureInfo.CurrentUICulture;
    }
}

public interface ITextRunPropertyFactory
{
    SimpleTextRunProperties CreateBody();
    SimpleTextRunProperties CreateEmphasis();
    SimpleTextRunProperties CreateStrong();
    SimpleTextRunProperties CreateInlineCode();
    SimpleTextRunProperties CreateLink();
    SimpleTextRunProperties CreateCodeLine();
}

public sealed class DefaultTextRunPropertyFactory : ITextRunPropertyFactory
{
    private readonly ThemeMetrics _theme; private readonly ThemePalette _palette;
    public DefaultTextRunPropertyFactory(ThemeMetrics theme, ThemePalette palette) { _theme = theme; _palette = palette; }
    public SimpleTextRunProperties CreateBody() => new(_theme.BodyTypeface, _theme.BodyFontSize, _palette.Foreground);
    public SimpleTextRunProperties CreateEmphasis() => new(new Typeface(_theme.BodyTypeface.FontFamily, FontStyles.Italic, _theme.BodyTypeface.Weight, _theme.BodyTypeface.Stretch), _theme.BodyFontSize, _palette.Foreground);
    public SimpleTextRunProperties CreateStrong() => new(new Typeface(_theme.BodyTypeface.FontFamily, _theme.BodyTypeface.Style, FontWeights.SemiBold, _theme.BodyTypeface.Stretch), _theme.BodyFontSize, _palette.Foreground);
    public SimpleTextRunProperties CreateInlineCode() => new(_theme.CodeTypeface, _theme.CodeFontSize, _palette.CodeForeground, _palette.CodeBackground);
    public SimpleTextRunProperties CreateLink() => new(_theme.BodyTypeface, _theme.BodyFontSize, _palette.LinkForeground, null, TextDecorations.Underline);
    public SimpleTextRunProperties CreateCodeLine() => new(_theme.CodeTypeface, _theme.CodeFontSize, _palette.CodeForeground);
}

public sealed class BasicParagraphProperties : TextParagraphProperties
{
    private readonly TextRunProperties _defaultTextRunProperties;
    public BasicParagraphProperties(TextRunProperties defaultTextRunProperties) { _defaultTextRunProperties = defaultTextRunProperties; }
    public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
    public override TextAlignment TextAlignment => TextAlignment.Left;
    public override double LineHeight => double.NaN;
    public override bool FirstLineInParagraph => true;
    public override TextRunProperties DefaultTextRunProperties => _defaultTextRunProperties;
    public override TextWrapping TextWrapping => TextWrapping.Wrap;
    public override TextMarkerProperties? TextMarkerProperties => null;
    public override double Indent => 0;
    public override TextDecorationCollection? TextDecorations => null;
}

public static class InlineRunBuilder
{
    public static IReadOnlyList<InlineRunInfo> Build(IReadOnlyList<MdInline> inlines, SourceText text, ITextRunPropertyFactory factory)
    {
        var result = new List<InlineRunInfo>();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextInline t: result.Add(new InlineRunInfo(t.Span, factory.CreateBody(), InlineRunKind.Text)); break;
                case CodeInline c: result.Add(new InlineRunInfo(c.Span, factory.CreateInlineCode(), InlineRunKind.Code)); break;
                case LinkInline l: result.Add(new InlineRunInfo(l.LabelSpan, factory.CreateLink(), InlineRunKind.Text)); break;
                default: result.Add(new InlineRunInfo(inline.Span, factory.CreateBody(), InlineRunKind.Text)); break;
            }
        }
        return result;
    }
}

public sealed class ParagraphTextSource : TextSource
{
    private readonly SourceText _text; private readonly TextSpan _paragraphSpan; private readonly IReadOnlyList<InlineRunInfo> _runs;
    private ParagraphTextSource(SourceText text, TextSpan paragraphSpan, IReadOnlyList<InlineRunInfo> runs) { _text = text; _paragraphSpan = paragraphSpan; _runs = runs; }
    public static ParagraphTextSource Create(ParagraphBlock block, SourceText text, ITextRunPropertyFactory propertyFactory) => new(text, block.Span, InlineRunBuilder.Build(block.Inlines, text, propertyFactory));
    public override TextRun GetTextRun(int textSourceCharacterIndex)
    {
        if (textSourceCharacterIndex >= _paragraphSpan.End) return new TextEndOfParagraph(1);
        var run = _runs.FirstOrDefault(r => textSourceCharacterIndex >= r.Span.Start && textSourceCharacterIndex < r.Span.End);
        if (run == default) return new TextEndOfParagraph(1);
        var s = _text.ToString(run.Span);
        return new TextCharacters(s, 0, s.Length, run.Properties);
    }
    public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit) => new(0, new CultureSpecificCharacterBufferRange(CultureInfo.CurrentUICulture, new CharacterBufferRange(string.Empty, 0, 0)));
    public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex) => textSourceCharacterIndex;
}

public sealed class CodeLineTextSource : TextSource
{
    private readonly string _text; private readonly SimpleTextRunProperties _properties;
    private CodeLineTextSource(string text, SimpleTextRunProperties properties) { _text = text; _properties = properties; }
    public static CodeLineTextSource Create(string text, ITextRunPropertyFactory propertyFactory) => new(text, propertyFactory.CreateCodeLine());
    public override TextRun GetTextRun(int textSourceCharacterIndex) => textSourceCharacterIndex >= _text.Length ? new TextEndOfParagraph(1) : new TextCharacters(_text, textSourceCharacterIndex, _text.Length - textSourceCharacterIndex, _properties);
    public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit) => new(0, new CultureSpecificCharacterBufferRange(CultureInfo.CurrentUICulture, new CharacterBufferRange(string.Empty, 0, 0)));
    public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex) => textSourceCharacterIndex;
}

public sealed record LayoutBuildRequest(ParseResult Parse, ViewportState Viewport, TextSpan? DirtySpan);
public sealed record LayoutBuildResult(LayoutDocument Document, IReadOnlyList<TextSpan> ReusedBlocks, IReadOnlyList<TextSpan> RelaidBlocks);
public interface ILayoutEngine { LayoutBuildResult Build(LayoutBuildRequest request, LayoutDocument? previous); }

public sealed class ParagraphLayouter
{
    private readonly TextFormatter _formatter; private readonly ITextRunPropertyFactory _propertyFactory;
    public ParagraphLayouter(TextFormatter formatter, ITextRunPropertyFactory propertyFactory) { _formatter = formatter; _propertyFactory = propertyFactory; }
    public LayoutParagraphBlock LayoutParagraph(ParagraphBlock block, SourceText text, double x, double y, double width)
    {
        var source = ParagraphTextSource.Create(block, text, _propertyFactory);
        var paragraphProps = new BasicParagraphProperties(_propertyFactory.CreateBody());
        var lines = new List<TextLineLayout>();
        double currentY = y; int absolutePosition = block.Span.Start; int lineIndex = 0;
        while (absolutePosition < block.Span.End)
        {
            var textLine = _formatter.FormatLine(source, absolutePosition, width, paragraphProps, null);
            int consumed = textLine.Length;
            if (consumed <= 0) { textLine.Dispose(); break; }
            var bounds = new Rect(x, currentY, Math.Max(0, textLine.WidthIncludingTrailingWhitespace), textLine.Height);
            lines.Add(new TextLineLayout(TextSpan.FromBounds(absolutePosition, absolutePosition + consumed), bounds, lineIndex, textLine, absolutePosition, consumed));
            absolutePosition += consumed; currentY += textLine.Height; lineIndex++;
        }
        return new LayoutParagraphBlock(block.Span, new Rect(x, y, width, Math.Max(0, currentY - y)), lines);
    }
}

public sealed class HeadingLayouter
{
    private readonly TextFormatter _formatter;
    public HeadingLayouter(TextFormatter formatter) => _formatter = formatter;
    public LayoutHeadingBlock LayoutHeading(HeadingBlock block, SourceText text, double x, double y, double width, TextRunProperties headingProps)
    {
        var sourceText = text.ToString(block.Span);
        var source = CodeLineTextSource.Create(sourceText, new DefaultTextRunPropertyFactory(new ThemeMetrics(860,8,16,12,36,new Typeface("Segoe UI"),16,new Typeface("Cascadia Code"),14), new ThemePalette(Brushes.Black, Brushes.Gray, Brushes.Black, Brushes.LightGray, Brushes.SteelBlue, Brushes.LightBlue)));
        var paragraphProps = new BasicParagraphProperties(headingProps);
        var lines = new List<TextLineLayout>();
        double currentY = y; int sourcePosition = 0; int lineIndex = 0;
        while (sourcePosition < sourceText.Length)
        {
            var textLine = _formatter.FormatLine(source, sourcePosition, width, paragraphProps, null);
            int consumed = textLine.Length;
            if (consumed <= 0) { textLine.Dispose(); break; }
            var bounds = new Rect(x, currentY, textLine.WidthIncludingTrailingWhitespace, textLine.Height);
            lines.Add(new TextLineLayout(TextSpan.FromBounds(block.Span.Start + sourcePosition, block.Span.Start + sourcePosition + consumed), bounds, lineIndex, textLine, block.Span.Start + sourcePosition, consumed));
            sourcePosition += consumed; currentY += textLine.Height; lineIndex++;
        }
        return new LayoutHeadingBlock(block.Span, new Rect(x, y, width, currentY - y), block.Level, lines);
    }
}

public sealed class CodeFenceLayouter
{
    private readonly TextFormatter _formatter; private readonly ITextRunPropertyFactory _propertyFactory; private readonly ThemeMetrics _theme;
    public CodeFenceLayouter(TextFormatter formatter, ITextRunPropertyFactory propertyFactory, ThemeMetrics theme) { _formatter = formatter; _propertyFactory = propertyFactory; _theme = theme; }
    public LayoutCodeFenceBlock LayoutCodeFence(CodeFenceBlock block, SourceText text, double x, double y, double width)
    {
        string[] logicalLines = text.ToString(block.ContentSpan).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        double outerPadding = _theme.CodeBlockPadding; double toolbarHeight = _theme.CodeToolbarHeight; double gutterWidth = 36; double contentX = x + outerPadding + gutterWidth; double contentY = y + toolbarHeight + outerPadding; double contentWidth = Math.Max(0, width - outerPadding * 2 - gutterWidth);
        var toolbarRect = new Rect(x, y, width, toolbarHeight); var codeLines = new List<CodeLineLayout>(); int absoluteOffset = block.ContentSpan.Start; double currentY = contentY;
        for (int i = 0; i < logicalLines.Length; i++)
        {
            string lineText = logicalLines[i];
            var source = CodeLineTextSource.Create(lineText, _propertyFactory);
            var paragraphProps = new BasicParagraphProperties(_propertyFactory.CreateCodeLine());
            var textLine = _formatter.FormatLine(source, 0, contentWidth, paragraphProps, null);
            var bounds = new Rect(contentX, currentY, Math.Max(0, textLine.WidthIncludingTrailingWhitespace), textLine.Height);
            codeLines.Add(new CodeLineLayout(new TextSpan(absoluteOffset, lineText.Length), bounds, i + 1, textLine, absoluteOffset, lineText.Length));
            currentY += textLine.Height; absoluteOffset += lineText.Length + 1;
        }
        return new LayoutCodeFenceBlock(block.Span, new Rect(x, y, width, (currentY - y) + outerPadding), block.Language, toolbarRect, codeLines);
    }
}

public sealed class LayoutEngine : ILayoutEngine
{
    private readonly ParagraphLayouter _paragraph; private readonly HeadingLayouter _heading; private readonly CodeFenceLayouter _codeFence; private readonly ITextRunPropertyFactory _propertyFactory; private readonly ThemeMetrics _theme;
    public LayoutEngine(ParagraphLayouter paragraph, HeadingLayouter heading, CodeFenceLayouter codeFence, ITextRunPropertyFactory propertyFactory, ThemeMetrics theme) { _paragraph = paragraph; _heading = heading; _codeFence = codeFence; _propertyFactory = propertyFactory; _theme = theme; }
    public LayoutBuildResult Build(LayoutBuildRequest request, LayoutDocument? previous)
    {
        var blocks = new List<LayoutBlock>();
        double pageX = Math.Max(24, (request.Viewport.Width - _theme.PageWidth) / 2); double pageY = 24; double pageWidth = Math.Min(request.Viewport.Width - 48, _theme.PageWidth);
        foreach (var mdBlock in request.Parse.Blocks)
        {
            LayoutBlock layoutBlock = mdBlock switch
            {
                ParagraphBlock p => _paragraph.LayoutParagraph(p, request.Parse.Snapshot.Text, pageX, pageY, pageWidth),
                HeadingBlock h => _heading.LayoutHeading(h, request.Parse.Snapshot.Text, pageX, pageY, pageWidth, _propertyFactory.CreateStrong()),
                CodeFenceBlock c => _codeFence.LayoutCodeFence(c, request.Parse.Snapshot.Text, pageX, pageY, pageWidth),
                _ => _paragraph.LayoutParagraph(new ParagraphBlock(mdBlock.Span, new MdInline[] { new TextInline(mdBlock.Span) }), request.Parse.Snapshot.Text, pageX, pageY, pageWidth)
            };
            blocks.Add(layoutBlock); pageY = layoutBlock.Bounds.Bottom + _theme.BlockSpacing;
        }
        return new LayoutBuildResult(new LayoutDocument(request.Parse.Snapshot.Version.Value, blocks, pageY), Array.Empty<TextSpan>(), request.Parse.Blocks.Select(b => b.Span).ToArray());
    }
}
