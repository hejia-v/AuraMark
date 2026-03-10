using AuraMark.Core.Text;
using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Syntax;

public interface IIncrementalMarkdownParser
{
    ParseResult Parse(DocumentSnapshot snapshot, ParseResult? previous, TextSpan dirtySpan);

    ReparseWindow ExpandToSafeWindow(SourceText text, ParseResult? previous, TextSpan dirtySpan);
}
