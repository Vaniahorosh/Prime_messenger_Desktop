using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrimeDesktop.Models;

public class ReactionGroup : INotifyPropertyChanged
{
    private string _emoji = string.Empty;
    public string Emoji
    {
        get => _emoji;
        set => SetField(ref _emoji, value);
    }

    private int _count;
    public int Count
    {
        get => _count;
        set => SetField(ref _count, value);
    }

    private bool _isSelectedByMe;
    public bool IsSelectedByMe
    {
        get => _isSelectedByMe;
        set => SetField(ref _isSelectedByMe, value);
    }

    private string _tooltip = string.Empty;
    public string Tooltip
    {
        get => _tooltip;
        set => SetField(ref _tooltip, value);
    }

    public ObservableCollection<string> Authors { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
