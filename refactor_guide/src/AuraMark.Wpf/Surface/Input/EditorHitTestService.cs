using System.Windows;
using AuraMark.Core.Layout;
using AuraMark.Core.Text;
using AuraMark.Core.Editing;

namespace AuraMark.Wpf.Surface.Input;

public interface IEditorHitTestService : IHitTestAdapter { }

public sealed class EditorHitTestService : IEditorHitTestService
{
    public TextPosition HitTest(LayoutDocument layout, Point point)
    {
        foreach (var block in layout.Blocks)
        {
            if (!block.Bounds.Contains(point)) continue;
            return block switch
            {
                LayoutParagraphBlock p => HitParagraph(p, point),
                LayoutHeadingBlock h => HitHeading(h, point),
                LayoutCodeFenceBlock c => HitCodeFence(c, point),
                _ => new TextPosition(block.Span.Start)
            };
        }
        if (layout.Blocks.Count == 0) return TextPosition.Zero;
        return point.Y < layout.Blocks[0].Bounds.Top ? new TextPosition(layout.Blocks[0].Span.Start) : new TextPosition(layout.Blocks[^1].Span.End);
    }

    private static TextPosition HitParagraph(LayoutParagraphBlock block, Point point)
    {
        var line = FindNearest(block.Lines, point.Y); return HitLine(line.Bounds, line.StartOffset, line.Length, point.X);
    }
    private static TextPosition HitHeading(LayoutHeadingBlock block, Point point)
    {
        var line = FindNearest(block.Lines, point.Y); return HitLine(line.Bounds, line.StartOffset, line.Length, point.X);
    }
    private static TextPosition HitCodeFence(LayoutCodeFenceBlock block, Point point)
    {
        var line = FindNearest(block.Lines, point.Y); return HitLine(line.Bounds, line.StartOffset, line.Length, point.X);
    }
    private static T FindNearest<T>(IReadOnlyList<T> lines, double y) where T : class
    {
        dynamic first = lines[0]; foreach (dynamic line in lines) if (y >= line.Bounds.Top && y < line.Bounds.Bottom) return line; return y < first.Bounds.Top ? lines[0] : lines[^1];
    }
    private static TextPosition HitLine(Rect bounds, int lineStart, int lineLength, double x)
    {
        if (lineLength <= 0) return new TextPosition(lineStart); double relativeX = x - bounds.X; if (relativeX <= 0) return new TextPosition(lineStart); double avg = bounds.Width / Math.Max(1, lineLength); int delta = (int)Math.Round(relativeX / Math.Max(1, avg)); delta = Math.Clamp(delta, 0, lineLength); return new TextPosition(lineStart + delta);
    }
}
