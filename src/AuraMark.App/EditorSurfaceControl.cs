using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AuraMark.Core.Editing;
using AuraMark.Core.Layout;
using AuraMark.Core.Syntax;
using AuraMark.Core.Text;
using Microsoft.CodeAnalysis.Text;
using Brush = System.Windows.Media.Brush;
using Clipboard = System.Windows.Clipboard;
using CoreRedoAction = AuraMark.Core.Editing.RedoAction;
using CoreUndoAction = AuraMark.Core.Editing.UndoAction;
using Cursors = System.Windows.Input.Cursors;
using FlowDirection = System.Windows.FlowDirection;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using SelectionRange = AuraMark.Core.Text.SelectionRange;
using Size = System.Windows.Size;

namespace AuraMark.App;

public sealed class EditorSurfaceControl : FrameworkElement
{
    private const double OuterPadding = 28;
    private const double MinimumSurfaceWidth = 320;

    private static readonly Brush SurfaceBackground = CreateBrush("#FFFCF7");
    private static readonly Brush SurfaceBorder = CreateBrush("#E5E7EB");
    private static readonly Brush EmptyStateBrush = CreateBrush("#9CA3AF");
    private static readonly Pen SurfaceBorderPen = CreatePen(SurfaceBorder, 1);

    private readonly MarkdownParser _parser = new();
    private readonly LayoutEngine _layoutEngine = new();
    private readonly EditorReducer _reducer;
    private readonly SelectionGeometryProvider _selectionGeometryProvider = new();
    private readonly CaretGeometryProvider _caretGeometryProvider = new();
    private readonly EditorHitTestService _hitTestService = new();
    private readonly ParagraphRenderer _paragraphRenderer = new();
    private readonly HeadingRenderer _headingRenderer = new();
    private readonly CodeFenceRenderer _codeFenceRenderer = new();
    private readonly QuoteRenderer _quoteRenderer = new();
    private readonly ListRenderer _listRenderer = new();
    private readonly SelectionRenderer _selectionRenderer = new();
    private readonly CaretRenderer _caretRenderer = new();
    private readonly DispatcherTimer _caretBlinkTimer;

    private EditorState _state;
    private bool _caretVisible = true;
    private bool _isPointerSelecting;
    private TextPosition _pointerAnchor = TextPosition.Zero;

    public EditorSurfaceControl()
    {
        Focusable = true;
        Cursor = System.Windows.Input.Cursors.IBeam;
        SnapsToDevicePixels = true;

        _reducer = new EditorReducer(
            new SourceTextBufferService(),
            _parser,
            _layoutEngine,
            new UndoRedoService());

        _state = EditorStateFactory.Create(string.Empty, _parser, _layoutEngine, viewport: new ViewportState(860, 0, 0));
        _caretBlinkTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(530),
        };
        _caretBlinkTimer.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateVisual();
        };
        _caretBlinkTimer.Start();

        Loaded += (_, _) => RefreshLayout();
        SizeChanged += (_, _) => RefreshLayout();
    }

    public event EventHandler? TextContentChanged;

    public event EventHandler? EditorSelectionChanged;

    public event EventHandler? HistoryStateChanged;

    public bool IsReadOnly { get; set; }

    public string Text => _state.Snapshot.Text.ToString();

    public int TextLength => _state.Snapshot.Text.Length;

    public int CaretOffset
    {
        get => _state.Snapshot.Selection.Active.Offset;
        set => SetSelection(value, 0);
    }

    public int SelectionStart => _state.Snapshot.Selection.Start;

    public int SelectionLength => _state.Snapshot.Selection.End - _state.Snapshot.Selection.Start;

    public bool CanUndo => !_state.History.UndoStack.IsEmpty;

    public bool CanRedo => !_state.History.RedoStack.IsEmpty;

    public void LoadText(string text, bool clearUndoStack)
    {
        var currentSelection = clearUndoStack
            ? SelectionRange.Collapsed(TextPosition.Zero)
            : new SelectionRange(
                new TextPosition(Math.Clamp(_state.Snapshot.Selection.Anchor.Offset, 0, text?.Length ?? 0)),
                new TextPosition(Math.Clamp(_state.Snapshot.Selection.Active.Offset, 0, text?.Length ?? 0)));

        _state.Layout?.DisposeLines();
        _state = EditorStateFactory.Create(
            text ?? string.Empty,
            _parser,
            _layoutEngine,
            viewport: CreateViewport(RenderSize));
        _state = _state with
        {
            Snapshot = _state.Snapshot with { Selection = currentSelection },
            VisualState = CreateVisualState(currentSelection),
        };

        ResetCaretBlink();
        RefreshLayout();
        RaiseSelectionAndHistoryChanged();
        TextContentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelection(int start, int length)
    {
        var anchor = new TextPosition(Math.Clamp(start, 0, TextLength));
        var active = new TextPosition(Math.Clamp(start + Math.Max(0, length), 0, TextLength));
        ApplyAction(new SetSelectionAction(new SelectionRange(anchor, active)));
    }

    public void ReplaceSelection(string text)
    {
        if (IsReadOnly)
        {
            return;
        }

        ApplyAction(new ReplaceSelectionWithTextAction(text ?? string.Empty));
    }

    public void Undo()
    {
        if (IsReadOnly || !CanUndo)
        {
            return;
        }

        ApplyAction(new AuraMark.Core.Editing.UndoAction());
    }

    public void Redo()
    {
        if (IsReadOnly || !CanRedo)
        {
            return;
        }

        ApplyAction(new AuraMark.Core.Editing.RedoAction());
    }

    public void ScrollToOffset(int offset)
    {
        BringDocumentRectIntoView(GetCaretRect(Math.Clamp(offset, 0, TextLength)));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureViewport(availableSize);
        var desiredWidth = double.IsInfinity(availableSize.Width)
            ? Math.Max(MinimumSurfaceWidth, _state.Viewport.Width + OuterPadding * 2)
            : availableSize.Width;
        var desiredHeight = Math.Max(160, (_state.Layout?.TotalHeight ?? 0) + OuterPadding * 2);
        return new Size(desiredWidth, desiredHeight);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        EnsureViewport(RenderSize);
        var pageBounds = GetPageBounds();
        drawingContext.DrawRoundedRectangle(SurfaceBackground, SurfaceBorderPen, pageBounds, 18, 18);

        if (_state.Layout is null || _state.Layout.Blocks.Count == 0)
        {
            var emptyText = CreateHintText("Start typing Markdown...");
            drawingContext.DrawText(
                emptyText,
                new Point(pageBounds.X + 24, pageBounds.Y + 24));
            return;
        }

        drawingContext.PushTransform(new TranslateTransform(pageBounds.X, pageBounds.Y));
        try
        {
            foreach (var block in _state.Layout.Blocks)
            {
                RenderBlock(drawingContext, block);
            }

            var selectionRects = _selectionGeometryProvider.GetSelectionRects(_state.Layout, _state.Snapshot.Selection);
            _selectionRenderer.Render(drawingContext, selectionRects);

            var caretRect = _caretGeometryProvider.GetCaretRect(_state.Layout, _state.Snapshot.Selection.Active);
            _caretRenderer.Render(
                drawingContext,
                caretRect,
                IsKeyboardFocused && _state.Snapshot.Selection.IsCollapsed && _caretVisible);
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        Focus();
        CaptureMouse();
        _isPointerSelecting = true;

        var position = _hitTestService.HitTest(_state.Layout, TranslateToDocument(e.GetPosition(this)));
        _pointerAnchor = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? _state.Snapshot.Selection.Anchor
            : position;

        var selection = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? new SelectionRange(_pointerAnchor, position)
            : SelectionRange.Collapsed(position);

        ApplyAction(new SetSelectionAction(selection));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_isPointerSelecting || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = _hitTestService.HitTest(_state.Layout, TranslateToDocument(e.GetPosition(this)));
        ApplyAction(new SetSelectionAction(new SelectionRange(_pointerAnchor, position)));
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (_isPointerSelecting)
        {
            _isPointerSelecting = false;
            ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);

        if (IsReadOnly || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        if (e.Text[0] == '\u001A')
        {
            return;
        }

        ApplyAction(new ReplaceSelectionWithTextAction(e.Text));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (HandleShortcutKey(e))
        {
            e.Handled = true;
            return;
        }

        if (IsReadOnly)
        {
            return;
        }

        var extendSelection = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        switch (e.Key)
        {
            case Key.Left:
                ApplyAction(new MoveCaretAction(CaretMoveKind.Left, extendSelection));
                e.Handled = true;
                break;
            case Key.Right:
                ApplyAction(new MoveCaretAction(CaretMoveKind.Right, extendSelection));
                e.Handled = true;
                break;
            case Key.Up:
                ApplyAction(new MoveCaretAction(CaretMoveKind.Up, extendSelection));
                e.Handled = true;
                break;
            case Key.Down:
                ApplyAction(new MoveCaretAction(CaretMoveKind.Down, extendSelection));
                e.Handled = true;
                break;
            case Key.Home:
                ApplyAction(new MoveCaretAction(CaretMoveKind.LineStart, extendSelection));
                e.Handled = true;
                break;
            case Key.End:
                ApplyAction(new MoveCaretAction(CaretMoveKind.LineEnd, extendSelection));
                e.Handled = true;
                break;
            case Key.Back:
                ApplyAction(new DeleteBackwardAction());
                e.Handled = true;
                break;
            case Key.Delete:
                ApplyAction(new DeleteForwardAction());
                e.Handled = true;
                break;
            case Key.Enter:
                ApplyAction(new InsertLineBreakAction());
                e.Handled = true;
                break;
            case Key.Tab:
                ApplyAction(new ReplaceSelectionWithTextAction(new string(' ', _state.Snapshot.Options.TabSize)));
                e.Handled = true;
                break;
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        ResetCaretBlink();
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    private bool HandleShortcutKey(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Z:
                    Undo();
                    return true;
                case Key.Y:
                    Redo();
                    return true;
                case Key.A:
                    SetSelection(0, TextLength);
                    return true;
                case Key.C:
                    CopySelection();
                    return true;
                case Key.X:
                    CutSelection();
                    return true;
                case Key.V:
                    PasteClipboard();
                    return true;
            }
        }

        if (Keyboard.Modifiers == ModifierKeys.Shift && e.Key == Key.Insert)
        {
            PasteClipboard();
            return true;
        }

        return false;
    }

    private void CopySelection()
    {
        if (SelectionLength <= 0)
        {
            return;
        }

        System.Windows.Clipboard.SetText(Text.Substring(SelectionStart, SelectionLength));
    }

    private void CutSelection()
    {
        if (IsReadOnly)
        {
            return;
        }

        CopySelection();
        ApplyAction(new ReplaceSelectionWithTextAction(string.Empty));
    }

    private void PasteClipboard()
    {
        if (IsReadOnly || !System.Windows.Clipboard.ContainsText())
        {
            return;
        }

        ApplyAction(new ReplaceSelectionWithTextAction(System.Windows.Clipboard.GetText()));
    }

    private void ApplyAction(IEditorAction action)
    {
        EnsureViewport(RenderSize);

        var previousText = Text;
        var previousSelection = _state.Snapshot.Selection;
        var previousUndo = CanUndo;
        var previousRedo = CanRedo;

        var result = _reducer.Reduce(_state, action);
        _state = result.State;

        ResetCaretBlink();
        InvalidateMeasure();
        InvalidateVisual();

        if (!string.Equals(previousText, Text, StringComparison.Ordinal))
        {
            TextContentChanged?.Invoke(this, EventArgs.Empty);
        }

        if (previousSelection != _state.Snapshot.Selection)
        {
            EditorSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        if (previousUndo != CanUndo || previousRedo != CanRedo || action is AuraMark.Core.Editing.UndoAction or AuraMark.Core.Editing.RedoAction)
        {
            HistoryStateChanged?.Invoke(this, EventArgs.Empty);
        }

        if (result.Effects.Count > 0)
        {
            BringDocumentRectIntoView(GetCaretRect(_state.Snapshot.Selection.Active.Offset));
        }
    }

    private void RefreshLayout()
    {
        EnsureViewport(RenderSize);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void RaiseSelectionAndHistoryChanged()
    {
        EditorSelectionChanged?.Invoke(this, EventArgs.Empty);
        HistoryStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureViewport(Size availableSize)
    {
        var viewport = CreateViewport(availableSize);
        if (Math.Abs(viewport.Width - _state.Viewport.Width) < 0.5 &&
            Math.Abs(viewport.Height - _state.Viewport.Height) < 0.5)
        {
            return;
        }

        var parse = _state.Parse ?? _parser.Parse(_state.Snapshot);
        var layout = _layoutEngine.Build(
            new LayoutBuildRequest(parse, viewport, new TextSpan(0, _state.Snapshot.Text.Length)),
            _state.Layout);

        _state = _state with
        {
            Parse = parse,
            Layout = layout.Document,
            Viewport = viewport,
        };
    }

    private ViewportState CreateViewport(Size availableSize)
    {
        var width = double.IsNaN(availableSize.Width) || double.IsInfinity(availableSize.Width)
            ? Math.Max(MinimumSurfaceWidth, ActualWidth - OuterPadding * 2)
            : availableSize.Width - OuterPadding * 2;
        var height = double.IsNaN(availableSize.Height) || double.IsInfinity(availableSize.Height)
            ? Math.Max(0, ActualHeight - OuterPadding * 2)
            : availableSize.Height - OuterPadding * 2;

        return new ViewportState(
            Math.Max(MinimumSurfaceWidth, width),
            Math.Max(0, height),
            FindScrollViewer()?.VerticalOffset ?? 0);
    }

    private Point TranslateToDocument(Point point)
    {
        var pageBounds = GetPageBounds();
        return new Point(
            Math.Max(0, point.X - pageBounds.X),
            Math.Max(0, point.Y - pageBounds.Y));
    }

    private Rect GetPageBounds()
    {
        var pageWidth = Math.Max(MinimumSurfaceWidth, _state.Viewport.Width);
        var x = Math.Max(OuterPadding, (RenderSize.Width - pageWidth) / 2);
        var height = Math.Max(RenderSize.Height - OuterPadding * 2, (_state.Layout?.TotalHeight ?? 0) + 24);
        return new Rect(x, OuterPadding, pageWidth, Math.Max(120, height));
    }

    private Rect GetCaretRect(int offset)
    {
        var pageBounds = GetPageBounds();
        var rect = _caretGeometryProvider.GetCaretRect(_state.Layout, new TextPosition(offset));
        return new Rect(rect.X + pageBounds.X, rect.Y + pageBounds.Y, rect.Width, rect.Height);
    }

    private void BringDocumentRectIntoView(Rect rect)
    {
        var scrollViewer = FindScrollViewer();
        if (scrollViewer is null || rect.IsEmpty)
        {
            return;
        }

        if (rect.Top < scrollViewer.VerticalOffset)
        {
            scrollViewer.ScrollToVerticalOffset(Math.Max(0, rect.Top - 24));
        }
        else if (rect.Bottom > scrollViewer.VerticalOffset + scrollViewer.ViewportHeight)
        {
            scrollViewer.ScrollToVerticalOffset(rect.Bottom - scrollViewer.ViewportHeight + 24);
        }
    }

    private ScrollViewer? FindScrollViewer()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void ResetCaretBlink()
    {
        _caretVisible = true;
        _caretBlinkTimer.Stop();
        _caretBlinkTimer.Start();
    }

    private void RenderBlock(DrawingContext drawingContext, LayoutBlock block)
    {
        switch (block)
        {
            case LayoutParagraphBlock paragraph:
                _paragraphRenderer.Render(drawingContext, paragraph);
                break;
            case LayoutHeadingBlock heading:
                _headingRenderer.Render(drawingContext, heading);
                break;
            case LayoutQuoteBlock quote:
                _quoteRenderer.Render(drawingContext, quote);
                foreach (var child in quote.Children)
                {
                    RenderBlock(drawingContext, child);
                }

                break;
            case LayoutListBlock list:
                _listRenderer.Render(drawingContext, list);
                foreach (var item in list.Items)
                {
                    foreach (var child in item.Children)
                    {
                        RenderBlock(drawingContext, child);
                    }
                }

                break;
            case LayoutCodeFenceBlock codeFence:
                _codeFenceRenderer.Render(drawingContext, codeFence);
                break;
        }
    }

    private static EditorVisualState CreateVisualState(SelectionRange selection) =>
        new(
            new CaretVisualState(selection.Active),
            new SelectionVisualState(selection));

    private static FormattedText CreateHintText(string text)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            14,
            EmptyStateBrush,
            1.0);
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
