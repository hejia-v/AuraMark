using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record StrongInline(TextSpan Span, IReadOnlyList<MdInline> Children)
    : MdInline(Span, MdInlineKind.Strong);
