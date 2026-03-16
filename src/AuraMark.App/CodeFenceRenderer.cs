using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AuraMark.Core.Layout;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using FlowDirection = System.Windows.FlowDirection;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace AuraMark.App;

public sealed class CodeFenceRenderer
{
    private static readonly Brush CardBackground = CreateBrush("#0F172A");
    private static readonly Brush ToolbarBackground = CreateBrush("#111827");
    private static readonly Brush BorderBrush = CreateBrush("#1F2937");
    private static readonly Brush ToolbarTextBrush = CreateBrush("#94A3B8");
    private static readonly Brush LineNumberBrush = CreateBrush("#64748B");
    private static readonly Typeface UiTypeface = new(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private static readonly Pen BorderPen = CreatePen(BorderBrush, 1);

    public void Render(DrawingContext drawingContext, LayoutCodeFenceBlock block)
    {
        drawingContext.DrawRoundedRectangle(CardBackground, BorderPen, block.Bounds, 14, 14);
        drawingContext.DrawRoundedRectangle(ToolbarBackground, null, block.ToolbarBounds, 14, 14);

        var label = string.IsNullOrWhiteSpace(block.Language)
            ? "code"
            : block.Language!.Trim();
        var labelText = CreateText(label, 12, ToolbarTextBrush);
        drawingContext.DrawText(labelText, new System.Windows.Point(block.ToolbarBounds.X + 14, block.ToolbarBounds.Y + 8));

        foreach (var line in block.Lines)
        {
            var lineNumber = CreateText((line.LineIndex + 1).ToString(CultureInfo.InvariantCulture), 12, LineNumberBrush);
            drawingContext.DrawText(
                lineNumber,
                new System.Windows.Point(line.GutterBounds.Right - lineNumber.Width, line.GutterBounds.Y + Math.Max(0, (line.GutterBounds.Height - lineNumber.Height) / 2)));

            line.TextLine.Draw(
                drawingContext,
                new System.Windows.Point(line.Bounds.X, line.Bounds.Y),
                System.Windows.Media.TextFormatting.InvertAxes.None);
        }
    }

    private static FormattedText CreateText(string text, double fontSize, Brush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            UiTypeface,
            fontSize,
            brush,
            1.0);
        formatted.SetFontWeight(FontWeights.Medium);
        return formatted;
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
