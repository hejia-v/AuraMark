using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace AuraMark.App;

public partial class MainWindow
{
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
        _ = actionId;
        _ = args;
        return false;
    }

    private void UpdateSourceEditorActionStates()
    {
        if (!_isSourceMode)
        {
            return;
        }

        _editorActionStates.Clear();
        foreach (var descriptor in EditorActionCatalog.SourceActionDescriptors)
        {
            _editorActionStates[descriptor.StateId] = new EditorActionState
            {
                Enabled = false,
                Shortcut = ResolveEditorActionShortcut(descriptor.StateId, descriptor.DefaultShortcut),
            };
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
}
