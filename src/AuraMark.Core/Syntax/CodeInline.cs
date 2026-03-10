using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record CodeInline(TextSpan Span) : MdInline(Span, MdInlineKind.Code);
