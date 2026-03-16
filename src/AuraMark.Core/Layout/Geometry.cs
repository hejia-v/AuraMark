using System.Windows;
using System.Windows.Media.TextFormatting;
using AuraMark.Core.Text;

namespace AuraMark.Core.Layout;

public sealed class SelectionGeometryProvider
{
    public IReadOnlyList<Rect> GetSelectionRects(LayoutDocument? document, SelectionRange selection)
    {
        if (document is null || selection.IsCollapsed)
        {
            return Array.Empty<Rect>();
        }

        var selectionSpan = selection.AsTextSpan();
        var rects = new List<Rect>();

        foreach (var line in LayoutLineEnumerator.EnumerateLines(document))
        {
            var lineStart = line.StartOffset;
            var lineEnd = line.StartOffset + line.Length;
            var start = Math.Max(selectionSpan.Start, lineStart);
            var end = Math.Min(selectionSpan.End, lineEnd);
            if (end <= start)
            {
                continue;
            }

            var startX = GetDistance(line.TextLine, start - lineStart);
            var endX = GetDistance(line.TextLine, end - lineStart);
            rects.Add(new Rect(line.Bounds.X + startX, line.Bounds.Y, Math.Max(1, endX - startX), line.Bounds.Height));
        }

        return rects;
    }

    private static double GetDistance(TextLine textLine, int offset)
    {
        try
        {
            return textLine.GetDistanceFromCharacterHit(new CharacterHit(Math.Max(0, offset), 0));
        }
        catch (ArgumentOutOfRangeException)
        {
            return Math.Max(0, textLine.WidthIncludingTrailingWhitespace);
        }
    }
}

public sealed class CaretGeometryProvider
{
    public Rect GetCaretRect(LayoutDocument? document, TextPosition position, double fallbackHeight = 18)
    {
        if (document is null)
        {
            return new Rect(0, 0, 1, fallbackHeight);
        }

        foreach (var line in LayoutLineEnumerator.EnumerateLines(document))
        {
            var lineStart = line.StartOffset;
            var lineEnd = line.StartOffset + line.Length;
            if (position.Offset < lineStart || position.Offset > lineEnd)
            {
                continue;
            }

            var x = line.Bounds.X + GetDistance(line.TextLine, position.Offset - lineStart);
            return new Rect(x, line.Bounds.Y, 1, line.Bounds.Height);
        }

        var lastLine = LayoutLineEnumerator.EnumerateLines(document).LastOrDefault();
        return lastLine is null
            ? new Rect(0, 0, 1, fallbackHeight)
            : new Rect(lastLine.Bounds.Right, lastLine.Bounds.Y, 1, lastLine.Bounds.Height);
    }

    private static double GetDistance(TextLine textLine, int offset)
    {
        try
        {
            return textLine.GetDistanceFromCharacterHit(new CharacterHit(Math.Max(0, offset), 0));
        }
        catch (ArgumentOutOfRangeException)
        {
            return Math.Max(0, textLine.WidthIncludingTrailingWhitespace);
        }
    }
}
