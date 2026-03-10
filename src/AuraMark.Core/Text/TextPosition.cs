namespace AuraMark.Core.Text;

public readonly record struct TextPosition(int Offset)
{
    public static TextPosition Zero => new(0);
}
