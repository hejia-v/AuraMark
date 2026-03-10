using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public readonly record struct ReparseWindow(TextSpan RequestedDirtySpan, TextSpan ExpandedSpan);
