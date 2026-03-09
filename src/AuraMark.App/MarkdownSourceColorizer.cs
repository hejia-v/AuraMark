using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace AuraMark.App;

internal sealed partial class MarkdownSourceColorizer : DocumentColorizingTransformer
{
    private static readonly SolidColorBrush HeadingBrush = CreateBrush("#255E95");
    private static readonly SolidColorBrush QuoteBrush = CreateBrush("#8A5A13");
    private static readonly SolidColorBrush MarkerBrush = CreateBrush("#A36A18");
    private static readonly SolidColorBrush EmphasisBrush = CreateBrush("#6A4F9F");
    private static readonly SolidColorBrush CodeBrush = CreateBrush("#7B4E22");
    private static readonly SolidColorBrush LinkBrush = CreateBrush("#2F7D32");
    private static readonly SolidColorBrush HtmlBrush = CreateBrush("#0F5C8A");
    private static readonly SolidColorBrush FrontMatterBrush = CreateBrush("#2C7A7B");
    private static readonly SolidColorBrush MutedBrush = CreateBrush("#708090");

    [GeneratedRegex(@"^\s{0,3}(#{1,6})\s+")]
    private static partial Regex HeadingMarkerRegex();

    [GeneratedRegex(@"^\s*>\s?")]
    private static partial Regex QuoteMarkerRegex();

    [GeneratedRegex(@"^\s*(?:[-+*]|\d+[.)])\s+(?:\[(?: |x|X)\]\s+)?")]
    private static partial Regex ListMarkerRegex();

    [GeneratedRegex(@"^\s{0,3}(?:[-*_])(?:\s*[-*_]){2,}\s*$")]
    private static partial Regex HorizontalRuleRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]+\)|\[[^\]]+\]\([^)]+\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"`[^`\r\n]+`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"(\*\*|__)(?=\S)(.+?[*_]*)(?<=\S)\1")]
    private static partial Regex StrongRegex();

    [GeneratedRegex(@"(?<!\*)\*(?=\S)(.+?)(?<=\S)\*(?!\*)|(?<!_)_(?=\S)(.+?)(?<=\S)_(?!_)")]
    private static partial Regex EmphasisRegex();

    [GeneratedRegex(@"</?[A-Za-z][^>\r\n]*>")]
    private static partial Regex HtmlRegex();

    [GeneratedRegex(@"^---\s*$|^\.\.\.\s*$")]
    private static partial Regex FrontMatterDelimiterRegex();

    [GeneratedRegex(@"^([A-Za-z0-9_-]+)(\s*:\s*)")]
    private static partial Regex FrontMatterKeyRegex();

    [GeneratedRegex(@"^\s*```")]
    private static partial Regex FenceDelimiterRegex();

    protected override void ColorizeLine(DocumentLine line)
    {
        var document = CurrentContext.Document;
        var lineText = document.GetText(line);
        var lineOffset = line.Offset;

        if (IsInsideFrontMatter(document, line))
        {
            ColorFrontMatterLine(lineText, lineOffset);
            return;
        }

        if (IsInsideFenceBlock(document, line.LineNumber))
        {
            ApplyBrush(lineOffset, line.EndOffset, CodeBrush);
            if (FenceDelimiterRegex().IsMatch(lineText))
            {
                ApplyBrush(lineOffset, line.EndOffset, MarkerBrush);
            }
            return;
        }

        if (HorizontalRuleRegex().IsMatch(lineText))
        {
            ApplyBrush(lineOffset, line.EndOffset, MarkerBrush);
            return;
        }

        ApplyPattern(HeadingMarkerRegex(), lineText, lineOffset, HeadingBrush);
        ApplyPattern(QuoteMarkerRegex(), lineText, lineOffset, QuoteBrush);
        ApplyPattern(ListMarkerRegex(), lineText, lineOffset, MarkerBrush);
        ApplyPattern(LinkRegex(), lineText, lineOffset, LinkBrush);
        ApplyPattern(InlineCodeRegex(), lineText, lineOffset, CodeBrush);
        ApplyPattern(StrongRegex(), lineText, lineOffset, HeadingBrush);
        ApplyPattern(EmphasisRegex(), lineText, lineOffset, EmphasisBrush);
        ApplyPattern(HtmlRegex(), lineText, lineOffset, HtmlBrush);

        if (HeadingMarkerRegex().IsMatch(lineText))
        {
            var headingStart = lineText.TakeWhile(char.IsWhiteSpace).Count();
            ApplyBrush(lineOffset + headingStart, line.EndOffset, HeadingBrush);
        }
    }

    private void ColorFrontMatterLine(string lineText, int lineOffset)
    {
        if (FrontMatterDelimiterRegex().IsMatch(lineText))
        {
            ApplyBrush(lineOffset, lineOffset + lineText.Length, MarkerBrush);
            return;
        }

        ApplyBrush(lineOffset, lineOffset + lineText.Length, FrontMatterBrush);
        var keyMatch = FrontMatterKeyRegex().Match(lineText);
        if (keyMatch.Success)
        {
            ApplyBrush(lineOffset, lineOffset + keyMatch.Groups[1].Length, HeadingBrush);
            ApplyBrush(
                lineOffset + keyMatch.Groups[1].Length,
                lineOffset + keyMatch.Groups[1].Length + keyMatch.Groups[2].Length,
                MutedBrush);
        }
    }

    private bool IsInsideFrontMatter(TextDocument document, DocumentLine line)
    {
        if (document.LineCount < 1)
        {
            return false;
        }

        var firstLine = document.GetText(document.GetLineByNumber(1));
        if (!FrontMatterDelimiterRegex().IsMatch(firstLine))
        {
            return false;
        }

        for (var lineNumber = 2; lineNumber <= document.LineCount; lineNumber++)
        {
            var candidate = document.GetLineByNumber(lineNumber);
            if (candidate.LineNumber < line.LineNumber &&
                FrontMatterDelimiterRegex().IsMatch(document.GetText(candidate)))
            {
                return false;
            }

            if (candidate.LineNumber >= line.LineNumber)
            {
                return true;
            }
        }

        return true;
    }

    private bool IsInsideFenceBlock(TextDocument document, int lineNumber)
    {
        var inside = false;
        for (var index = 1; index <= lineNumber; index++)
        {
            var lineText = document.GetText(document.GetLineByNumber(index));
            if (!FenceDelimiterRegex().IsMatch(lineText))
            {
                continue;
            }

            inside = !inside;
        }

        return inside;
    }

    private void ApplyPattern(Regex regex, string lineText, int lineOffset, System.Windows.Media.Brush brush)
    {
        foreach (Match match in regex.Matches(lineText))
        {
            if (!match.Success || match.Length == 0)
            {
                continue;
            }

            ApplyBrush(lineOffset + match.Index, lineOffset + match.Index + match.Length, brush);
        }
    }

    private void ApplyBrush(int startOffset, int endOffset, System.Windows.Media.Brush brush)
    {
        if (startOffset >= endOffset)
        {
            return;
        }

        ChangeLinePart(startOffset, endOffset, element => element.TextRunProperties.SetForegroundBrush(brush));
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
