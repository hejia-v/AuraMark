namespace AuraMark.Core.Text;

public sealed record DocumentVersion(long Value)
{
    public DocumentVersion Next() => new(Value + 1);
}
