using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record ParagraphBlock(TextSpan Span, IReadOnlyList<MdInline> Inlines)
    : MdBlock(Span, MdBlockKind.Paragraph);
