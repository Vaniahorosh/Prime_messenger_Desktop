using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace PrimeDesktop.Models;

public enum DeviceConnectionState
{
    Disconnected,
    Connecting,
    Connected
}

public class ChatItem : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private string _username = string.Empty;
    private string _lastMessage = string.Empty;
    private string _lastMessageTime = string.Empty;
    private int _unreadCount;
    private bool _isOnline;
    private bool _isTyping;
    private string? _avatarPath;
    private DeviceConnectionState _connectionState = DeviceConnectionState.Disconnected;
    private string _peerId = string.Empty;
    private string _deviceAddress = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Opaque, anonymized peer identifier. Hardware MAC addresses are never stored here.
    /// </summary>
    public string PeerId
    {
        get => _peerId;
        set => SetField(ref _peerId, value);
    }

    /// <summary>
    /// Hardware device address or network IP used for physical connection.
    /// </summary>
    public string DeviceAddress
    {
        get => !string.IsNullOrEmpty(_deviceAddress) ? _deviceAddress : _peerId;
        set => SetField(ref _deviceAddress, value);
    }

    public bool IsBluetoothDevice { get; set; } = true;

    public string? AvatarPath
    {
        get => _avatarPath;
        set
        {
            if (SetField(ref _avatarPath, value))
            {
                OnPropertyChanged(nameof(HasAvatar));
            }
        }
    }

    public bool HasAvatar => !string.IsNullOrEmpty(_avatarPath) && File.Exists(_avatarPath);

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }

    public string LastMessage
    {
        get => _lastMessage;
        set => SetField(ref _lastMessage, value);
    }

    public string LastMessageTime
    {
        get => _lastMessageTime;
        set => SetField(ref _lastMessageTime, value);
    }

    public int UnreadCount
    {
        get => _unreadCount;
        set => SetField(ref _unreadCount, value);
    }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (SetField(ref _isOnline, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColorHex));
                OnPropertyChanged(nameof(IsOnlineIndicatorVisible));
            }
        }
    }

    public bool IsTyping
    {
        get => _isTyping;
        set
        {
            if (SetField(ref _isTyping, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColorHex));
            }
        }
    }

    public DeviceConnectionState ConnectionState
    {
        get => _connectionState;
        set
        {
            if (SetField(ref _connectionState, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColorHex));
                OnPropertyChanged(nameof(IsOnlineIndicatorVisible));
            }
        }
    }

    public string StatusText
    {
        get
        {
            if (IsTyping) return "Печатает...";
            return ConnectionState switch
            {
                DeviceConnectionState.Connected => "в сети",
                DeviceConnectionState.Connecting => "Установка связи...",
                _ => "не в сети"
            };
        }
    }

    public string StatusColorHex
    {
        get
        {
            if (IsTyping) return "#2EA043";
            return ConnectionState switch
            {
                DeviceConnectionState.Connected => "#2EA043", // Green
                DeviceConnectionState.Connecting => "#D29922", // Amber
                _ => "#8B949E" // Muted gray
            };
        }
    }

    public bool IsOnlineIndicatorVisible => ConnectionState == DeviceConnectionState.Connected;

    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DisplayName)) return "?";
            var parts = DisplayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}";
            return char.ToUpper(parts[0][0]).ToString();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(DisplayName)) OnPropertyChanged(nameof(Initials));
        return true;
    }
}
