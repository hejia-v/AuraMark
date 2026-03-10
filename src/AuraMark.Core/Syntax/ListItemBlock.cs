using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record ListItemBlock(TextSpan Span, IReadOnlyList<MdBlock> Children)
    : MdBlock(Span, MdBlockKind.ListItem);
