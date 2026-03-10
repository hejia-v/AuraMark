using AuraMark.Core.Text;

namespace AuraMark.Core.Editing;

public sealed record EditorTransaction(string Name, DocumentSnapshot Before, DocumentSnapshot After);
