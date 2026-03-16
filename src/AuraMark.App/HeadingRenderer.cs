using System.Windows;
using System.Windows.Media;
using AuraMark.Core.Layout;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace AuraMark.App;

public sealed class HeadingRenderer
{
    private static readonly Brush RuleBrush = CreateBrush("#E5E7EB");

    public void Render(DrawingContext drawingContext, LayoutHeadingBlock block)
    {
        foreach (var line in block.Lines)
        {
            line.TextLine.Draw(
                drawingContext,
                new System.Windows.Point(line.Bounds.X, line.Bounds.Y),
                System.Windows.Media.TextFormatting.InvertAxes.None);
        }

        if (block.Level > 2 || block.Lines.Count == 0)
        {
            return;
        }

        var lastLine = block.Lines[^1];
        var y = lastLine.Bounds.Bottom + (block.Level == 1 ? 10 : 6);
        drawingContext.DrawLine(
            new System.Windows.Media.Pen(RuleBrush, block.Level == 1 ? 1.6 : 1.0),
            new System.Windows.Point(block.Bounds.X, y),
            new System.Windows.Point(block.Bounds.Right, y));
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
