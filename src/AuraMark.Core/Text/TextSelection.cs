namespace AuraMark.Core.Text;

public readonly record struct TextSelection(int Anchor, int Active)
{
    public int Start => Math.Min(Anchor, Active);

    public int End => Math.Max(Anchor, Active);

    public int Length => End - Start;

    public bool IsCollapsed => Anchor == Active;

    public static TextSelection Collapsed(int offset) => new(offset, offset);

    public static TextSelection FromStartAndLength(int start, int length) =>
        new(start, start + Math.Max(0, length));
}
