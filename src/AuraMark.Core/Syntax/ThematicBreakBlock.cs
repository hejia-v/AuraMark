using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record ThematicBreakBlock(TextSpan Span)
    : MdBlock(Span, MdBlockKind.ThematicBreak);
