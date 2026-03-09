using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using System.Text.Json;

namespace AuraMark.App;

public partial class MainWindow
{
    private sealed record SourceLineSelection(int StartOffset, int Length, string[] Lines);

    private static readonly Regex SourceOrderedListRegex = new(@"^\s*\d+[.)]\s+", RegexOptions.Compiled);
    private static readonly Regex SourceUnorderedListRegex = new(@"^\s*[-+*]\s+", RegexOptions.Compiled);
    private static readonly Regex SourceTaskListRegex = new(@"^\s*[-+*]\s+\[(?: |x|X)\]\s+", RegexOptions.Compiled);
    private static readonly Regex SourceQuoteRegex = new(@"^\s*>\s+", RegexOptions.Compiled);
    private static readonly Regex SourceHeadingRegex = new(@"^\s{0,3}(#{1,6})\s+", RegexOptions.Compiled);
    private static readonly Regex SourceFenceRegex = new(@"^\s*```", RegexOptions.Compiled);
    private static readonly Regex SourceLineEndingRegex = new(@"\r\n|\n|\r", RegexOptions.Compiled);

    private bool _suppressSourceEditorUpdates;

    private void InitializeSourceEditor()
    {
        SourceTextEditor.Focusable = true;
        SourceTextEditor.IsTabStop = true;
        SourceTextEditor.Options.ConvertTabsToSpaces = false;
        SourceTextEditor.Options.IndentationSize = 2;
        SourceTextEditor.Options.EnableHyperlinks = false;
        SourceTextEditor.TextArea.Focusable = true;
        SourceTextEditor.TextArea.IsTabStop = true;
        SourceTextEditor.TextArea.TextView.LineTransformers.Add(new MarkdownSourceColorizer());
        SourceTextEditor.TextChanged += OnSourceEditorTextChanged;
        SourceTextEditor.TextArea.SelectionChanged += (_, _) => HandleSourceSelectionOrCaretChanged();
        SourceTextEditor.TextArea.Caret.PositionChanged += (_, _) => HandleSourceSelectionOrCaretChanged();
        SourceTextEditor.IsReadOnly = _inputFrozen;
    }

    private void ToggleSourceMode()
    {
        ApplySourceModeState(!_isSourceMode);
    }

    private void ApplySourceModeState(bool enabled)
    {
        if (_isSourceMode == enabled &&
            WebViewHost.Visibility == (enabled ? Visibility.Collapsed : Visibility.Visible) &&
            SourceEditorHost.Visibility == (enabled ? Visibility.Visible : Visibility.Collapsed))
        {
            UpdateSourceModeToggleUi();
            return;
        }

        _isSourceMode = enabled;
        WebViewHost.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        SourceEditorHost.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        SourceTextEditor.IsReadOnly = _inputFrozen;
        UpdateSourceModeToggleUi();

        if (enabled)
        {
            SetSourceEditorText(_pendingMarkdown, clearUndoStack: false);
            ShowLoading(false);
            UpdateSourceHistoryAvailability();
            UpdateSourceEditorActionStates();
            UpdateSourceActiveHeading();
            FocusSourceEditorDeferred();
            return;
        }

        SetActiveHeadingIndex(-1);
        _editorActionStates.Clear();
        ApplyEditorActionMenuStates();
        ResetHistoryAvailability();
        ShowLoading(true, Text("Rendering"));
        QueueDocumentToWeb(_pendingMarkdown);
        Dispatcher.InvokeAsync(() => MainWebView.Focus(), DispatcherPriority.Background);
    }

    private void FocusSourceEditorDeferred()
    {
        void FocusSourceEditor()
        {
            if (!_isSourceMode || SourceEditorHost.Visibility != Visibility.Visible)
            {
                return;
            }

            if (SourceTextEditor.CaretOffset > SourceTextEditor.Text.Length)
            {
                SourceTextEditor.CaretOffset = SourceTextEditor.Text.Length;
            }

            FocusManager.SetFocusedElement(this, SourceTextEditor.TextArea);
            SourceTextEditor.Focus();
            SourceTextEditor.TextArea.Focus();
            Keyboard.Focus(SourceTextEditor.TextArea);
            SourceTextEditor.TextArea.Caret.BringCaretToView();
        }

        Dispatcher.InvokeAsync(FocusSourceEditor, DispatcherPriority.Input);
        Dispatcher.InvokeAsync(FocusSourceEditor, DispatcherPriority.ContextIdle);
        Dispatcher.InvokeAsync(FocusSourceEditor, DispatcherPriority.ApplicationIdle);
    }

    private void SetSourceEditorText(string markdown, bool clearUndoStack)
    {
        var nextText = markdown ?? string.Empty;
        var caretOffset = Math.Min(SourceTextEditor.CaretOffset, nextText.Length);
        _suppressSourceEditorUpdates = true;
        try
        {
            if (!string.Equals(SourceTextEditor.Text, nextText, StringComparison.Ordinal))
            {
                SourceTextEditor.Text = nextText;
            }

            if (clearUndoStack)
            {
                SourceTextEditor.Document?.UndoStack.ClearAll();
            }

            SourceTextEditor.CaretOffset = Math.Min(caretOffset, SourceTextEditor.Text.Length);
        }
        finally
        {
            _suppressSourceEditorUpdates = false;
        }

        if (_isSourceMode)
        {
            UpdateSourceHistoryAvailability();
            UpdateSourceEditorActionStates();
            UpdateSourceActiveHeading();
        }
    }

    private void OnSourceEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressSourceEditorUpdates)
        {
            return;
        }

        if (_inputFrozen)
        {
            return;
        }

        _pendingMarkdown = SourceTextEditor.Text;
        _dirty = true;
        UpdateOutline(_pendingMarkdown);
        SetState(EditorState.Dirty);
        SetSavingDot(true);
        HideError();
        TrackTyping();

        if (_isSourceMode)
        {
            UpdateSourceHistoryAvailability();
            UpdateSourceEditorActionStates();
            UpdateSourceActiveHeading();
        }
    }

    private void HandleSourceSelectionOrCaretChanged()
    {
        if (_suppressSourceEditorUpdates || !_isSourceMode)
        {
            return;
        }

        UpdateSourceEditorActionStates();
        UpdateSourceActiveHeading();
    }

    private void UpdateSourceEditorReadOnly()
    {
        SourceTextEditor.IsReadOnly = _inputFrozen;
        if (_isSourceMode)
        {
            UpdateSourceHistoryAvailability();
            UpdateSourceEditorActionStates();
        }
    }

    private void UpdateSourceHistoryAvailability()
    {
        if (!_isSourceMode)
        {
            return;
        }

        var canUndo = !_inputFrozen && (SourceTextEditor.Document?.UndoStack.CanUndo ?? false);
        var canRedo = !_inputFrozen && (SourceTextEditor.Document?.UndoStack.CanRedo ?? false);
        SetHistoryAvailability(canUndo, canRedo);
    }

    private bool TryExecuteSourceEditorAction(string actionId, IReadOnlyDictionary<string, object?>? args = null)
    {
        if (_inputFrozen)
        {
            return false;
        }

        var handled = actionId switch
        {
            "paragraph.paragraph" => MutateSourceSelectedLines(lines =>
                lines.Select(line =>
                {
                    var leading = line[..(line.Length - line.TrimStart().Length)];
                    var body = line.TrimStart();
                    body = SourceHeadingRegex.Replace(body, string.Empty);
                    body = SourceQuoteRegex.Replace(body, string.Empty);
                    body = SourceTaskListRegex.Replace(body, string.Empty);
                    body = SourceOrderedListRegex.Replace(body, string.Empty);
                    body = SourceUnorderedListRegex.Replace(body, string.Empty);
                    return body.Length > 0 ? $"{leading}{body}" : line;
                }).ToArray()),
            "paragraph.heading" => RunSourceHeading(GetHeadingLevelArg(args)),
            "paragraph.heading.increase" => RunSourceIncreaseHeading(),
            "paragraph.heading.decrease" => RunSourceDecreaseHeading(),
            "paragraph.quote" => RunSourceToggleQuote(),
            "paragraph.ordered-list" => RunSourceOrderedList(),
            "paragraph.unordered-list" => RunSourceUnorderedList(),
            "paragraph.task-list" => RunSourceTaskList(),
            "paragraph.code-fence" => WrapSourceSelection("\n```text\n", "\n```\n", "code"),
            "paragraph.math-block" => WrapSourceSelection("\n$$\n", "\n$$\n", "math"),
            "paragraph.table" => ReplaceSourceSelection("| Column 1 | Column 2 | Column 3 |\n| --- | --- | --- |\n| Value 1 | Value 2 | Value 3 |"),
            "paragraph.footnote" => RunSourceFootnote(),
            "paragraph.horizontal-rule" => ReplaceSourceSelection("\n\n---\n\n"),
            "format.bold" => WrapSourceSelection("**", "**", "bold"),
            "format.italic" => WrapSourceSelection("*", "*", "italic"),
            "format.underline" => WrapSourceSelection("<u>", "</u>", "underlined"),
            "format.strikethrough" => WrapSourceSelection("~~", "~~", "struck"),
            "format.inline-code" => WrapSourceSelection("`", "`", "code"),
            "format.inline-math" => WrapSourceSelection("$", "$", "x"),
            "format.link" => RunSourceLink(),
            "format.image" => ReplaceSourceSelection("![alt text](path/to/image.png)", 2, 10),
            "format.highlight" => WrapSourceSelection("<mark>", "</mark>", "highlight"),
            "format.superscript" => WrapSourceSelection("<sup>", "</sup>", "sup"),
            "format.subscript" => WrapSourceSelection("<sub>", "</sub>", "sub"),
            "format.clear" => RunSourceClearFormatting(),
            _ => false,
        };

        if (handled)
        {
            UpdateSourceHistoryAvailability();
            UpdateSourceEditorActionStates();
            UpdateSourceActiveHeading();
            SourceTextEditor.Focus();
        }

        return handled;
    }

    private void UpdateSourceEditorActionStates()
    {
        if (!_isSourceMode)
        {
            return;
        }

        var enabled = !_inputFrozen;
        var currentLine = GetCurrentSourceLineText();
        var actions = new Dictionary<string, EditorActionState>(StringComparer.Ordinal)
        {
            ["paragraph.paragraph"] = new() { Enabled = enabled, Active = IsSourceParagraphActive(), Shortcut = ResolveEditorActionShortcut("paragraph.paragraph", "Ctrl+0") },
            ["paragraph.heading.increase"] = new() { Enabled = enabled, Active = GetCurrentSourceHeadingLevel() > 0, Shortcut = ResolveEditorActionShortcut("paragraph.heading.increase", "Ctrl+Alt+]") },
            ["paragraph.heading.decrease"] = new() { Enabled = enabled, Active = GetCurrentSourceHeadingLevel() > 0, Shortcut = ResolveEditorActionShortcut("paragraph.heading.decrease", "Ctrl+Alt+[") },
            ["paragraph.quote"] = new() { Enabled = enabled, Active = SourceQuoteRegex.IsMatch(currentLine), Shortcut = ResolveEditorActionShortcut("paragraph.quote", "Ctrl+Alt+Q") },
            ["paragraph.ordered-list"] = new() { Enabled = enabled, Active = SourceOrderedListRegex.IsMatch(currentLine), Shortcut = ResolveEditorActionShortcut("paragraph.ordered-list", "Ctrl+Alt+7") },
            ["paragraph.unordered-list"] = new() { Enabled = enabled, Active = SourceUnorderedListRegex.IsMatch(currentLine), Shortcut = ResolveEditorActionShortcut("paragraph.unordered-list", "Ctrl+Alt+8") },
            ["paragraph.task-list"] = new() { Enabled = enabled, Active = SourceTaskListRegex.IsMatch(currentLine), Shortcut = ResolveEditorActionShortcut("paragraph.task-list", "Ctrl+Alt+9") },
            ["paragraph.code-fence"] = new() { Enabled = enabled, Active = IsSourceWithinFenceBlock(), Shortcut = ResolveEditorActionShortcut("paragraph.code-fence", "Ctrl+Shift+K") },
            ["paragraph.math-block"] = new() { Enabled = enabled, Active = false, Shortcut = ResolveEditorActionShortcut("paragraph.math-block", "Ctrl+Alt+M") },
            ["paragraph.table"] = new() { Enabled = enabled, Active = GetSelectedSourceLines().Any(line => line.Contains('|')), Shortcut = ResolveEditorActionShortcut("paragraph.table", "Ctrl+Alt+T") },
            ["paragraph.footnote"] = new() { Enabled = enabled, Active = false, Shortcut = ResolveEditorActionShortcut("paragraph.footnote", "Ctrl+Alt+F") },
            ["paragraph.horizontal-rule"] = new() { Enabled = enabled, Active = false, Shortcut = ResolveEditorActionShortcut("paragraph.horizontal-rule", "Ctrl+Alt+H") },
            ["format.bold"] = new() { Enabled = enabled, Active = IsSourceWrappedSelection("**", "**"), Shortcut = ResolveEditorActionShortcut("format.bold", "Ctrl+B") },
            ["format.italic"] = new() { Enabled = enabled, Active = IsSourceWrappedSelection("*", "*"), Shortcut = ResolveEditorActionShortcut("format.italic", "Ctrl+I") },
            ["format.underline"] = new() { Enabled = enabled, Active = IsSourceWrappedSelection("<u>", "</u>"), Shortcut = ResolveEditorActionShortcut("format.underline", "Ctrl+U") },
            ["format.strikethrough"] = new() { Enabled = enabled, Active = IsSourceWrappedSelection("~~", "~~"), Shortcut = ResolveEditorActionShortcut("format.strikethrough", "Ctrl+Alt+S") },
            ["format.inline-code"] = new() { Enabled = enabled, Active = IsSourceWrappedSelection("`", "`"), Shortcut = ResolveEditorActionShortcut("format.inline-code", "Ctrl+Shift+`") },
            ["format.inline-math"] = new() { Enabled = enabled, Active = IsSourceWrappedSelection("$", "$"), Shortcut = ResolveEditorActionShortcut("format.inline-math", "Ctrl+Alt+K") },
            ["format.link"] = new() { Enabled = enabled, Active = false, Shortcut = ResolveEditorActionShortcut("format.link", "Ctrl+K") },
            ["format.image"] = new() { Enabled = enabled, Active = false, Shortcut = ResolveEditorActionShortcut("format.image", "Ctrl+Shift+I") },
            ["format.highlight"] = new() { Enabled = enabled, Active = IsSourceWrappedSelection("<mark>", "</mark>"), Shortcut = ResolveEditorActionShortcut("format.highlight", "Ctrl+Shift+H") },
            ["format.superscript"] = new() { Enabled = enabled, Active = IsSourceWrappedSelection("<sup>", "</sup>"), Shortcut = ResolveEditorActionShortcut("format.superscript", "Ctrl+.") },
            ["format.subscript"] = new() { Enabled = enabled, Active = IsSourceWrappedSelection("<sub>", "</sub>"), Shortcut = ResolveEditorActionShortcut("format.subscript", "Ctrl+,") },
            ["format.clear"] = new() { Enabled = enabled, Active = false, Shortcut = ResolveEditorActionShortcut("format.clear", "Ctrl+\\") },
        };

        for (var level = 1; level <= 6; level++)
        {
            actions[$"paragraph.heading.{level}"] = new EditorActionState
            {
                Enabled = enabled,
                Active = IsSourceHeadingActive(level),
                Shortcut = ResolveEditorActionShortcut($"paragraph.heading.{level}", $"Ctrl+{level}"),
            };
        }

        _editorActionStates.Clear();
        foreach (var (actionId, state) in actions)
        {
            _editorActionStates[actionId] = state;
        }

        ApplyEditorActionMenuStates();
    }

    private void UpdateSourceActiveHeading()
    {
        if (!_isSourceMode || SourceTextEditor.Document is null || SourceTextEditor.Document.TextLength == 0)
        {
            SetActiveHeadingIndex(-1);
            return;
        }

        var caretOffset = Math.Min(SourceTextEditor.CaretOffset, Math.Max(0, SourceTextEditor.Document.TextLength - 1));
        var caretLine = SourceTextEditor.Document.GetLineByOffset(caretOffset).LineNumber;
        var activeIndex = -1;
        var headingIndex = -1;

        for (var lineNumber = 1; lineNumber <= SourceTextEditor.Document.LineCount; lineNumber++)
        {
            var lineText = SourceTextEditor.Document.GetText(SourceTextEditor.Document.GetLineByNumber(lineNumber));
            if (!SourceHeadingRegex.IsMatch(lineText))
            {
                continue;
            }

            headingIndex++;
            if (lineNumber <= caretLine)
            {
                activeIndex = headingIndex;
            }
            else
            {
                break;
            }
        }

        SetActiveHeadingIndex(activeIndex);
    }

    private void ScrollSourceEditorToHeading(int index)
    {
        if (!_isSourceMode || index < 0 || SourceTextEditor.Document is null)
        {
            return;
        }

        var headingIndex = -1;
        for (var lineNumber = 1; lineNumber <= SourceTextEditor.Document.LineCount; lineNumber++)
        {
            var line = SourceTextEditor.Document.GetLineByNumber(lineNumber);
            if (!SourceHeadingRegex.IsMatch(SourceTextEditor.Document.GetText(line)))
            {
                continue;
            }

            headingIndex++;
            if (headingIndex != index)
            {
                continue;
            }

            SourceTextEditor.ScrollToLine(lineNumber);
            SourceTextEditor.CaretOffset = line.Offset;
            SourceTextEditor.Focus();
            return;
        }
    }

    private bool RunSourceHeading(int level)
    {
        return MutateSourceSelectedLines(lines =>
        {
            var prefix = $"{new string('#', level)} ";
            return lines.Select(line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return line;
                }

                var body = SourceHeadingRegex.Replace(line.TrimStart(), string.Empty);
                body = SourceQuoteRegex.Replace(body, string.Empty);
                return $"{prefix}{body}";
            }).ToArray();
        });
    }

    private bool RunSourceIncreaseHeading()
    {
        return MutateSourceSelectedLines(lines => lines.Select(line =>
        {
            var match = SourceHeadingRegex.Match(line);
            if (!match.Success)
            {
                return $"# {line.Trim()}";
            }

            var nextLevel = Math.Min(6, match.Groups[1].Length + 1);
            return $"{new string('#', nextLevel)} {SourceHeadingRegex.Replace(line.TrimStart(), string.Empty)}";
        }).ToArray());
    }

    private bool RunSourceDecreaseHeading()
    {
        return MutateSourceSelectedLines(lines => lines.Select(line =>
        {
            var match = SourceHeadingRegex.Match(line);
            if (!match.Success)
            {
                return line;
            }

            var nextLevel = match.Groups[1].Length - 1;
            var body = SourceHeadingRegex.Replace(line.TrimStart(), string.Empty);
            return nextLevel <= 0 ? body : $"{new string('#', nextLevel)} {body}";
        }).ToArray());
    }

    private bool RunSourceToggleQuote()
    {
        return MutateSourceSelectedLines(lines =>
        {
            var nonEmptyLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            var shouldRemove = nonEmptyLines.Length > 0 && nonEmptyLines.All(line => SourceQuoteRegex.IsMatch(line));
            return lines.Select(line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return line;
                }

                return shouldRemove ? SourceQuoteRegex.Replace(line, string.Empty) : $"> {line}";
            }).ToArray();
        });
    }

    private bool RunSourceOrderedList()
    {
        return MutateSourceSelectedLines(lines => lines.Select((line, index) =>
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return line;
            }

            return $"{index + 1}. {StripSourceListMarkers(line)}";
        }).ToArray());
    }

    private bool RunSourceUnorderedList()
    {
        return MutateSourceSelectedLines(lines => lines.Select(line =>
            string.IsNullOrWhiteSpace(line) ? line : $"- {StripSourceListMarkers(line)}").ToArray());
    }

    private bool RunSourceTaskList()
    {
        return MutateSourceSelectedLines(lines => lines.Select(line =>
            string.IsNullOrWhiteSpace(line) ? line : $"- [ ] {StripSourceListMarkers(line)}").ToArray());
    }

    private bool RunSourceFootnote()
    {
        var document = SourceTextEditor.Document;
        if (document is null)
        {
            return false;
        }

        var selectedText = string.IsNullOrEmpty(SourceTextEditor.SelectedText) ? "footnote" : SourceTextEditor.SelectedText;
        var marker = $"[^{FindNextFootnoteIndex(SourceTextEditor.Text)}]";
        var start = SourceTextEditor.SelectionStart;
        var length = SourceTextEditor.SelectionLength;
        var lineEnding = DetectSourceLineEnding(SourceTextEditor.Text);
        var notePrefix = SourceTextEditor.Text.Length == 0 || SourceTextEditor.Text.EndsWith($"{lineEnding}{lineEnding}", StringComparison.Ordinal)
            ? string.Empty
            : $"{lineEnding}{lineEnding}";

        document.BeginUpdate();
        try
        {
            document.Replace(start, length, $"{selectedText}{marker}");
            document.Insert(document.TextLength, $"{notePrefix}[^{marker[2..^1]}]: note");
        }
        finally
        {
            document.EndUpdate();
        }

        var markerStart = start + selectedText.Length;
        SourceTextEditor.Select(markerStart, marker.Length);
        SourceTextEditor.CaretOffset = markerStart + marker.Length;
        return true;
    }

    private bool RunSourceLink()
    {
        var selectedText = string.IsNullOrEmpty(SourceTextEditor.SelectedText) ? "link text" : SourceTextEditor.SelectedText;
        return ReplaceSourceSelection($"[{selectedText}](https://example.com)", selectedText.Length + 3, selectedText.Length + 22);
    }

    private bool RunSourceClearFormatting()
    {
        if (SourceTextEditor.SelectionLength == 0)
        {
            return false;
        }

        var selectedText = SourceTextEditor.SelectedText
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("~~", string.Empty, StringComparison.Ordinal);

        selectedText = Regex.Replace(selectedText, @"^\*(.*)\*$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^`(.*)`$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^\$(.*)\$$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^<u>(.*)</u>$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^<mark>(.*)</mark>$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^<sup>(.*)</sup>$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^<sub>(.*)</sub>$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^\[(.*)\]\((.*)\)$", "$1", RegexOptions.Singleline);
        selectedText = Regex.Replace(selectedText, @"^!\[(.*)\]\((.*)\)$", "$1", RegexOptions.Singleline);
        return ReplaceSourceSelection(selectedText);
    }

    private bool WrapSourceSelection(string prefix, string suffix, string placeholder)
    {
        var selectedText = string.IsNullOrEmpty(SourceTextEditor.SelectedText) ? placeholder : SourceTextEditor.SelectedText;
        var startOffset = prefix.Length;
        return ReplaceSourceSelection($"{prefix}{selectedText}{suffix}", startOffset, startOffset + selectedText.Length);
    }

    private bool ReplaceSourceSelection(string replacement, int? selectionStartOffset = null, int? selectionEndOffset = null)
    {
        var start = SourceTextEditor.SelectionStart;
        var length = SourceTextEditor.SelectionLength;
        ReplaceSourceRange(start, length, replacement, selectionStartOffset ?? replacement.Length, selectionEndOffset ?? replacement.Length);
        return true;
    }

    private bool MutateSourceSelectedLines(Func<string[], string[]> mutateLines)
    {
        var selection = GetSourceLineSelection();
        var lineEnding = DetectSourceLineEnding(SourceTextEditor.Text);
        var nextBlock = string.Join(lineEnding, mutateLines(selection.Lines));
        ReplaceSourceRange(selection.StartOffset, selection.Length, nextBlock, 0, nextBlock.Length);
        return true;
    }

    private void ReplaceSourceRange(int start, int length, string replacement, int selectionStartOffset, int selectionEndOffset)
    {
        var document = SourceTextEditor.Document;
        if (document is null)
        {
            return;
        }

        document.Replace(start, length, replacement);
        var nextSelectionStart = Math.Min(start + selectionStartOffset, SourceTextEditor.Text.Length);
        var nextSelectionEnd = Math.Min(start + selectionEndOffset, SourceTextEditor.Text.Length);
        SourceTextEditor.Select(nextSelectionStart, Math.Max(0, nextSelectionEnd - nextSelectionStart));
        SourceTextEditor.CaretOffset = nextSelectionEnd;
    }

    private SourceLineSelection GetSourceLineSelection()
    {
        var document = SourceTextEditor.Document;
        if (document is null || document.LineCount == 0)
        {
            return new SourceLineSelection(0, 0, [string.Empty]);
        }

        var start = SourceTextEditor.SelectionStart;
        var end = start + SourceTextEditor.SelectionLength;
        var startAnchor = document.TextLength == 0 ? 0 : Math.Min(start, Math.Max(0, document.TextLength - 1));
        var endAnchor = document.TextLength == 0
            ? 0
            : Math.Min(Math.Max(start, end > start ? end - 1 : start), Math.Max(0, document.TextLength - 1));

        var startLine = document.GetLineByOffset(startAnchor);
        var endLine = document.GetLineByOffset(endAnchor);
        var lines = Enumerable.Range(startLine.LineNumber, endLine.LineNumber - startLine.LineNumber + 1)
            .Select(lineNumber => document.GetText(document.GetLineByNumber(lineNumber)))
            .ToArray();
        return new SourceLineSelection(startLine.Offset, endLine.EndOffset - startLine.Offset, lines);
    }

    private string[] GetSelectedSourceLines()
    {
        return GetSourceLineSelection().Lines;
    }

    private string GetCurrentSourceLineText()
    {
        var document = SourceTextEditor.Document;
        if (document is null || document.LineCount == 0)
        {
            return string.Empty;
        }

        var offset = document.TextLength == 0 ? 0 : Math.Min(SourceTextEditor.CaretOffset, Math.Max(0, document.TextLength - 1));
        var line = document.GetLineByOffset(offset);
        return document.GetText(line);
    }

    private int GetCurrentSourceHeadingLevel()
    {
        var match = SourceHeadingRegex.Match(GetCurrentSourceLineText());
        return match.Success ? match.Groups[1].Length : 0;
    }

    private bool IsSourceParagraphActive()
    {
        var lines = GetSelectedSourceLines();
        return lines.Any(line => !string.IsNullOrWhiteSpace(line)) &&
               lines.All(line =>
               {
                   if (string.IsNullOrWhiteSpace(line))
                   {
                       return true;
                   }

                   return !SourceHeadingRegex.IsMatch(line) &&
                          !SourceQuoteRegex.IsMatch(line) &&
                          !SourceOrderedListRegex.IsMatch(line) &&
                          !SourceUnorderedListRegex.IsMatch(line);
               });
    }

    private bool IsSourceHeadingActive(int level)
    {
        var lines = GetSelectedSourceLines()
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        return lines.Length > 0 &&
               lines.All(line =>
            {
                var match = SourceHeadingRegex.Match(line);
                return match.Success && match.Groups[1].Length == level;
            });
    }

    private bool IsSourceWrappedSelection(string prefix, string suffix)
    {
        var selectionStart = SourceTextEditor.SelectionStart;
        var selectionLength = SourceTextEditor.SelectionLength;
        if (selectionLength == 0)
        {
            return false;
        }

        return selectionStart >= prefix.Length &&
               selectionStart + selectionLength + suffix.Length <= SourceTextEditor.Text.Length &&
               string.Equals(SourceTextEditor.Text[(selectionStart - prefix.Length)..selectionStart], prefix, StringComparison.Ordinal) &&
               string.Equals(SourceTextEditor.Text[(selectionStart + selectionLength)..(selectionStart + selectionLength + suffix.Length)], suffix, StringComparison.Ordinal);
    }

    private bool IsSourceWithinFenceBlock()
    {
        var document = SourceTextEditor.Document;
        if (document is null || document.LineCount == 0)
        {
            return false;
        }

        var offset = document.TextLength == 0 ? 0 : Math.Min(SourceTextEditor.CaretOffset, Math.Max(0, document.TextLength - 1));
        var lineNumber = document.GetLineByOffset(offset).LineNumber;
        var inside = false;
        for (var index = 1; index <= lineNumber; index++)
        {
            if (!SourceFenceRegex.IsMatch(document.GetText(document.GetLineByNumber(index))))
            {
                continue;
            }

            inside = !inside;
        }

        return inside;
    }

    private static int GetHeadingLevelArg(IReadOnlyDictionary<string, object?>? args)
    {
        if (args is null || !args.TryGetValue("level", out var value) || value is null)
        {
            return 1;
        }

        var level = value switch
        {
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number => jsonElement.GetInt32(),
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.String && int.TryParse(jsonElement.GetString(), out var parsed) => parsed,
            int intValue => intValue,
            long longValue => (int)longValue,
            _ => 1,
        };

        return Math.Clamp(level, 1, 6);
    }

    private static int FindNextFootnoteIndex(string markdown)
    {
        var matches = Regex.Matches(markdown, @"\[\^(\d+)\]");
        var max = 0;
        foreach (Match match in matches)
        {
            if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
            {
                max = Math.Max(max, value);
            }
        }

        return max + 1;
    }

    private static string StripSourceListMarkers(string line)
    {
        var body = line.TrimStart();
        body = SourceTaskListRegex.Replace(body, string.Empty);
        body = SourceOrderedListRegex.Replace(body, string.Empty);
        body = SourceUnorderedListRegex.Replace(body, string.Empty);
        body = SourceQuoteRegex.Replace(body, string.Empty);
        body = SourceHeadingRegex.Replace(body, string.Empty);
        return body;
    }

    private static string DetectSourceLineEnding(string text)
    {
        var match = SourceLineEndingRegex.Match(text);
        return match.Success ? match.Value : Environment.NewLine;
    }
}
