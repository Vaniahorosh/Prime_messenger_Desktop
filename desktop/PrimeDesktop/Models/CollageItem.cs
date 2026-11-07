using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrimeDesktop.Models;

public class CollageItem : INotifyPropertyChanged
{
    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set => SetField(ref _filePath, value);
    }

    private int _index;
    public int Index
    {
        get => _index;
        set => SetField(ref _index, value);
    }

    private int _totalCount;
    public int TotalCount
    {
        get => _totalCount;
        set => SetField(ref _totalCount, value);
    }

    private bool _isVideo;
    public bool IsVideo
    {
        get => _isVideo;
        set => SetField(ref _isVideo, value);
    }

    private string _duration = string.Empty;
    public string Duration
    {
        get => _duration;
        set => SetField(ref _duration, value);
    }

    private bool _hasRemainingOverlay;
    public bool HasRemainingOverlay
    {
        get => _hasRemainingOverlay;
        set => SetField(ref _hasRemainingOverlay, value);
    }

    private string _remainingText = string.Empty;
    public string RemainingText
    {
        get => _remainingText;
        set => SetField(ref _remainingText, value);
    }

    private int _column;
    public int Column
    {
        get => _column;
        set => SetField(ref _column, value);
    }

    private int _row;
    public int Row
    {
        get => _row;
        set => SetField(ref _row, value);
    }

    private int _columnSpan = 1;
    public int ColumnSpan
    {
        get => _columnSpan;
        set => SetField(ref _columnSpan, value);
    }

    private int _rowSpan = 1;
    public int RowSpan
    {
        get => _rowSpan;
        set => SetField(ref _rowSpan, value);
    }

    private double _itemHeight = 120.0;
    public double ItemHeight
    {
        get => _itemHeight;
        set => SetField(ref _itemHeight, value);
    }

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
