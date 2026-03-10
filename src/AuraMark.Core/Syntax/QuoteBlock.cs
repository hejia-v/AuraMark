using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record QuoteBlock(TextSpan Span, IReadOnlyList<MdBlock> Children)
    : MdBlock(Span, MdBlockKind.Quote);
