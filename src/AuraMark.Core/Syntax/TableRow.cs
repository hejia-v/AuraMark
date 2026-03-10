using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record TableRow(IReadOnlyList<TextSpan> Cells);
