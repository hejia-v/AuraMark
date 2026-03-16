using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace AuraMark.App;

public sealed class SelectionRenderer
{
    private static readonly Brush SelectionBrush = CreateBrush("#66499AF5");

    public void Render(DrawingContext drawingContext, IReadOnlyList<Rect> rects)
    {
        foreach (var rect in rects)
        {
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            drawingContext.DrawRoundedRectangle(SelectionBrush, null, rect, 3, 3);
        }
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
