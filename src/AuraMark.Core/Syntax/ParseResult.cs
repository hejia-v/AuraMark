using AuraMark.Core.Text;

namespace AuraMark.Core.Syntax;

public sealed record ParseResult(
    DocumentSnapshot Snapshot,
    IReadOnlyList<MdBlock> Blocks,
    IReadOnlyList<OutlineItem> Outline,
    IReadOnlyList<ParseDiagnostic> Diagnostics,
    IReadOnlyList<BlockMapEntry> BlockMap);
