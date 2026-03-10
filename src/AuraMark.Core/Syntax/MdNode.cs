using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public abstract record MdNode(TextSpan Span);

public abstract record MdBlock(TextSpan Span, MdBlockKind Kind) : MdNode(Span);

public abstract record MdInline(TextSpan Span, MdInlineKind Kind) : MdNode(Span);
