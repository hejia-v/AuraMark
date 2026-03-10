using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record OutlineItem(string Text, int Level, TextSpan Span);
