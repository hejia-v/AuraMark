using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AuraMark.Core.Editing;
using AuraMark.Core.Syntax;
using AuraMark.Core.Text;

namespace AuraMark.App;

public partial class MainWindow
{
    private readonly MarkdownEditorReducer _markdownEditorReducer = new();
    private readonly MarkdownEditorActionStateEvaluator _markdownEditorActionStateEvaluator = new();
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

    private void ApplyE2eSourceModeProbe()
    {
        if (!_e2eSourceModePending && string.IsNullOrWhiteSpace(_e2eSourceAppendText))
        {
            return;
        }

        _e2eSourceModePending = false;
        ApplySourceModeState(true);

        if (!string.IsNullOrWhiteSpace(_e2eSourceAppendText))
        {
            var lineEnding = MarkdownEditorTextUtilities.DetectLineEnding(SourceTextEditor.Text);
            var prefix = SourceTextEditor.Text.Length == 0 || SourceTextEditor.Text.EndsWith(lineEnding, StringComparison.Ordinal)
                ? string.Empty
                : lineEnding;
            var addition = prefix + _e2eSourceAppendText;
            ApplySourceEditResult(new MarkdownEditorEditResult(
                [new MarkdownEditorReplacement(SourceTextEditor.Text.Length, 0, addition)],
                TextSelection.Collapsed(SourceTextEditor.Text.Length + addition.Length)));
            _e2eSourceAppendText = string.Empty;
        }

        FocusSourceEditorDeferred();
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

        var handled = _markdownEditorReducer.TryReduce(
            CreateMarkdownEditorState(),
            new MarkdownEditorAction(actionId, args),
            out var result);

        if (handled && result is not null)
        {
            ApplySourceEditResult(result);
        }

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
        var snapshot = _markdownEditorActionStateEvaluator.Evaluate(CreateMarkdownEditorState());
        var actions = SourceEditorActionStateFactory.Create(snapshot, enabled, ResolveEditorActionShortcut);

        _editorActionStates.Clear();
        foreach (var (actionId, state) in actions)
        {
            _editorActionStates[actionId] = state;
        }

        ApplyEditorActionMenuStates();
    }

    private void UpdateSourceActiveHeading()
    {
        if (!_isSourceMode || SourceTextEditor.Document is null || _outlineDocument.Headings.Count == 0)
        {
            SetActiveHeadingIndex(-1);
            return;
        }

        var activeIndex = -1;
        var caretOffset = Math.Min(SourceTextEditor.CaretOffset, SourceTextEditor.Text.Length);
        for (var index = 0; index < _outlineDocument.Headings.Count; index++)
        {
            if (_outlineDocument.Headings[index].SourceOffset > caretOffset)
            {
                break;
            }

            activeIndex = index;
        }

        SetActiveHeadingIndex(activeIndex);
    }

    private void ScrollSourceEditorToHeading(int index)
    {
        if (!_isSourceMode ||
            index < 0 ||
            index >= _outlineDocument.Headings.Count ||
            SourceTextEditor.Document is null)
        {
            return;
        }

        var headingOffset = Math.Min(_outlineDocument.Headings[index].SourceOffset, SourceTextEditor.Document.TextLength);
        var line = SourceTextEditor.Document.GetLineByOffset(headingOffset);
        SourceTextEditor.ScrollToLine(line.LineNumber);
        SourceTextEditor.CaretOffset = line.Offset;
        SourceTextEditor.Focus();
    }

    private MarkdownEditorState CreateMarkdownEditorState()
    {
        return new MarkdownEditorState(
            SourceTextEditor.Text,
            TextSelection.FromStartAndLength(SourceTextEditor.SelectionStart, SourceTextEditor.SelectionLength));
    }

    private void ApplySourceEditResult(MarkdownEditorEditResult result)
    {
        var document = SourceTextEditor.Document;
        if (document is null)
        {
            return;
        }

        document.BeginUpdate();
        try
        {
            foreach (var replacement in result.Replacements.OrderByDescending(item => item.Start))
            {
                document.Replace(replacement.Start, replacement.Length, replacement.NewText);
            }
        }
        finally
        {
            document.EndUpdate();
        }

        var nextSelectionStart = Math.Min(result.Selection.Start, SourceTextEditor.Text.Length);
        var nextSelectionEnd = Math.Min(result.Selection.End, SourceTextEditor.Text.Length);
        SourceTextEditor.Select(nextSelectionStart, Math.Max(0, nextSelectionEnd - nextSelectionStart));
        SourceTextEditor.CaretOffset = nextSelectionEnd;
    }
}
