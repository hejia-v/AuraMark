using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Editing;

public abstract record EditorInvalidation;

public sealed record SyntaxInvalidation(TextSpan Span) : EditorInvalidation;

public sealed record LayoutInvalidation(TextSpan Span) : EditorInvalidation;

public sealed record VisualInvalidation(TextSpan Span) : EditorInvalidation;
