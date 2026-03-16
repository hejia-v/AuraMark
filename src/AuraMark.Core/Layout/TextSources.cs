using System.Globalization;
using System.Linq;
using System.Windows.Media.TextFormatting;
using AuraMark.Core.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Layout;

public sealed class ParagraphTextSource : TextSource
{
    private readonly IReadOnlyList<InlineRunSegment> _segments;
    private readonly int _documentStart;
    private readonly int _documentEnd;
    private readonly string _fullText;
    private readonly CultureInfo _culture;

    private ParagraphTextSource(
        IReadOnlyList<InlineRunSegment> segments,
        int documentStart,
        int documentEnd,
        TextRunProperties defaultProperties)
    {
        _segments = segments;
        _documentStart = documentStart;
        _documentEnd = documentEnd;
        _fullText = string.Concat(segments.Select(segment => segment.Text));
        _culture = defaultProperties.CultureInfo;
    }

    public static ParagraphTextSource Create(ParagraphBlock block, SourceText text, MarkdownTextRunPropertyFactory propertyFactory)
    {
        var defaultProperties = propertyFactory.CreateBody();
        var segments = BuildSegments(block.Span, block.Inlines, text, propertyFactory, defaultProperties);
        return new ParagraphTextSource(segments, block.Span.Start, block.Span.End, defaultProperties);
    }

    public static ParagraphTextSource Create(HeadingBlock block, SourceText text, MarkdownTextRunPropertyFactory propertyFactory)
    {
        var defaultProperties = propertyFactory.CreateHeading(block.Level);
        var segments = BuildSegments(block.Span, block.Inlines, text, propertyFactory, defaultProperties);
        return new ParagraphTextSource(segments, block.Span.Start, block.Span.End, defaultProperties);
    }

    public override TextRun GetTextRun(int textSourceCharacterIndex)
    {
        if (textSourceCharacterIndex < _documentStart || textSourceCharacterIndex >= _documentEnd)
        {
            return new TextEndOfParagraph(1);
        }

        foreach (var segment in _segments)
        {
            if (textSourceCharacterIndex < segment.DocumentStart || textSourceCharacterIndex >= segment.DocumentEnd)
            {
                continue;
            }

            var relativeIndex = textSourceCharacterIndex - segment.DocumentStart;
            return new TextCharacters(segment.Text, relativeIndex, segment.DocumentEnd - textSourceCharacterIndex, segment.Properties);
        }

        return new TextEndOfParagraph(1);
    }

    public override System.Windows.Media.TextFormatting.TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit)
    {
        var count = Math.Clamp(textSourceCharacterIndexLimit - _documentStart, 0, _fullText.Length);
        var range = new CharacterBufferRange(_fullText, 0, count);
        return new System.Windows.Media.TextFormatting.TextSpan<CultureSpecificCharacterBufferRange>(
            count,
            new CultureSpecificCharacterBufferRange(_culture, range));
    }

    public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex) =>
        Math.Max(0, textSourceCharacterIndex - _documentStart);

    private static IReadOnlyList<InlineRunSegment> BuildSegments(
        TextSpan blockSpan,
        IReadOnlyList<MdInline> inlines,
        SourceText text,
        MarkdownTextRunPropertyFactory propertyFactory,
        TextRunProperties defaultProperties)
    {
        if (inlines.Count == 0)
        {
            return
            [
                new InlineRunSegment(text.ToString(blockSpan), blockSpan.Start, blockSpan.End, defaultProperties),
            ];
        }

        var segments = new List<InlineRunSegment>(inlines.Count);
        AppendSegments(segments, inlines, text, propertyFactory, defaultProperties);

        if (segments.Count == 0)
        {
            segments.Add(new InlineRunSegment(text.ToString(blockSpan), blockSpan.Start, blockSpan.End, defaultProperties));
        }

        return segments;
    }

    private static void AppendSegments(
        List<InlineRunSegment> segments,
        IReadOnlyList<MdInline> inlines,
        SourceText text,
        MarkdownTextRunPropertyFactory propertyFactory,
        TextRunProperties inheritedProperties)
    {
        foreach (var inline in inlines)
        {
            AppendInlineSegments(segments, inline, text, propertyFactory, inheritedProperties);
        }
    }

    private static void AppendInlineSegments(
        List<InlineRunSegment> segments,
        MdInline inline,
        SourceText text,
        MarkdownTextRunPropertyFactory propertyFactory,
        TextRunProperties inheritedProperties)
    {
        var span = inline.Span;
        if (span.Length == 0)
        {
            return;
        }

        var inlineProperties = propertyFactory.CreateInline(inline, inheritedProperties);
        var children = GetChildren(inline);
        if (children.Count == 0)
        {
            AddSegment(segments, text, span, inlineProperties);
            return;
        }

        var cursor = span.Start;
        foreach (var child in children)
        {
            if (child.Span.Start > cursor)
            {
                AddSegment(segments, text, TextSpan.FromBounds(cursor, child.Span.Start), inlineProperties);
            }

            AppendInlineSegments(segments, child, text, propertyFactory, inlineProperties);
            cursor = Math.Max(cursor, child.Span.End);
        }

        if (cursor < span.End)
        {
            AddSegment(segments, text, TextSpan.FromBounds(cursor, span.End), inlineProperties);
        }
    }

    private static IReadOnlyList<MdInline> GetChildren(MdInline inline) =>
        inline switch
        {
            StrongInline strong => strong.Children,
            EmphasisInline emphasis => emphasis.Children,
            LinkInline link => link.Children,
            _ => Array.Empty<MdInline>(),
        };

    private static void AddSegment(
        List<InlineRunSegment> segments,
        SourceText text,
        TextSpan span,
        TextRunProperties properties)
    {
        if (span.Length == 0)
        {
            return;
        }

        segments.Add(new InlineRunSegment(text.ToString(span), span.Start, span.End, properties));
    }

    private sealed record InlineRunSegment(string Text, int DocumentStart, int DocumentEnd, TextRunProperties Properties);
}

public sealed class CodeLineTextSource : TextSource
{
    private readonly string _text;
    private readonly int _documentStart;
    private readonly TextRunProperties _properties;
    private readonly CultureInfo _culture;

    private CodeLineTextSource(string text, int documentStart, TextRunProperties properties)
    {
        _text = text;
        _documentStart = documentStart;
        _properties = properties;
        _culture = properties.CultureInfo;
    }

    public static CodeLineTextSource Create(string text, int documentStart, MarkdownTextRunPropertyFactory propertyFactory) =>
        new(text, documentStart, propertyFactory.CreateCodeLine());

    public override TextRun GetTextRun(int textSourceCharacterIndex)
    {
        var relativeIndex = textSourceCharacterIndex - _documentStart;
        if (relativeIndex < 0 || relativeIndex >= _text.Length)
        {
            return new TextEndOfParagraph(1);
        }

        return new TextCharacters(_text, relativeIndex, _text.Length - relativeIndex, _properties);
    }

    public override System.Windows.Media.TextFormatting.TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit)
    {
        var count = Math.Clamp(textSourceCharacterIndexLimit - _documentStart, 0, _text.Length);
        var range = new CharacterBufferRange(_text, 0, count);
        return new System.Windows.Media.TextFormatting.TextSpan<CultureSpecificCharacterBufferRange>(
            count,
            new CultureSpecificCharacterBufferRange(_culture, range));
    }

    public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex) =>
        Math.Max(0, textSourceCharacterIndex - _documentStart);
}
