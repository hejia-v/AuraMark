using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record ListBlock(TextSpan Span, bool Ordered, IReadOnlyList<ListItemBlock> Items)
    : MdBlock(Span, Ordered ? MdBlockKind.OrderedList : MdBlockKind.BulletList);
