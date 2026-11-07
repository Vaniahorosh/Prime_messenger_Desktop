using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace PrimeDesktop.Models;

public class PendingAttachment : INotifyPropertyChanged
{
    private PacketType _type = PacketType.File;
    public PacketType Type
    {
        get => _type;
        set { SetField(ref _type, value); OnPropertyChanged(nameof(HasImageThumbnail)); OnPropertyChanged(nameof(IsCollage)); }
    }

    private string? _primaryFilePath;
    public string? PrimaryFilePath
    {
        get => _primaryFilePath;
        set { SetField(ref _primaryFilePath, value); OnPropertyChanged(nameof(HasImageThumbnail)); }
    }

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    private string _subtitle = string.Empty;
    public string Subtitle
    {
        get => _subtitle;
        set => SetField(ref _subtitle, value);
    }

    private string _icon = "📎";
    public string Icon
    {
        get => _icon;
        set => SetField(ref _icon, value);
    }

    private List<string> _filePaths = new();
    public List<string> FilePaths
    {
        get => _filePaths;
        set { SetField(ref _filePaths, value); OnPropertyChanged(nameof(IsCollage)); }
    }

    private bool? _isCollage;
    public bool IsCollage
    {
        get => _isCollage ?? (Type == PacketType.Photo && FilePaths.Count > 1);
        set => SetField(ref _isCollage, value);
    }

    private bool? _hasImageThumbnail;
    public bool HasImageThumbnail
    {
        get => _hasImageThumbnail ?? ((Type == PacketType.Photo || IsImageFile(PrimaryFilePath)) && 
                                      !string.IsNullOrEmpty(PrimaryFilePath) && 
                                      File.Exists(PrimaryFilePath));
        set => SetField(ref _hasImageThumbnail, value);
    }

    private static bool IsImageFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif";
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} Б";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} КБ";
        return $"{(bytes / (1024.0 * 1024.0)):F1} МБ";
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
