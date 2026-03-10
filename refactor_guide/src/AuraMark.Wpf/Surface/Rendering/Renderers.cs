using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Microsoft.CodeAnalysis.Text;
using AuraMark.Core.Layout;
using AuraMark.Core.Text;
using AuraMark.Core.Editing;

namespace AuraMark.Wpf.Surface.Rendering;

public sealed class ParagraphRenderer
{
    public void RenderParagraph(DrawingContext dc, LayoutParagraphBlock block)
    {
        foreach (var line in block.Lines)
            line.TextLine.Draw(dc, new Point(line.Bounds.X, line.Bounds.Y), InvertAxes.None);
    }
}

public sealed class HeadingRenderer
{
    public void RenderHeading(DrawingContext dc, LayoutHeadingBlock block)
    {
        foreach (var line in block.Lines)
            line.TextLine.Draw(dc, new Point(line.Bounds.X, line.Bounds.Y), InvertAxes.None);

        if (block.Level <= 2)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(220, 226, 235)), 1);
            dc.DrawLine(pen, new Point(block.Bounds.Left, block.Bounds.Bottom + 4), new Point(block.Bounds.Right, block.Bounds.Bottom + 4));
        }
    }
}

public sealed class CodeFenceRenderer
{
    private readonly Brush _cardBackground = new SolidColorBrush(Color.FromRgb(250, 251, 253));
    private readonly Brush _toolbarBackground = new SolidColorBrush(Color.FromRgb(242, 245, 248));
    private readonly Brush _borderBrush = new SolidColorBrush(Color.FromRgb(223, 229, 235));
    private readonly Brush _gutterForeground = Brushes.Gray;
    private readonly Brush _toolbarForeground = Brushes.DimGray;
    private readonly Pen _borderPen;
    private readonly Typeface _uiTypeface = new("Segoe UI");
    private readonly double _uiFontSize = 12;
    private readonly double _cornerRadius = 10;

    public CodeFenceRenderer() { _borderPen = new Pen(_borderBrush, 1); }

    public void RenderCodeFence(DrawingContext dc, LayoutCodeFenceBlock block)
    {
        dc.DrawRoundedRectangle(_cardBackground, _borderPen, block.Bounds, _cornerRadius, _cornerRadius);
        dc.DrawRoundedRectangle(_toolbarBackground, null, block.ToolbarRect, _cornerRadius, _cornerRadius);
        string label = string.IsNullOrWhiteSpace(block.Language) ? "plain text" : block.Language!;
        var ft = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _uiTypeface, _uiFontSize, _toolbarForeground, 1.0);
        dc.DrawText(ft, new Point(block.ToolbarRect.X + 12, block.ToolbarRect.Y + 8));
        foreach (var line in block.Lines)
        {
            var num = new FormattedText(line.LineNumber.ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _uiTypeface, _uiFontSize - 1, _gutterForeground, 1.0);
            dc.DrawText(num, new Point(Math.Max(0, line.Bounds.X - num.Width - 12), line.Bounds.Y));
            line.TextLine.Draw(dc, new Point(line.Bounds.X, line.Bounds.Y), InvertAxes.None);
        }
    }
}

public interface ISelectionGeometryProvider { IReadOnlyList<Rect> GetSelectionRects(LayoutDocument layout, TextSpan selection); }
public sealed class SelectionGeometryProvider : ISelectionGeometryProvider
{
    public IReadOnlyList<Rect> GetSelectionRects(LayoutDocument layout, TextSpan selection)
    {
        var rects = new List<Rect>();
        foreach (var block in layout.Blocks)
        {
            switch (block)
            {
                case LayoutParagraphBlock p: rects.AddRange(GetRects(p.Lines.Select(l => (l.Bounds, l.StartOffset, l.Length, l.Span)), selection)); break;
                case LayoutHeadingBlock h: rects.AddRange(GetRects(h.Lines.Select(l => (l.Bounds, l.StartOffset, l.Length, l.Span)), selection)); break;
                case LayoutCodeFenceBlock c: rects.AddRange(GetRects(c.Lines.Select(l => (l.Bounds, l.StartOffset, l.Length, l.Span)), selection)); break;
            }
        }
        return rects;
    }

    private static IEnumerable<Rect> GetRects(IEnumerable<(Rect Bounds, int StartOffset, int Length, TextSpan Span)> lines, TextSpan selection)
    {
        foreach (var line in lines)
        {
            int start = Math.Max(line.Span.Start, selection.Start); int end = Math.Min(line.Span.End, selection.End); if (start >= end) continue;
            int localStart = Math.Max(0, start - line.StartOffset); int localEnd = Math.Min(line.Length, end - line.StartOffset);
            double charWidth = line.Bounds.Width / Math.Max(1, line.Length); double x = line.Bounds.X + charWidth * localStart; double width = Math.Max(1, charWidth * (localEnd - localStart));
            yield return new Rect(x, line.Bounds.Y, width, line.Bounds.Height);
        }
    }
}

public sealed class SelectionRenderer
{
    private readonly Brush _selectionBrush;
    public SelectionRenderer(Brush selectionBrush) { _selectionBrush = selectionBrush; }
    public void Render(DrawingContext dc, IReadOnlyList<Rect> rects) { foreach (var rect in rects) dc.DrawRectangle(_selectionBrush, null, rect); }
}

public interface ICaretGeometryProvider : ICaretGeometryAdapter { }
public sealed class CaretGeometryProvider : ICaretGeometryProvider
{
    public Rect GetCaretRect(LayoutDocument layout, TextPosition position)
    {
        foreach (var block in layout.Blocks)
        {
            switch (block)
            {
                case LayoutParagraphBlock p:
                    foreach (var line in p.Lines) if (position.Offset >= line.Span.Start && position.Offset <= line.Span.End) return Estimate(line.Bounds, line.StartOffset, line.Length, position.Offset);
                    break;
                case LayoutHeadingBlock h:
                    foreach (var line in h.Lines) if (position.Offset >= line.Span.Start && position.Offset <= line.Span.End) return Estimate(line.Bounds, line.StartOffset, line.Length, position.Offset);
                    break;
                case LayoutCodeFenceBlock c:
                    foreach (var line in c.Lines) if (position.Offset >= line.Span.Start && position.Offset <= line.Span.End) return Estimate(line.Bounds, line.StartOffset, line.Length, position.Offset);
                    break;
            }
        }
        return new Rect(0, 0, 1, 16);
    }
    private static Rect Estimate(Rect lineBounds, int lineStart, int lineLength, int offset) { int local = Math.Clamp(offset - lineStart, 0, lineLength); double charWidth = lineBounds.Width / Math.Max(1, lineLength); double x = lineBounds.X + charWidth * local; return new Rect(x, lineBounds.Y, 1, lineBounds.Height); }
}

public sealed class CaretRenderer
{
    private readonly Brush _caretBrush;
    public CaretRenderer(Brush caretBrush) { _caretBrush = caretBrush; }
    public void Render(DrawingContext dc, Rect caretRect, bool isVisible) { if (isVisible) dc.DrawRectangle(_caretBrush, null, caretRect); }
}
