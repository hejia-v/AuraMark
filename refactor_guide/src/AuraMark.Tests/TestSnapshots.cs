using Microsoft.CodeAnalysis.Text;
using AuraMark.Core.Text;

namespace AuraMark.Tests;

public static class TestSnapshots
{
    public static DocumentSnapshot Create(string text, int caret = 0, int? anchor = null, int? active = null)
    {
        int a = anchor ?? caret;
        int b = active ?? caret;
        return new DocumentSnapshot(SourceText.From(text), new DocumentVersion(1), new SelectionRange(new TextPosition(a), new TextPosition(b)), new EditorOptions(), new DocumentMeta(null, "Test", false, null));
    }
}
