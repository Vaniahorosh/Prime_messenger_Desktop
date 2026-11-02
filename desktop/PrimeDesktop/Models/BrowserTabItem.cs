using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace PrimeDesktop.Models;

public class BrowserTabItem : INotifyPropertyChanged
{
    private string _title = "Новая вкладка";
    private string _url = "about:blank";
    private bool _isLoading;
    private ImageSource? _favicon;
    private bool _isSecure = true;
    private bool _isActive;
    private bool _isPinned;
    private ImageSource? _thumbnailPreview;
    private string _ramUsage = "";

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ImageSource? Favicon
    {
        get => _favicon;
        set => SetProperty(ref _favicon, value);
    }

    public bool IsSecure
    {
        get => _isSecure;
        set => SetProperty(ref _isSecure, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    public ImageSource? ThumbnailPreview
    {
        get => _thumbnailPreview;
        set => SetProperty(ref _thumbnailPreview, value);
    }

    public string RamUsage
    {
        get => _ramUsage;
        set => SetProperty(ref _ramUsage, value);
    }

    public WebView2? WebView { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
        storage = value;
        OnPropertyChanged(name);
        return true;
    }
}
