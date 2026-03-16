using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace AuraMark.Core.Layout;

public sealed class SimpleTextRunProperties : TextRunProperties
{
    private static readonly TextDecorationCollection EmptyDecorations = new();
    private static readonly TextEffectCollection EmptyEffects = new();
    private static readonly TextRunTypographyProperties DefaultTypography = new DefaultTypographyProperties();

    private readonly Typeface _typeface;
    private readonly double _fontSize;
    private readonly Brush _foreground;
    private readonly Brush? _background;
    private readonly CultureInfo _culture;
    private readonly TextDecorationCollection _textDecorations;
    private readonly TextEffectCollection _textEffects;

    public SimpleTextRunProperties(
        Typeface typeface,
        double fontSize,
        Brush foreground,
        Brush? background = null,
        TextDecorationCollection? textDecorations = null,
        CultureInfo? culture = null)
    {
        _typeface = typeface;
        _fontSize = fontSize;
        _foreground = foreground;
        _background = background;
        _culture = culture ?? CultureInfo.CurrentUICulture;
        _textDecorations = textDecorations ?? EmptyDecorations;
        _textEffects = EmptyEffects;
    }

    public override Brush? BackgroundBrush => _background;

    public override BaselineAlignment BaselineAlignment => BaselineAlignment.Baseline;

    public override CultureInfo CultureInfo => _culture;

    public override double FontHintingEmSize => _fontSize;

    public override double FontRenderingEmSize => _fontSize;

    public override Brush ForegroundBrush => _foreground;

    public override NumberSubstitution? NumberSubstitution => null;

    public override TextDecorationCollection TextDecorations => _textDecorations;

    public override TextEffectCollection TextEffects => _textEffects;

    public override Typeface Typeface => _typeface;

    public override TextRunTypographyProperties TypographyProperties => DefaultTypography;

    private sealed class DefaultTypographyProperties : TextRunTypographyProperties
    {
        public override int AnnotationAlternates => 0;
        public override FontCapitals Capitals => default;
        public override bool CapitalSpacing => false;
        public override bool CaseSensitiveForms => false;
        public override bool ContextualAlternates => true;
        public override bool ContextualLigatures => true;
        public override int ContextualSwashes => 0;
        public override bool DiscretionaryLigatures => false;
        public override bool EastAsianExpertForms => false;
        public override FontEastAsianLanguage EastAsianLanguage => default;
        public override FontEastAsianWidths EastAsianWidths => default;
        public override FontFraction Fraction => default;
        public override bool HistoricalForms => false;
        public override bool HistoricalLigatures => false;
        public override bool Kerning => true;
        public override bool MathematicalGreek => false;
        public override FontNumeralAlignment NumeralAlignment => default;
        public override FontNumeralStyle NumeralStyle => default;
        public override bool SlashedZero => false;
        public override bool StandardLigatures => true;
        public override int StandardSwashes => 0;
        public override int StylisticAlternates => 0;
        public override bool StylisticSet1 => false;
        public override bool StylisticSet2 => false;
        public override bool StylisticSet3 => false;
        public override bool StylisticSet4 => false;
        public override bool StylisticSet5 => false;
        public override bool StylisticSet6 => false;
        public override bool StylisticSet7 => false;
        public override bool StylisticSet8 => false;
        public override bool StylisticSet9 => false;
        public override bool StylisticSet10 => false;
        public override bool StylisticSet11 => false;
        public override bool StylisticSet12 => false;
        public override bool StylisticSet13 => false;
        public override bool StylisticSet14 => false;
        public override bool StylisticSet15 => false;
        public override bool StylisticSet16 => false;
        public override bool StylisticSet17 => false;
        public override bool StylisticSet18 => false;
        public override bool StylisticSet19 => false;
        public override bool StylisticSet20 => false;
        public override FontVariants Variants => default;
    }
}

public sealed class MarkdownTextRunPropertyFactory
{
    private static readonly Brush BodyForeground = CreateBrush("#111827");
    private static readonly Brush MutedForeground = CreateBrush("#6B7280");
    private static readonly Brush AccentForeground = CreateBrush("#1D4ED8");
    private static readonly Brush CodeForeground = CreateBrush("#E5E7EB");
    private static readonly Brush CodeBackground = CreateBrush("#111827");

    private readonly ThemeMetrics _metrics;

    public MarkdownTextRunPropertyFactory(ThemeMetrics metrics)
    {
        _metrics = metrics;
    }

    public TextRunProperties CreateBody() =>
        new SimpleTextRunProperties(_metrics.BodyTypeface, _metrics.BodyFontSize, BodyForeground);

    public TextRunProperties CreateHeading(int level)
    {
        var scale = level switch
        {
            1 => 1.75,
            2 => 1.45,
            3 => 1.25,
            _ => 1.1,
        };

        return new SimpleTextRunProperties(
            new Typeface(_metrics.BodyTypeface.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            _metrics.BodyFontSize * scale,
            BodyForeground);
    }

    public TextRunProperties CreateCodeLine() =>
        new SimpleTextRunProperties(_metrics.CodeTypeface, _metrics.CodeFontSize, CodeForeground, CodeBackground);

    public TextRunProperties CreateCodeToolbar() =>
        new SimpleTextRunProperties(
            new Typeface(_metrics.BodyTypeface.FontFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
            12,
            MutedForeground);

    public TextRunProperties CreateLink() =>
        new SimpleTextRunProperties(
            _metrics.BodyTypeface,
            _metrics.BodyFontSize,
            AccentForeground,
            textDecorations: TextDecorations.Underline);

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}

public sealed class BasicParagraphProperties : TextParagraphProperties
{
    private static readonly TextDecorationCollection EmptyDecorations = new();

    private readonly TextRunProperties _defaultTextRunProperties;
    private readonly bool _wrap;
    private readonly double _defaultIncrementalTab;

    public BasicParagraphProperties(TextRunProperties defaultTextRunProperties, bool wrap = true, double defaultIncrementalTab = 32)
    {
        _defaultTextRunProperties = defaultTextRunProperties;
        _wrap = wrap;
        _defaultIncrementalTab = defaultIncrementalTab;
    }

    public override FlowDirection FlowDirection => FlowDirection.LeftToRight;

    public override TextAlignment TextAlignment => TextAlignment.Left;

    public override double LineHeight => double.NaN;

    public override bool FirstLineInParagraph => true;

    public override TextRunProperties DefaultTextRunProperties => _defaultTextRunProperties;

    public override TextWrapping TextWrapping => _wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public override TextMarkerProperties? TextMarkerProperties => null;

    public override double Indent => 0;

    public override double ParagraphIndent => 0;

    public override IList<TextTabProperties>? Tabs => null;

    public override double DefaultIncrementalTab => _defaultIncrementalTab;

    public override bool AlwaysCollapsible => false;

    public override TextDecorationCollection TextDecorations => EmptyDecorations;
}
