using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public sealed record TableBlock(TextSpan Span, IReadOnlyList<TableRow> Rows)
    : MdBlock(Span, MdBlockKind.Table);
