using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PrimeDesktop.Models;

public class AppSettings : INotifyPropertyChanged
{
    private string _myUsername = "prime_user";
    private string _myDisplayName = Environment.UserName;
    private string? _myAvatarPath;
    private string? _chatWallpaperPath;
    private double _wallpaperOpacity = 0.35;
    private string _accentColorHex = "#4D9FFF";
    private bool _darkTheme = true;
    private string _themeMode = "system";
    private bool _enableAcrylic = true;
    private bool _enableAnimations = true;
    private int _avatarCornerRadiusPercent = 50;
    private int _bubbleRadius = 16;
    private bool _playNotificationSounds = true;
    private bool _playOutgoingSound = true;
    private bool _playIncomingSound = true;
    private bool _playVoiceRecordSound = true;
    private int _soundEffectsVolume = 80;
    private bool _showToastNotifications = true;
    private bool _showMessagePreviewInNotifications = true;
    private bool _minimizeToTray = true;
    private bool _enableScreenshotProtection = true;
    private bool _maskDeviceIdentifiers = true;
    private int _companionPort = 8989;
    private bool _autoConnectBluetooth = true;
    private string _searchEngine = "Google";
    private bool _useInternalBrowser = false;
    private bool _browserEnableJs = true;
    private bool _browserEnableCookies = true;
    private bool _browserDesktopMode = false;
    private int _videoNoteRadiusPercent = 50;
    private int _thanksHeartCount = 0;
    private List<string> _forgottenDevices = new();

    public string MyUsername
    {
        get => _myUsername;
        set => SetField(ref _myUsername, value);
    }

    public string MyDisplayName
    {
        get => _myDisplayName;
        set => SetField(ref _myDisplayName, value);
    }

    public string? MyAvatarPath
    {
        get => _myAvatarPath;
        set => SetField(ref _myAvatarPath, value);
    }

    public string? ChatWallpaperPath
    {
        get => _chatWallpaperPath;
        set
        {
            if (SetField(ref _chatWallpaperPath, value))
            {
                OnPropertyChanged(nameof(HasWallpaper));
            }
        }
    }

    public double WallpaperOpacity
    {
        get => _wallpaperOpacity;
        set => SetField(ref _wallpaperOpacity, value);
    }

    [JsonIgnore]
    public bool HasWallpaper => !string.IsNullOrEmpty(_chatWallpaperPath) && File.Exists(_chatWallpaperPath);

    public string AccentColorHex
    {
        get => _accentColorHex;
        set => SetField(ref _accentColorHex, value);
    }

        public string ThemeMode
    {
        get => _themeMode;
        set => SetField(ref _themeMode, value);
    }

    public bool DarkTheme
    {
        get => _darkTheme;
        set => SetField(ref _darkTheme, value);
    }

    public bool EnableAcrylic
    {
        get => _enableAcrylic;
        set => SetField(ref _enableAcrylic, value);
    }

    public bool EnableAnimations
    {
        get => _enableAnimations;
        set => SetField(ref _enableAnimations, value);
    }

    public int AvatarCornerRadiusPercent
    {
        get => _avatarCornerRadiusPercent;
        set => SetField(ref _avatarCornerRadiusPercent, value);
    }

    public int BubbleRadius
    {
        get => _bubbleRadius;
        set => SetField(ref _bubbleRadius, value);
    }

    public bool PlayNotificationSounds
    {
        get => _playNotificationSounds;
        set => SetField(ref _playNotificationSounds, value);
    }

    public bool PlayOutgoingSound
    {
        get => _playOutgoingSound;
        set => SetField(ref _playOutgoingSound, value);
    }

    public bool PlayIncomingSound
    {
        get => _playIncomingSound;
        set => SetField(ref _playIncomingSound, value);
    }

    public bool PlayVoiceRecordSound
    {
        get => _playVoiceRecordSound;
        set => SetField(ref _playVoiceRecordSound, value);
    }

    public int SoundEffectsVolume
    {
        get => _soundEffectsVolume;
        set => SetField(ref _soundEffectsVolume, Math.Clamp(value, 0, 100));
    }

    public bool ShowToastNotifications
    {
        get => _showToastNotifications;
        set => SetField(ref _showToastNotifications, value);
    }

    public bool ShowMessagePreviewInNotifications
    {
        get => _showMessagePreviewInNotifications;
        set => SetField(ref _showMessagePreviewInNotifications, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetField(ref _minimizeToTray, value);
    }

    public bool EnableScreenshotProtection
    {
        get => _enableScreenshotProtection;
        set => SetField(ref _enableScreenshotProtection, value);
    }

    public bool MaskDeviceIdentifiers
    {
        get => _maskDeviceIdentifiers;
        set => SetField(ref _maskDeviceIdentifiers, value);
    }

    public int CompanionPort
    {
        get => _companionPort;
        set => SetField(ref _companionPort, value);
    }

    public bool AutoConnectBluetooth
    {
        get => _autoConnectBluetooth;
        set => SetField(ref _autoConnectBluetooth, value);
    }

    public string SearchEngine
    {
        get => _searchEngine;
        set => SetField(ref _searchEngine, value);
    }

    public bool UseInternalBrowser
    {
        get => _useInternalBrowser;
        set => SetField(ref _useInternalBrowser, value);
    }

    public bool BrowserEnableJs
    {
        get => _browserEnableJs;
        set => SetField(ref _browserEnableJs, value);
    }

    public bool BrowserEnableCookies
    {
        get => _browserEnableCookies;
        set => SetField(ref _browserEnableCookies, value);
    }

    public bool BrowserDesktopMode
    {
        get => _browserDesktopMode;
        set => SetField(ref _browserDesktopMode, value);
    }

    public int VideoNoteRadiusPercent
    {
        get => _videoNoteRadiusPercent;
        set => SetField(ref _videoNoteRadiusPercent, value);
    }

    public int ThanksHeartCount
    {
        get => _thanksHeartCount;
        set => SetField(ref _thanksHeartCount, value);
    }

    public List<string> ForgottenDevices
    {
        get => _forgottenDevices;
        set => SetField(ref _forgottenDevices, value ?? new());
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
