using System.Windows.Threading;

namespace AuraMark.Wpf.Surface;

public sealed class CaretBlinkController : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Action _toggle;
    private bool _disposed;

    public CaretBlinkController(Action toggle)
    {
        _toggle = toggle;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(530) };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
    public void ResetVisible() { Stop(); Start(); }
    private void OnTick(object? sender, EventArgs e) => _toggle();
    public void Dispose() { if (_disposed) return; _disposed = true; _timer.Stop(); _timer.Tick -= OnTick; }
}
