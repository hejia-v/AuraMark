using System.Globalization;
using System.Windows.Media.TextFormatting;
using AuraMark.Core.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Layout;

public sealed class ParagraphTextSource : TextSource
{
    private readonly string _text;
    private readonly int _documentStart;
    private readonly TextRunProperties _properties;
    private readonly CultureInfo _culture;

    private ParagraphTextSource(string text, int documentStart, TextRunProperties properties)
    {
        _text = text;
        _documentStart = documentStart;
        _properties = properties;
        _culture = properties.CultureInfo;
    }

    public static ParagraphTextSource Create(ParagraphBlock block, SourceText text, MarkdownTextRunPropertyFactory propertyFactory) =>
        new(text.ToString(block.Span), block.Span.Start, propertyFactory.CreateBody());

    public static ParagraphTextSource Create(HeadingBlock block, SourceText text, MarkdownTextRunPropertyFactory propertyFactory) =>
        new(text.ToString(block.Span), block.Span.Start, propertyFactory.CreateHeading(block.Level));

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
