namespace AuraMark.App;

internal static class EditorActionCatalog
{
    public static Dictionary<string, string> CreateShortcuts()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["paragraph.paragraph"] = "Ctrl+0",
            ["paragraph.heading.1"] = "Ctrl+1",
            ["paragraph.heading.2"] = "Ctrl+2",
            ["paragraph.heading.3"] = "Ctrl+3",
            ["paragraph.heading.4"] = "Ctrl+4",
            ["paragraph.heading.5"] = "Ctrl+5",
            ["paragraph.heading.6"] = "Ctrl+6",
            ["paragraph.heading.increase"] = "Ctrl+Alt+]",
            ["paragraph.heading.decrease"] = "Ctrl+Alt+[",
            ["paragraph.quote"] = "Ctrl+Alt+Q",
            ["paragraph.ordered-list"] = "Ctrl+Alt+7",
            ["paragraph.unordered-list"] = "Ctrl+Alt+8",
            ["paragraph.task-list"] = "Ctrl+Alt+9",
            ["paragraph.code-fence"] = "Ctrl+Shift+K",
            ["paragraph.math-block"] = "Ctrl+Alt+M",
            ["paragraph.table"] = "Ctrl+Alt+T",
            ["paragraph.footnote"] = "Ctrl+Alt+F",
            ["paragraph.horizontal-rule"] = "Ctrl+Alt+H",
            ["format.bold"] = "Ctrl+B",
            ["format.italic"] = "Ctrl+I",
            ["format.underline"] = "Ctrl+U",
            ["format.strikethrough"] = "Ctrl+Alt+S",
            ["format.inline-code"] = "Ctrl+Shift+`",
            ["format.inline-math"] = "Ctrl+Alt+K",
            ["format.link"] = "Ctrl+K",
            ["format.image"] = "Ctrl+Shift+I",
            ["format.highlight"] = "Ctrl+Shift+H",
            ["format.superscript"] = "Ctrl+.",
            ["format.subscript"] = "Ctrl+,",
            ["format.clear"] = "Ctrl+\\",
        };
    }

    public static IReadOnlyList<EditorActionMenuDefinition> CreateParagraphMenuDefinitions()
    {
        return
        [
            new() { HeaderKey = "ParagraphParagraph", ActionId = "paragraph.paragraph", Shortcut = "Ctrl+0", IsCheckable = true },
            new()
            {
                HeaderKey = "ParagraphHeading",
                Children =
                [
                    new() { HeaderKey = "ParagraphHeading1", ActionId = "paragraph.heading", StateId = "paragraph.heading.1", Args = new Dictionary<string, object?> { ["level"] = 1 }, Shortcut = "Ctrl+1", IsCheckable = true },
                    new() { HeaderKey = "ParagraphHeading2", ActionId = "paragraph.heading", StateId = "paragraph.heading.2", Args = new Dictionary<string, object?> { ["level"] = 2 }, Shortcut = "Ctrl+2", IsCheckable = true },
                    new() { HeaderKey = "ParagraphHeading3", ActionId = "paragraph.heading", StateId = "paragraph.heading.3", Args = new Dictionary<string, object?> { ["level"] = 3 }, Shortcut = "Ctrl+3", IsCheckable = true },
                    new() { HeaderKey = "ParagraphHeading4", ActionId = "paragraph.heading", StateId = "paragraph.heading.4", Args = new Dictionary<string, object?> { ["level"] = 4 }, Shortcut = "Ctrl+4", IsCheckable = true },
                    new() { HeaderKey = "ParagraphHeading5", ActionId = "paragraph.heading", StateId = "paragraph.heading.5", Args = new Dictionary<string, object?> { ["level"] = 5 }, Shortcut = "Ctrl+5", IsCheckable = true },
                    new() { HeaderKey = "ParagraphHeading6", ActionId = "paragraph.heading", StateId = "paragraph.heading.6", Args = new Dictionary<string, object?> { ["level"] = 6 }, Shortcut = "Ctrl+6", IsCheckable = true },
                ]
            },
            new() { HeaderKey = "ParagraphIncreaseHeading", ActionId = "paragraph.heading.increase", Shortcut = "Ctrl+Alt+]", IsCheckable = true },
            new() { HeaderKey = "ParagraphDecreaseHeading", ActionId = "paragraph.heading.decrease", Shortcut = "Ctrl+Alt+[", IsCheckable = true },
            new() { HeaderKey = "MenuSeparator", IsSeparator = true },
            new() { HeaderKey = "ParagraphQuote", ActionId = "paragraph.quote", Shortcut = "Ctrl+Alt+Q", IsCheckable = true },
            new() { HeaderKey = "ParagraphOrderedList", ActionId = "paragraph.ordered-list", Shortcut = "Ctrl+Alt+7", IsCheckable = true },
            new() { HeaderKey = "ParagraphUnorderedList", ActionId = "paragraph.unordered-list", Shortcut = "Ctrl+Alt+8", IsCheckable = true },
            new() { HeaderKey = "ParagraphTaskList", ActionId = "paragraph.task-list", Shortcut = "Ctrl+Alt+9", IsCheckable = true },
            new() { HeaderKey = "MenuSeparator", IsSeparator = true },
            new() { HeaderKey = "ParagraphCodeFence", ActionId = "paragraph.code-fence", Shortcut = "Ctrl+Shift+K", IsCheckable = true },
            new() { HeaderKey = "ParagraphMathBlock", ActionId = "paragraph.math-block", Shortcut = "Ctrl+Alt+M" },
            new() { HeaderKey = "ParagraphTable", ActionId = "paragraph.table", Shortcut = "Ctrl+Alt+T", IsCheckable = true },
            new() { HeaderKey = "ParagraphFootnote", ActionId = "paragraph.footnote", Shortcut = "Ctrl+Alt+F" },
            new() { HeaderKey = "ParagraphHorizontalRule", ActionId = "paragraph.horizontal-rule", Shortcut = "Ctrl+Alt+H" },
        ];
    }

    public static IReadOnlyList<EditorActionMenuDefinition> CreateFormatMenuDefinitions()
    {
        return
        [
            new() { HeaderKey = "FormatBold", ActionId = "format.bold", Shortcut = "Ctrl+B", IsCheckable = true },
            new() { HeaderKey = "FormatItalic", ActionId = "format.italic", Shortcut = "Ctrl+I", IsCheckable = true },
            new() { HeaderKey = "FormatUnderline", ActionId = "format.underline", Shortcut = "Ctrl+U", IsCheckable = true },
            new() { HeaderKey = "FormatStrikethrough", ActionId = "format.strikethrough", Shortcut = "Ctrl+Alt+S", IsCheckable = true },
            new() { HeaderKey = "FormatInlineCode", ActionId = "format.inline-code", Shortcut = "Ctrl+Shift+`", IsCheckable = true },
            new() { HeaderKey = "FormatInlineMath", ActionId = "format.inline-math", Shortcut = "Ctrl+Alt+K", IsCheckable = true },
            new() { HeaderKey = "MenuSeparator", IsSeparator = true },
            new() { HeaderKey = "FormatLink", ActionId = "format.link", Shortcut = "Ctrl+K", IsCheckable = true },
            new() { HeaderKey = "FormatImage", ActionId = "format.image", Shortcut = "Ctrl+Shift+I" },
            new() { HeaderKey = "FormatHighlight", ActionId = "format.highlight", Shortcut = "Ctrl+Shift+H", IsCheckable = true },
            new() { HeaderKey = "FormatSuperscript", ActionId = "format.superscript", Shortcut = "Ctrl+.", IsCheckable = true },
            new() { HeaderKey = "FormatSubscript", ActionId = "format.subscript", Shortcut = "Ctrl+,", IsCheckable = true },
            new() { HeaderKey = "MenuSeparator", IsSeparator = true },
            new() { HeaderKey = "FormatClear", ActionId = "format.clear", Shortcut = "Ctrl+\\" },
        ];
    }

    public static IReadOnlyList<EditorActionDescriptor> SourceActionDescriptors { get; } =
    [
        new("paragraph.paragraph", "Ctrl+0"),
        new("paragraph.heading.increase", "Ctrl+Alt+]"),
        new("paragraph.heading.decrease", "Ctrl+Alt+["),
        new("paragraph.quote", "Ctrl+Alt+Q"),
        new("paragraph.ordered-list", "Ctrl+Alt+7"),
        new("paragraph.unordered-list", "Ctrl+Alt+8"),
        new("paragraph.task-list", "Ctrl+Alt+9"),
        new("paragraph.code-fence", "Ctrl+Shift+K"),
        new("paragraph.math-block", "Ctrl+Alt+M"),
        new("paragraph.table", "Ctrl+Alt+T"),
        new("paragraph.footnote", "Ctrl+Alt+F"),
        new("paragraph.horizontal-rule", "Ctrl+Alt+H"),
        new("format.bold", "Ctrl+B"),
        new("format.italic", "Ctrl+I"),
        new("format.underline", "Ctrl+U"),
        new("format.strikethrough", "Ctrl+Alt+S"),
        new("format.inline-code", "Ctrl+Shift+`"),
        new("format.inline-math", "Ctrl+Alt+K"),
        new("format.link", "Ctrl+K"),
        new("format.image", "Ctrl+Shift+I"),
        new("format.highlight", "Ctrl+Shift+H"),
        new("format.superscript", "Ctrl+."),
        new("format.subscript", "Ctrl+,"),
        new("format.clear", "Ctrl+\\"),
        new("paragraph.heading.1", "Ctrl+1"),
        new("paragraph.heading.2", "Ctrl+2"),
        new("paragraph.heading.3", "Ctrl+3"),
        new("paragraph.heading.4", "Ctrl+4"),
        new("paragraph.heading.5", "Ctrl+5"),
        new("paragraph.heading.6", "Ctrl+6"),
    ];
}
