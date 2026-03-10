using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public readonly record struct BlockMapEntry(TextSpan Span, int BlockIndex, MdBlockKind Kind);
