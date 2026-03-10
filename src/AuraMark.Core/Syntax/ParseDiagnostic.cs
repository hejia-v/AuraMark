using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record ParseDiagnostic(string Code, string Message, TextSpan Span);
