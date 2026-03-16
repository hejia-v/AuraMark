using System.Windows.Media;
using AuraMark.Core.Layout;
using Brush = System.Windows.Media.Brush;

namespace AuraMark.App;

public sealed class QuoteRenderer
{
    private static readonly Brush StripeBrush = CreateBrush("#CBD5E1");
    private static readonly Brush BackgroundBrush = CreateBrush("#F8FAFC");

    public void Render(DrawingContext drawingContext, LayoutQuoteBlock block)
    {
        drawingContext.DrawRoundedRectangle(BackgroundBrush, null, block.Bounds, 10, 10);
        drawingContext.DrawRoundedRectangle(StripeBrush, null, block.StripeBounds, 2, 2);
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
