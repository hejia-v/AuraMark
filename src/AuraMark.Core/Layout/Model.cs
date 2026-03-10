using AuraMark.Core.Syntax;
using AuraMark.Core.Text;
using Microsoft.CodeAnalysis.Text;

namespace AuraMark.Core.Layout;

public abstract record LayoutBlock(TextSpan Span);

public sealed record LayoutDocument(
    long Version,
    IReadOnlyList<LayoutBlock> Blocks,
    double TotalHeight)
{
    public void DisposeLines()
    {
    }
}

public sealed record LayoutBuildRequest(ParseResult Parse, ViewportState Viewport, TextSpan DirtySpan);

public sealed record LayoutBuildResult(LayoutDocument Document);

public interface ILayoutEngine
{
    LayoutBuildResult Build(LayoutBuildRequest request, LayoutDocument? previous);
}

public sealed class NullLayoutEngine : ILayoutEngine
{
    public LayoutBuildResult Build(LayoutBuildRequest request, LayoutDocument? previous)
    {
        return new LayoutBuildResult(
            new LayoutDocument(
                request.Parse.Snapshot.Version.Value,
                Array.Empty<LayoutBlock>(),
                0));
    }
}
