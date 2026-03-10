using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record HeadingBlock(TextSpan Span, int Level, IReadOnlyList<MdInline> Inlines)
    : MdBlock(Span, MdBlockKind.Heading);
