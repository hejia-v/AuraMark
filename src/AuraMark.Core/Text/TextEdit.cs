using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Text;

public readonly record struct TextEdit(TextSpan Span, string NewText);
