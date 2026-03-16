using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record LinkInline(TextSpan Span, TextSpan LabelSpan, string Destination, IReadOnlyList<MdInline> Children)
    : MdInline(Span, MdInlineKind.Link);
