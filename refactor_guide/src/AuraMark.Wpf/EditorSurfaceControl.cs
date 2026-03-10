using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AuraMark.Core.Editing;
using AuraMark.Core.Text;
using AuraMark.Wpf.Surface;
using AuraMark.Wpf.Surface.Input;
using AuraMark.Wpf.Surface.Rendering;

namespace AuraMark.Wpf;

public sealed class EditorSurfaceControl : FrameworkElement, IDisposable
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(EditorState), typeof(EditorSurfaceControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnStateChanged));

    private readonly ParagraphRenderer _paragraphRenderer = new();
    private readonly HeadingRenderer _headingRenderer = new();
    private readonly CodeFenceRenderer _codeFenceRenderer = new();
    private readonly SelectionRenderer _selectionRenderer = new(new SolidColorBrush(Color.FromArgb(90, 80, 140, 255)));
    private readonly CaretRenderer _caretRenderer = new(Brushes.Black);
    private readonly ISelectionGeometryProvider _selectionGeometry = new SelectionGeometryProvider();
    private readonly ICaretGeometryProvider _caretGeometry = new CaretGeometryProvider();
    private readonly IEditorHitTestService _hitTest = new EditorHitTestService();
    private readonly CaretBlinkController _blink;
    private bool _caretVisible = true;
    private bool _isDraggingSelection;

    public EditorSurfaceControl()
    {
        Focusable = true;
        _blink = new CaretBlinkController(() => { _caretVisible = !_caretVisible; InvalidateVisual(); });
        Loaded += (_, _) => _blink.Start();
        Unloaded += (_, _) => _blink.Stop();
    }

    public EditorState? State { get => (EditorState?)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public IEditorDispatcher? Dispatcher { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var state = State; var layout = state?.Layout; if (layout is null) return;
        foreach (var block in layout.Blocks)
        {
            switch (block)
            {
                case AuraMark.Core.Layout.LayoutParagraphBlock p: _paragraphRenderer.RenderParagraph(dc, p); break;
                case AuraMark.Core.Layout.LayoutHeadingBlock h: _headingRenderer.RenderHeading(dc, h); break;
                case AuraMark.Core.Layout.LayoutCodeFenceBlock c: _codeFenceRenderer.RenderCodeFence(dc, c); break;
            }
        }
        var selection = state.Snapshot.Selection;
        if (!selection.IsCollapsed)
        {
            var rects = _selectionGeometry.GetSelectionRects(layout, selection.AsTextSpan());
            _selectionRenderer.Render(dc, rects);
        }
        if (IsFocused && selection.IsCollapsed)
        {
            var caretRect = _caretGeometry.GetCaretRect(layout, selection.Active);
            _caretRenderer.Render(dc, caretRect, _caretVisible);
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e); Focus(); CaptureMouse(); _isDraggingSelection = true;
        var state = State; var layout = state?.Layout; if (layout is null || Dispatcher is null) return;
        var pos = _hitTest.HitTest(layout, e.GetPosition(this));
        Dispatcher.Dispatch(new SetSelectionAction(SelectionRange.Collapsed(pos)));
        ResetCaretBlink();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (!_isDraggingSelection || Dispatcher is null) return;
        var state = State; var layout = state?.Layout; if (layout is null) return;
        var active = _hitTest.HitTest(layout, e.GetPosition(this));
        var anchor = state.Snapshot.Selection.Anchor;
        Dispatcher.Dispatch(new SetSelectionAction(new SelectionRange(anchor, active)));
        ResetCaretBlink();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e) { base.OnMouseUp(e); _isDraggingSelection = false; ReleaseMouseCapture(); }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e); if (Dispatcher is null || string.IsNullOrEmpty(e.Text)) return;
        Dispatcher.Dispatch(new ReplaceSelectionWithTextAction(e.Text)); ResetCaretBlink(); e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e); if (Dispatcher is null) return;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift); bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.Z) { Dispatcher.Dispatch(new UndoAction()); e.Handled = true; }
        else if (ctrl && (e.Key == Key.Y || (shift && e.Key == Key.Z))) { Dispatcher.Dispatch(new RedoAction()); e.Handled = true; }
        else
        {
            switch (e.Key)
            {
                case Key.Left: Dispatcher.Dispatch(new MoveCaretAction(CaretMoveKind.Left, shift)); e.Handled = true; break;
                case Key.Right: Dispatcher.Dispatch(new MoveCaretAction(CaretMoveKind.Right, shift)); e.Handled = true; break;
                case Key.Up: Dispatcher.Dispatch(new MoveCaretAction(CaretMoveKind.Up, shift)); e.Handled = true; break;
                case Key.Down: Dispatcher.Dispatch(new MoveCaretAction(CaretMoveKind.Down, shift)); e.Handled = true; break;
                case Key.Home: Dispatcher.Dispatch(new MoveCaretAction(CaretMoveKind.LineStart, shift)); e.Handled = true; break;
                case Key.End: Dispatcher.Dispatch(new MoveCaretAction(CaretMoveKind.LineEnd, shift)); e.Handled = true; break;
                case Key.Back: Dispatcher.Dispatch(new DeleteBackwardAction()); e.Handled = true; break;
                case Key.Delete: Dispatcher.Dispatch(new DeleteForwardAction()); e.Handled = true; break;
                case Key.Enter: Dispatcher.Dispatch(new InsertLineBreakAction()); e.Handled = true; break;
            }
        }
        if (e.Handled) ResetCaretBlink();
    }

    private void ResetCaretBlink() { _caretVisible = true; _blink.ResetVisible(); InvalidateVisual(); }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { if (d is EditorSurfaceControl control) control.InvalidateVisual(); }
    public void Dispose() => _blink.Dispose();
}
