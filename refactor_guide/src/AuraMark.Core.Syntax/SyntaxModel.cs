using Microsoft.CodeAnalysis.Text;
using AuraMark.Core.Text;

namespace AuraMark.Core.Syntax;

public enum MdBlockKind { Paragraph, Heading, Quote, BulletList, OrderedList, ListItem, CodeFence, Table, ThematicBreak, Metadata }
public enum MdInlineKind { Text, Emphasis, Strong, Code, Link }

public abstract record MdNode(TextSpan Span);
public abstract record MdBlock(TextSpan Span, MdBlockKind Kind) : MdNode(Span);
public abstract record MdInline(TextSpan Span, MdInlineKind Kind) : MdNode(Span);

public sealed record ParagraphBlock(TextSpan Span, IReadOnlyList<MdInline> Inlines) : MdBlock(Span, MdBlockKind.Paragraph);
public sealed record HeadingBlock(TextSpan Span, int Level, IReadOnlyList<MdInline> Inlines) : MdBlock(Span, MdBlockKind.Heading);
public sealed record QuoteBlock(TextSpan Span, IReadOnlyList<MdBlock> Children) : MdBlock(Span, MdBlockKind.Quote);
public sealed record ListBlock(TextSpan Span, bool Ordered, IReadOnlyList<ListItemBlock> Items) : MdBlock(Span, Ordered ? MdBlockKind.OrderedList : MdBlockKind.BulletList);
public sealed record ListItemBlock(TextSpan Span, IReadOnlyList<MdBlock> Children) : MdBlock(Span, MdBlockKind.ListItem);
public sealed record CodeFenceBlock(TextSpan Span, TextSpan InfoStringSpan, TextSpan ContentSpan, string? Language) : MdBlock(Span, MdBlockKind.CodeFence);
public sealed record TableBlock(TextSpan Span, IReadOnlyList<TableRow> Rows) : MdBlock(Span, MdBlockKind.Table);
public sealed record ThematicBreakBlock(TextSpan Span) : MdBlock(Span, MdBlockKind.ThematicBreak);
public sealed record TableRow(IReadOnlyList<TextSpan> Cells);

public sealed record TextInline(TextSpan Span) : MdInline(Span, MdInlineKind.Text);
public sealed record EmphasisInline(TextSpan Span, IReadOnlyList<MdInline> Children) : MdInline(Span, MdInlineKind.Emphasis);
public sealed record StrongInline(TextSpan Span, IReadOnlyList<MdInline> Children) : MdInline(Span, MdInlineKind.Strong);
public sealed record CodeInline(TextSpan Span) : MdInline(Span, MdInlineKind.Code);
public sealed record LinkInline(TextSpan Span, TextSpan LabelSpan, string Destination) : MdInline(Span, MdInlineKind.Link);

public sealed record OutlineItem(string Text, int Level, TextSpan Span);
public sealed record ParseDiagnostic(string Code, string Message, TextSpan Span);
public readonly record struct BlockMapEntry(TextSpan Span, int BlockIndex, MdBlockKind Kind);
public readonly record struct ReparseWindow(TextSpan RequestedDirtySpan, TextSpan ExpandedSpan);

public sealed record ParseResult(DocumentSnapshot Snapshot, IReadOnlyList<MdBlock> Blocks, IReadOnlyList<OutlineItem> Outline, IReadOnlyList<ParseDiagnostic> Diagnostics, IReadOnlyList<BlockMapEntry> BlockMap);

public interface IMarkdownParser { ParseResult Parse(DocumentSnapshot snapshot); }
public interface IIncrementalMarkdownParser { ParseResult Parse(DocumentSnapshot snapshot, ParseResult? previous, TextSpan dirtySpan); ReparseWindow ExpandToSafeWindow(SourceText text, ParseResult? previous, TextSpan dirtySpan); }
