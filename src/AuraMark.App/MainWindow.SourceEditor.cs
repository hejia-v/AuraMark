using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AuraMark.Core.Editing;
using AuraMark.Core.Text;

namespace AuraMark.App;

public partial class MainWindow
{
    private readonly MarkdownEditorReducer _sourceEditorActionReducer = new();
    private readonly MarkdownEditorActionStateEvaluator _sourceEditorActionStateEvaluator = new();
    private bool _suppressSourceEditorUpdates;

    private void InitializeSourceEditor()
    {
        SourceTextEditor.Focusable = true;
        SourceTextEditor.TextContentChanged += OnSourceEditorTextChanged;
        SourceTextEditor.EditorSelectionChanged += (_, _) => HandleSourceSelectionOrCaretChanged();
        SourceTextEditor.HistoryStateChanged += (_, _) => UpdateSourceHistoryAvailability();
        SourceTextEditor.IsReadOnly = _inputFrozen;
    }

    private void ToggleSourceMode()
    {
        ApplySourceModeState(true);
    }

    private void ApplySourceModeState(bool enabled)
    {
        if (_isSourceMode == enabled &&
            SourceEditorHost.Visibility == Visibility.Visible)
        {
            UpdateSourceModeToggleUi();
            return;
        }

        _isSourceMode = true;
        SourceEditorHost.Visibility = Visibility.Visible;
        SourceTextEditor.IsReadOnly = _inputFrozen;
        UpdateSourceModeToggleUi();
        SetSourceEditorText(_pendingMarkdown, clearUndoStack: false);
        ShowLoading(false);
        UpdateSourceHistoryAvailability();
        UpdateSourceEditorActionStates();
        UpdateSourceActiveHeading();
        FocusSourceEditorDeferred();
    }

    private void FocusSourceEditorDeferred()
    {
        void FocusSourceEditor()
        {
            if (!_isSourceMode || SourceEditorHost.Visibility != Visibility.Visible)
            {
                return;
            }

            FocusManager.SetFocusedElement(this, SourceTextEditor);
            SourceTextEditor.Focus();
            Keyboard.Focus(SourceTextEditor);
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
            var prefix = SourceTextEditor.TextLength > 0 &&
                         !SourceTextEditor.Text.EndsWith("\n", StringComparison.Ordinal) &&
                         !SourceTextEditor.Text.EndsWith("\r", StringComparison.Ordinal)
                ? Environment.NewLine
                : string.Empty;

            SourceTextEditor.SetSelection(SourceTextEditor.TextLength, 0);
            SourceTextEditor.ReplaceSelection(prefix + _e2eSourceAppendText);
            _e2eSourceAppendText = string.Empty;
        }

        FocusSourceEditorDeferred();
    }

    private void SetSourceEditorText(string markdown, bool clearUndoStack)
    {
        var nextText = markdown ?? string.Empty;
        if (!clearUndoStack && string.Equals(SourceTextEditor.Text, nextText, StringComparison.Ordinal))
        {
            return;
        }

        _suppressSourceEditorUpdates = true;
        try
        {
            SourceTextEditor.LoadText(nextText, clearUndoStack);
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
        if (_suppressSourceEditorUpdates || _inputFrozen)
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

        SetHistoryAvailability(!_inputFrozen && SourceTextEditor.CanUndo, !_inputFrozen && SourceTextEditor.CanRedo);
    }

    private bool TryExecuteSourceEditorAction(string actionId, IReadOnlyDictionary<string, object?>? args = null)
    {
        var state = new MarkdownEditorState(
            SourceTextEditor.Text,
            TextSelection.FromStartAndLength(SourceTextEditor.SelectionStart, SourceTextEditor.SelectionLength));

        if (!_sourceEditorActionReducer.TryReduce(state, new MarkdownEditorAction(actionId, args), out var result) ||
            result is null)
        {
            return false;
        }

        var updatedMarkdown = ApplyMarkdownEditResult(state.Text, result);

        _suppressSourceEditorUpdates = true;
        try
        {
            SourceTextEditor.LoadText(updatedMarkdown, clearUndoStack: false);
            SourceTextEditor.SetSelection(result.Selection.Start, result.Selection.Length);
        }
        finally
        {
            _suppressSourceEditorUpdates = false;
        }

        _pendingMarkdown = updatedMarkdown;
        _dirty = !string.Equals(_currentMarkdown, updatedMarkdown, StringComparison.Ordinal);
        UpdateOutline(updatedMarkdown);
        SetState(_dirty ? EditorState.Dirty : EditorState.Editing);
        SetSavingDot(_dirty);
        HideError();
        TrackTyping();
        UpdateSourceHistoryAvailability();
        UpdateSourceEditorActionStates();
        UpdateSourceActiveHeading();
        FocusSourceEditorDeferred();
        return true;
    }

    private void UpdateSourceEditorActionStates()
    {
        if (!_isSourceMode)
        {
            return;
        }

        var snapshot = _sourceEditorActionStateEvaluator.Evaluate(
            new MarkdownEditorState(
                SourceTextEditor.Text,
                TextSelection.FromStartAndLength(SourceTextEditor.SelectionStart, SourceTextEditor.SelectionLength)));

        _editorActionStates.Clear();
        foreach (var (actionId, state) in SourceEditorActionStateFactory.Create(
                     snapshot,
                     enabled: !_inputFrozen,
                     ResolveEditorActionShortcut))
        {
            _editorActionStates[actionId] = state;
        }

        ApplyEditorActionMenuStates();
    }

    private void UpdateSourceActiveHeading()
    {
        if (!_isSourceMode || _outlineDocument.Headings.Count == 0)
        {
            SetActiveHeadingIndex(-1);
            return;
        }

        var activeIndex = -1;
        var caretOffset = Math.Min(SourceTextEditor.CaretOffset, SourceTextEditor.TextLength);
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
        if (!_isSourceMode || index < 0 || index >= _outlineDocument.Headings.Count)
        {
            return;
        }

        var headingOffset = Math.Min(_outlineDocument.Headings[index].SourceOffset, SourceTextEditor.TextLength);
        SourceTextEditor.CaretOffset = headingOffset;
        SourceTextEditor.ScrollToOffset(headingOffset);
        SourceTextEditor.Focus();
    }

    private static string ApplyMarkdownEditResult(string originalText, MarkdownEditorEditResult result)
    {
        var builder = new StringBuilder(originalText);
        foreach (var replacement in result.Replacements.OrderByDescending(item => item.Start))
        {
            builder.Remove(replacement.Start, replacement.Length);
            builder.Insert(replacement.Start, replacement.NewText);
        }

        return builder.ToString();
    }
}
