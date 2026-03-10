using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Text;

public sealed record DocumentSnapshot(
    SourceText Text,
    DocumentVersion Version,
    SelectionRange Selection,
    EditorOptions Options,
    DocumentMeta Meta);
