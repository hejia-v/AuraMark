using Microsoft.CodeAnalysis.Text;
using AuraMark.Core.Text;

namespace AuraMark.Core.Syntax;

public sealed record MarkdownHeadingInfo(string Text, int Level, int BodyOffset, int SourceOffset);

public sealed record MarkdownOutlineDocument(
    int BodyStartOffset,
    IReadOnlyList<MarkdownHeadingInfo> Headings)
{
    public static MarkdownOutlineDocument Empty { get; } =
        new(0, Array.Empty<MarkdownHeadingInfo>());
}

public sealed class MarkdownOutlineService
{
    private readonly MarkdownParser _parser = new();

    public MarkdownOutlineDocument ParseDocument(string? markdown)
    {
        var document = FrontMatterParser.Parse(markdown);
        var bodyStartOffset = document.FrontMatterRaw.Length;
        var snapshot = new DocumentSnapshot(
            SourceText.From(document.BodyMarkdown),
            new DocumentVersion(1),
            SelectionRange.Collapsed(TextPosition.Zero),
            new EditorOptions(),
            new DocumentMeta(null, null, false, null));

        var parseResult = _parser.Parse(snapshot);
        var headings = parseResult.Outline
            .Select(item => new MarkdownHeadingInfo(
                item.Text,
                item.Level,
                item.Span.Start,
                bodyStartOffset + item.Span.Start))
            .ToArray();

        return new MarkdownOutlineDocument(bodyStartOffset, headings);
    }
}
