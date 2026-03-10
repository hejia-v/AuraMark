using AuraMark.Core.Layout;
using AuraMark.Core.Syntax;
using AuraMark.Core.Text;
using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Editing;

public static class EditorStateFactory
{
    public static EditorState Create(
        string text,
        IMarkdownParser parser,
        ILayoutEngine? layoutEngine = null,
        EditorOptions? options = null,
        DocumentMeta? meta = null,
        ViewportState viewport = default)
    {
        var snapshot = new DocumentSnapshot(
            SourceText.From(text ?? string.Empty),
            new DocumentVersion(1),
            SelectionRange.Collapsed(TextPosition.Zero),
            options ?? new EditorOptions(),
            meta ?? new DocumentMeta(null, null, false, null));

        var parse = parser.Parse(snapshot);
        var layout = (layoutEngine ?? new NullLayoutEngine())
            .Build(new LayoutBuildRequest(parse, viewport, new TextSpan(0, snapshot.Text.Length)), previous: null)
            .Document;

        return new EditorState(
            snapshot,
            parse,
            layout,
            viewport,
            UndoRedoState.Empty,
            new UiTransientState(),
            new EditorVisualState(
                new CaretVisualState(snapshot.Selection.Active),
                new SelectionVisualState(snapshot.Selection)));
    }
}
