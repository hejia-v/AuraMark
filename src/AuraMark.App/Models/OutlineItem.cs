using System.ComponentModel;

namespace AuraMark.App.Models;

public sealed class OutlineItem : INotifyPropertyChanged
{
    private bool _isActive;

    public int Index { get; init; }

    public int Level { get; init; }

    public string Text { get; init; } = string.Empty;

    public string DisplayText { get; init; } = string.Empty;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
