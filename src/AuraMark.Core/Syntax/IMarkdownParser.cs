using AuraMark.Core.Text;

namespace AuraMark.Core.Syntax;

public interface IMarkdownParser
{
    ParseResult Parse(DocumentSnapshot snapshot);
}
