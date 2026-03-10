namespace AuraMark.Core.Text;

public interface ITextBufferService
{
    TextApplyResult Apply(DocumentSnapshot snapshot, IReadOnlyList<TextEdit> edits);
}
