using System.Windows;
using System.Windows.Media;
using AuraMark.Core.Layout;
using Point = System.Windows.Point;

namespace AuraMark.App;

public sealed class ParagraphRenderer
{
    public void Render(DrawingContext drawingContext, LayoutParagraphBlock block)
    {
        foreach (var line in block.Lines)
        {
            line.TextLine.Draw(
                drawingContext,
                new System.Windows.Point(line.Bounds.X, line.Bounds.Y),
                System.Windows.Media.TextFormatting.InvertAxes.None);
        }
    }
}
