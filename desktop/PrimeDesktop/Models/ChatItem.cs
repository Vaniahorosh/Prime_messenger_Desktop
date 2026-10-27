using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

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
    private bool _isMuted;
    private int _currentAvatarIndex;
    private ObservableCollection<string> _avatarHistory = new();

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
                if (!string.IsNullOrEmpty(value) && !_avatarHistory.Contains(value))
                {
                    _avatarHistory.Insert(0, value);
                }
                OnPropertyChanged(nameof(HasAvatar));
                OnPropertyChanged(nameof(CurrentAvatarDisplayPath));
                OnPropertyChanged(nameof(AvatarCounterText));
                OnPropertyChanged(nameof(HasMultipleAvatars));
                OnPropertyChanged(nameof(CanGoPreviousAvatar));
                OnPropertyChanged(nameof(CanGoNextAvatar));
            }
        }
    }

    public ObservableCollection<string> AvatarHistory
    {
        get => _avatarHistory;
        set
        {
            if (SetField(ref _avatarHistory, value))
            {
                OnPropertyChanged(nameof(CurrentAvatarDisplayPath));
                OnPropertyChanged(nameof(AvatarCounterText));
                OnPropertyChanged(nameof(HasMultipleAvatars));
                OnPropertyChanged(nameof(CanGoPreviousAvatar));
                OnPropertyChanged(nameof(CanGoNextAvatar));
                OnPropertyChanged(nameof(HasAvatar));
            }
        }
    }

    public int CurrentAvatarIndex
    {
        get => _currentAvatarIndex;
        set
        {
            if (SetField(ref _currentAvatarIndex, value))
            {
                OnPropertyChanged(nameof(CurrentAvatarDisplayPath));
                OnPropertyChanged(nameof(AvatarCounterText));
                OnPropertyChanged(nameof(CanGoPreviousAvatar));
                OnPropertyChanged(nameof(CanGoNextAvatar));
            }
        }
    }

    [JsonIgnore]
    public string? CurrentAvatarDisplayPath
    {
        get
        {
            if (_avatarHistory != null && _avatarHistory.Count > 0 && _currentAvatarIndex >= 0 && _currentAvatarIndex < _avatarHistory.Count)
            {
                return _avatarHistory[_currentAvatarIndex];
            }
            return _avatarPath;
        }
    }

    [JsonIgnore]
    public string AvatarCounterText
    {
        get
        {
            int count = _avatarHistory?.Count ?? 0;
            if (count == 0) return HasAvatar ? "1/1" : "0/0";
            return $"{_currentAvatarIndex + 1}/{count}";
        }
    }

    [JsonIgnore]
    public bool HasMultipleAvatars => (_avatarHistory?.Count ?? 0) > 1;

    [JsonIgnore]
    public bool CanGoPreviousAvatar => _currentAvatarIndex > 0;

    [JsonIgnore]
    public bool CanGoNextAvatar => _avatarHistory != null && _currentAvatarIndex < _avatarHistory.Count - 1;

    [JsonIgnore]
    public bool HasAvatar => (!string.IsNullOrEmpty(CurrentAvatarDisplayPath) && File.Exists(CurrentAvatarDisplayPath)) ||
                             (!string.IsNullOrEmpty(_avatarPath) && File.Exists(_avatarPath));

    public void AddAvatarToHistory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (_avatarHistory.Contains(path))
        {
            _avatarHistory.Remove(path);
        }
        _avatarHistory.Insert(0, path);
        _avatarPath = path;
        _currentAvatarIndex = 0;
        OnPropertyChanged(nameof(AvatarPath));
        OnPropertyChanged(nameof(CurrentAvatarDisplayPath));
        OnPropertyChanged(nameof(AvatarCounterText));
        OnPropertyChanged(nameof(HasMultipleAvatars));
        OnPropertyChanged(nameof(CanGoPreviousAvatar));
        OnPropertyChanged(nameof(CanGoNextAvatar));
        OnPropertyChanged(nameof(HasAvatar));
    }

    public void NextAvatar()
    {
        if (CanGoNextAvatar)
        {
            CurrentAvatarIndex++;
        }
    }

    public void PrevAvatar()
    {
        if (CanGoPreviousAvatar)
        {
            CurrentAvatarIndex--;
        }
    }

    public void RemoveCurrentAvatar()
    {
        if (_avatarHistory != null && _avatarHistory.Count > 0 && _currentAvatarIndex >= 0 && _currentAvatarIndex < _avatarHistory.Count)
        {
            string toDelete = _avatarHistory[_currentAvatarIndex];
            _avatarHistory.RemoveAt(_currentAvatarIndex);
            try { File.Delete(toDelete); } catch { }

            if (_currentAvatarIndex >= _avatarHistory.Count)
            {
                _currentAvatarIndex = Math.Max(0, _avatarHistory.Count - 1);
            }
            _avatarPath = _avatarHistory.Count > 0 ? _avatarHistory[_currentAvatarIndex] : null;
            OnPropertyChanged(nameof(AvatarPath));
            OnPropertyChanged(nameof(CurrentAvatarDisplayPath));
            OnPropertyChanged(nameof(AvatarCounterText));
            OnPropertyChanged(nameof(HasMultipleAvatars));
            OnPropertyChanged(nameof(CanGoPreviousAvatar));
            OnPropertyChanged(nameof(CanGoNextAvatar));
            OnPropertyChanged(nameof(HasAvatar));
        }
    }

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

    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

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
