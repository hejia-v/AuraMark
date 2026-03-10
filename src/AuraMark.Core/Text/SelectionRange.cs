using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Text;

public readonly record struct SelectionRange(TextPosition Anchor, TextPosition Active)
{
    public bool IsCollapsed => Anchor.Offset == Active.Offset;

    public int Start => Math.Min(Anchor.Offset, Active.Offset);

    public int End => Math.Max(Anchor.Offset, Active.Offset);

    public TextSpan AsTextSpan() => TextSpan.FromBounds(Start, End);

    public static SelectionRange Collapsed(TextPosition position) => new(position, position);
}
