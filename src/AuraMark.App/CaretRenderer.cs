using System.Windows;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace AuraMark.App;

public sealed class CaretRenderer
{
    private static readonly Pen CaretPen = CreatePen("#111827", 1.2);

    public void Render(DrawingContext drawingContext, Rect rect, bool visible)
    {
        if (!visible || rect.IsEmpty)
        {
            return;
        }

        drawingContext.DrawLine(
            CaretPen,
            new System.Windows.Point(rect.X + 0.5, rect.Y),
            new System.Windows.Point(rect.X + 0.5, rect.Bottom));
    }

    private static Pen CreatePen(string color, double thickness)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();

        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
