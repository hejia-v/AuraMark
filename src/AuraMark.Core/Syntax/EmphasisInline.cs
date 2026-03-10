using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record EmphasisInline(TextSpan Span, IReadOnlyList<MdInline> Children)
    : MdInline(Span, MdInlineKind.Emphasis);
