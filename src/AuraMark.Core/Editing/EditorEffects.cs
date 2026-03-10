using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Editing;

public abstract record EditorEffect;

public sealed record ScrollIntoViewEffect(TextSpan Span) : EditorEffect;

public sealed record RequestRenderEffect() : EditorEffect;
