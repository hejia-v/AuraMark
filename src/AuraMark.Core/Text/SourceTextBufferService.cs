using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Text;

public sealed class SourceTextBufferService : ITextBufferService
{
    public TextApplyResult Apply(DocumentSnapshot snapshot, IReadOnlyList<TextEdit> edits)
    {
        if (edits.Count == 0)
        {
            return new TextApplyResult(snapshot, Array.Empty<TextChange>(), default);
        }

        var ordered = edits.OrderByDescending(edit => edit.Span.Start).ToArray();
        var text = snapshot.Text;
        TextSpan? dirtySpan = null;

        foreach (var edit in ordered)
        {
            text = text.Replace(edit.Span, edit.NewText);
            dirtySpan = dirtySpan is null
                ? TextSpan.FromBounds(edit.Span.Start, edit.Span.Start + edit.NewText.Length)
                : TextSpan.FromBounds(
                    Math.Min(dirtySpan.Value.Start, edit.Span.Start),
                    Math.Max(dirtySpan.Value.End, edit.Span.Start + edit.NewText.Length));
        }

        var changes = text.GetTextChanges(snapshot.Text).ToArray();
        var nextSnapshot = snapshot with
        {
            Text = text,
            Version = snapshot.Version.Next(),
            Meta = snapshot.Meta with { IsDirty = true },
        };

        return new TextApplyResult(nextSnapshot, changes, dirtySpan ?? default);
    }
}
