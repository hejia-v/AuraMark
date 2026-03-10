using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record CodeFenceBlock(TextSpan Span, TextSpan InfoStringSpan, TextSpan ContentSpan, string? Language)
    : MdBlock(Span, MdBlockKind.CodeFence);
