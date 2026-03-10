using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Text;

public sealed record TextApplyResult(
    DocumentSnapshot Snapshot,
    IReadOnlyList<TextChange> Changes,
    TextSpan DirtySpan);
