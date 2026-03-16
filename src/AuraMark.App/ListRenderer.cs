using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AuraMark.Core.Layout;
using Brush = System.Windows.Media.Brush;
using FlowDirection = System.Windows.FlowDirection;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace AuraMark.App;

public sealed class ListRenderer
{
    private static readonly Brush MarkerBrush = CreateBrush("#334155");
    private static readonly Typeface MarkerTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public void Render(DrawingContext drawingContext, LayoutListBlock block)
    {
        foreach (var item in block.Items)
        {
            var marker = CreateMarker(item.MarkerText);
            var point = new Point(
                item.MarkerBounds.Right - marker.Width,
                item.MarkerBounds.Y + Math.Max(0, (item.MarkerBounds.Height - marker.Height) / 2));
            drawingContext.DrawText(marker, point);
        }
    }

    private static FormattedText CreateMarker(string text)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            MarkerTypeface,
            14,
            MarkerBrush,
            1.0);
        formatted.SetFontWeight(FontWeights.SemiBold);
        return formatted;
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
