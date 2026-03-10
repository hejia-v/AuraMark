using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Text;

public readonly record struct TextPosition(int Offset)
{
    public static TextPosition Zero => new(0);
}

public readonly record struct SelectionRange(TextPosition Anchor, TextPosition Active)
{
    public bool IsCollapsed => Anchor.Offset == Active.Offset;
    public int Start => Math.Min(Anchor.Offset, Active.Offset);
    public int End => Math.Max(Anchor.Offset, Active.Offset);

    public TextSpan AsTextSpan() => TextSpan.FromBounds(Start, End);

    public static SelectionRange Collapsed(TextPosition position) => new(position, position);
}

public readonly record struct ViewportState(double Width, double Height, double VerticalOffset);

public sealed record EditorOptions(int TabSize = 4, bool UseSoftWrap = true, bool ShowParagraphSpacing = true, double PageWidth = 860, string ThemeName = "Aura");
public sealed record DocumentMeta(string? FilePath, string? Title, bool IsDirty, DateTimeOffset? LastSavedAt);

public sealed record DocumentVersion(long Value)
{
    public DocumentVersion Next() => new(Value + 1);
}

public sealed record DocumentSnapshot(SourceText Text, DocumentVersion Version, SelectionRange Selection, EditorOptions Options, DocumentMeta Meta);
public readonly record struct TextEdit(TextSpan Span, string NewText);
public sealed record TextApplyResult(DocumentSnapshot Snapshot, IReadOnlyList<TextChange> Changes, TextSpan DirtySpan);

public interface ITextBufferService
{
    TextApplyResult Apply(DocumentSnapshot snapshot, IReadOnlyList<TextEdit> edits);
}

public sealed class SourceTextBufferService : ITextBufferService
{
    public TextApplyResult Apply(DocumentSnapshot snapshot, IReadOnlyList<TextEdit> edits)
    {
        if (edits.Count == 0)
            return new TextApplyResult(snapshot, Array.Empty<TextChange>(), default);

        var ordered = edits.OrderByDescending(e => e.Span.Start).ToArray();
        var text = snapshot.Text;
        TextSpan? dirty = null;

        foreach (var edit in ordered)
        {
            text = text.Replace(edit.Span, edit.NewText);
            dirty = dirty is null
                ? TextSpan.FromBounds(edit.Span.Start, edit.Span.Start + edit.NewText.Length)
                : TextSpan.FromBounds(Math.Min(dirty.Value.Start, edit.Span.Start), Math.Max(dirty.Value.End, edit.Span.Start + edit.NewText.Length));
        }

        var changes = text.GetTextChanges(snapshot.Text).ToArray();
        var next = snapshot with
        {
            Text = text,
            Version = snapshot.Version.Next(),
            Meta = snapshot.Meta with { IsDirty = true }
        };

        return new TextApplyResult(next, changes, dirty ?? default);
    }
}
