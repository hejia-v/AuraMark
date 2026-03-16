using System.Windows;
using AuraMark.Core.Layout;
using AuraMark.Core.Text;
using Point = System.Windows.Point;

namespace AuraMark.App;

public sealed class EditorHitTestService
{
    public TextPosition HitTest(LayoutDocument? document, Point point)
    {
        var line = FindNearestLine(document, point.Y);
        if (line is null)
        {
            return TextPosition.Zero;
        }

        if (point.X <= line.Bounds.X)
        {
            return new TextPosition(line.StartOffset);
        }

        if (point.X >= line.Bounds.Right)
        {
            return new TextPosition(line.StartOffset + line.Length);
        }

        var distance = point.X - line.Bounds.X;
        var hit = line.TextLine.GetCharacterHitFromDistance(distance);
        var offset = line.StartOffset + hit.FirstCharacterIndex + hit.TrailingLength;
        return new TextPosition(Math.Clamp(offset, line.StartOffset, line.StartOffset + line.Length));
    }

    private static ILayoutTextLine? FindNearestLine(LayoutDocument? document, double y)
    {
        if (document is null)
        {
            return null;
        }

        ILayoutTextLine? closest = null;
        var bestDistance = double.MaxValue;

        foreach (var line in LayoutLineEnumerator.EnumerateLines(document))
        {
            if (y >= line.Bounds.Top && y <= line.Bounds.Bottom)
            {
                return line;
            }

            var distance = y < line.Bounds.Top
                ? line.Bounds.Top - y
                : y - line.Bounds.Bottom;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                closest = line;
            }
        }

        return closest;
    }
}
