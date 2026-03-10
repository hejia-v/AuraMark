using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record TextInline(TextSpan Span) : MdInline(Span, MdInlineKind.Text);
