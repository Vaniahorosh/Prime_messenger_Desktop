using System;

using System.Collections.ObjectModel;

using System.IO;
using System.Net.NetworkInformation;

using System.Linq;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Input;

using Microsoft.Win32;

using PrimeDesktop.Models;

using PrimeDesktop.Network;

using PrimeDesktop.Services;
using PrimeDesktop.Views;

namespace PrimeDesktop.ViewModels;

public class MainViewModel : ViewModelBase

{
    private readonly StorageService _storageService = new();

    private readonly AudioService _audioService = new();

    private readonly BluetoothRfcommService _bluetoothService = new();
    private readonly CompanionBridgeService _companionService = new();

    private ObservableCollection<ChatItem> _allChats = new();

    private ObservableCollection<ChatItem> _filteredChats = new();

    private ChatItem? _selectedChat;

    private string _searchQuery = string.Empty;
    private bool _isSearchActive;

    private string _inputMessageText = string.Empty;

    private ChatMessage? _replyMessage;

    private bool _isSettingsOpen;

    private bool _isMediaDrawerOpen;

    private bool _isScanning;

    private string _statusMessage = "Ready";

    private AppSettings _settings = new();

    private readonly Dictionary<string, System.Windows.Threading.DispatcherTimer> _chatActivityTimers = new();
    private string _myCurrentActivityState = "IDLE";
    private DateTime _lastActivitySentTime = DateTime.MinValue;
    private System.Windows.Threading.DispatcherTimer? _myActivityIdleTimer;

    public ObservableCollection<ChatItem> FilteredChats

    {
        get => _filteredChats;

        set => SetProperty(ref _filteredChats, value);

    }

    public ChatItem? SelectedChat

    {
        get => _selectedChat;
        set
        {
            if (SetProperty(ref _selectedChat, value))
            {
                _audioService.Stop();
                if (_selectedChat != null)
                {
                    IsReadingMode = false;

                    // Ensure avatar history is populated and deduplicated
                    if (_selectedChat.AvatarHistory.Count == 0 && !string.IsNullOrEmpty(_selectedChat.AvatarPath) && File.Exists(_selectedChat.AvatarPath))
                    {
                        _selectedChat.AvatarHistory.Add(_selectedChat.AvatarPath);
                    }
                    var diskAvatars = new List<string>();
                    if (!string.IsNullOrEmpty(_selectedChat.DeviceAddress))
                        diskAvatars.AddRange(AvatarManager.GetAvatarHistory(_selectedChat.DeviceAddress));
                    if (!string.IsNullOrEmpty(_selectedChat.PeerId))
                        diskAvatars.AddRange(AvatarManager.GetAvatarHistory(_selectedChat.PeerId));
                    if (!string.IsNullOrEmpty(_selectedChat.Username))
                        diskAvatars.AddRange(AvatarManager.GetAvatarHistory(_selectedChat.Username));

                    foreach (var a in diskAvatars)
                    {
                        if (!_selectedChat.AvatarHistory.Contains(a))
                        {
                            _selectedChat.AvatarHistory.Add(a);
                        }
                    }
                    _selectedChat.DeduplicateAvatarHistory();
                    _selectedChat.UpdateAvatarSegments();
                    _selectedChat.CurrentAvatarIndex = 0;

                    OnPropertyChanged(nameof(SharedMedia));
                    OnPropertyChanged(nameof(SharedMediaList));
                    OnPropertyChanged(nameof(SharedFiles));
                    OnPropertyChanged(nameof(SharedFileList));
                    OnPropertyChanged(nameof(SharedAudio));

                    _ = _storageService.SaveChatsAsync(_allChats);

                    if (Settings != null && Settings.AutoConnectBluetooth && _selectedChat.IsBluetoothDevice && 
                        !_selectedChat.IsOnline && _selectedChat.ConnectionState != DeviceConnectionState.Connecting &&
                        !_selectedChat.PeerId.StartsWith("peer_prime"))
                    {
                        _ = ConnectSelectedChatAsync();
                    }
                }
                else
                {
                    IsMediaDrawerOpen = false;
                }
                OnPropertyChanged(nameof(HasSelectedChat));
                CommandManager.InvalidateRequerySuggested();
            }
        }

    }

    public bool HasSelectedChat => SelectedChat != null;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplySearchFilter();
            }
        }
    }

    public bool IsSearchActive
    {
        get => _isSearchActive;
        set
        {
            if (SetProperty(ref _isSearchActive, value))
            {
                if (!value)
                {
                    SearchQuery = string.Empty;
                }
            }
        }
    }

    public bool HasInputText => !string.IsNullOrWhiteSpace(_inputMessageText);

    public string InputMessageText
    {
        get => _inputMessageText;
        set
        {
            if (SetProperty(ref _inputMessageText, value))
            {
                OnPropertyChanged(nameof(HasInputText));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    NotifyTyping();
                }
                else
                {
                    SendMyActivityState("IDLE", true);
                }
            }
        }
    }

    public ChatMessage? ReplyMessage

    {
        get => _replyMessage;

        set

        {
            if (SetProperty(ref _replyMessage, value))

            {
                OnPropertyChanged(nameof(HasReply));

            }

        }

    }

    public bool HasReply => ReplyMessage != null;

    public bool IsSettingsOpen

    {
        get => _isSettingsOpen;

        set => SetProperty(ref _isSettingsOpen, value);

    }

    public bool IsMediaDrawerOpen

    {
        get => _isMediaDrawerOpen;

        set => SetProperty(ref _isMediaDrawerOpen, value);

    }

    public bool IsScanning

    {
        get => _isScanning;

        set => SetProperty(ref _isScanning, value);

    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, SanitizeStatusText(value));
    }

    public string SanitizeStatusText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return DeviceAnonymizer.MaskMacAddresses(text, id =>
        {
            var chat = FindChatByAddressOrPeer(id);
            if (chat != null && !DeviceAnonymizer.IsHardwareIdentifier(chat.DisplayName))
            {
                return chat.DisplayName;
            }
            return null;
        });
    }

    public ChatItem? FindChatByAddressOrPeer(string? addressOrPeer, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(addressOrPeer) && string.IsNullOrWhiteSpace(displayName))
            return null;

        string norm = !string.IsNullOrWhiteSpace(addressOrPeer)
            ? BluetoothRfcommService.NormalizeMacAddress(addressOrPeer)
            : string.Empty;

        string resolvedHw = !string.IsNullOrWhiteSpace(addressOrPeer)
            ? _bluetoothService.ResolveHardwareAddress(addressOrPeer)
            : string.Empty;
        string normResolvedHw = !string.IsNullOrWhiteSpace(resolvedHw)
            ? BluetoothRfcommService.NormalizeMacAddress(resolvedHw)
            : string.Empty;

        string resolvedPeer = !string.IsNullOrWhiteSpace(addressOrPeer)
            ? _bluetoothService.ResolvePeerId(addressOrPeer)
            : string.Empty;

        foreach (var chat in _allChats)
        {
            // 1. Exact PeerId match
            if (!string.IsNullOrWhiteSpace(addressOrPeer) &&
                chat.PeerId.Equals(addressOrPeer, StringComparison.OrdinalIgnoreCase))
                return chat;

            // 2. Exact DeviceAddress match
            if (!string.IsNullOrWhiteSpace(addressOrPeer) &&
                !string.IsNullOrWhiteSpace(chat.DeviceAddress) &&
                chat.DeviceAddress.Equals(addressOrPeer, StringComparison.OrdinalIgnoreCase))
                return chat;

            // 3. Resolved PeerId match
            if (!string.IsNullOrWhiteSpace(resolvedPeer) &&
                chat.PeerId.Equals(resolvedPeer, StringComparison.OrdinalIgnoreCase))
                return chat;

            // 4. Normalized MAC match with chat.DeviceAddress or chat.PeerId
            if (!string.IsNullOrWhiteSpace(norm))
            {
                if (!string.IsNullOrWhiteSpace(chat.DeviceAddress) &&
                    BluetoothRfcommService.NormalizeMacAddress(chat.DeviceAddress).Equals(norm, StringComparison.OrdinalIgnoreCase))
                    return chat;

                if (BluetoothRfcommService.NormalizeMacAddress(chat.PeerId).Equals(norm, StringComparison.OrdinalIgnoreCase))
                    return chat;
            }

            // 5. Resolved hardware address match
            if (!string.IsNullOrWhiteSpace(normResolvedHw))
            {
                if (!string.IsNullOrWhiteSpace(chat.DeviceAddress) &&
                    BluetoothRfcommService.NormalizeMacAddress(chat.DeviceAddress).Equals(normResolvedHw, StringComparison.OrdinalIgnoreCase))
                    return chat;

                string chatResolvedHw = BluetoothRfcommService.NormalizeMacAddress(_bluetoothService.ResolveHardwareAddress(chat.PeerId));
                if (!string.IsNullOrWhiteSpace(chatResolvedHw) && chatResolvedHw.Equals(normResolvedHw, StringComparison.OrdinalIgnoreCase))
                    return chat;
            }

            // 6. Non-generic display name match
            if (!string.IsNullOrWhiteSpace(displayName) &&
                !IsStatusKeyword(displayName) &&
                !displayName.StartsWith("Собеседник") &&
                chat.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase))
            {
                return chat;
            }
        }

        return null;
    }

    private void MergeChats(ChatItem target, ChatItem source)
    {
        if (target == null || source == null || ReferenceEquals(target, source)) return;

        foreach (var msg in source.Messages)
        {
            if (!target.Messages.Any(m => m.Id == msg.Id || 
                (!string.IsNullOrEmpty(m.Text) && m.Text == msg.Text && m.FormattedTime == msg.FormattedTime && m.IsFromMe == msg.IsFromMe)))
            {
                target.Messages.Add(msg);
            }
        }

        if (!string.IsNullOrWhiteSpace(source.DeviceAddress) && (string.IsNullOrWhiteSpace(target.DeviceAddress) || target.DeviceAddress == target.PeerId))
        {
            target.DeviceAddress = source.DeviceAddress;
        }

        if (!string.IsNullOrWhiteSpace(target.DeviceAddress) && !string.IsNullOrWhiteSpace(target.PeerId))
        {
            _bluetoothService.RegisterPeerAddress(target.PeerId, target.DeviceAddress);
        }

        if (!target.HasAvatar && source.HasAvatar)
        {
            target.AvatarPath = source.AvatarPath;
        }

        if ((DeviceAnonymizer.IsHardwareIdentifier(target.DisplayName) || target.DisplayName.StartsWith("Собеседник")) &&
            !string.IsNullOrWhiteSpace(source.DisplayName) && !DeviceAnonymizer.IsHardwareIdentifier(source.DisplayName) && !source.DisplayName.StartsWith("Собеседник"))
        {
            target.DisplayName = source.DisplayName;
        }

        if (string.IsNullOrWhiteSpace(target.LastMessage) && !string.IsNullOrWhiteSpace(source.LastMessage))
        {
            target.LastMessage = source.LastMessage;
            target.LastMessageTime = source.LastMessageTime;
        }
    }

    public AppSettings Settings
    {
        get => _settings;
        set
        {
            if (SetProperty(ref _settings, value))
            {
                if (_settings != null)
                {
                    _settings.PropertyChanged += async (s, e) =>
                    {
                        CheckForPendingSettingsChanges();
                        if (e.PropertyName == nameof(AppSettings.AvatarCornerRadiusPercent))
                        {
                            OnPropertyChanged(nameof(CanResetAvatarRadius));
                        }
                        else if (e.PropertyName == nameof(AppSettings.BubbleRadius))
                        {
                            OnPropertyChanged(nameof(CanResetBubbleRadius));
                        }
                        else if (e.PropertyName == nameof(AppSettings.WallpaperOpacity))
                        {
                            OnPropertyChanged(nameof(CanResetWallpaperOpacity));
                        }
                        else if (e.PropertyName == nameof(AppSettings.VideoNoteRadiusPercent))
                        {
                            OnPropertyChanged(nameof(CanResetVideoNoteRadius));
                        }
                        else if (e.PropertyName == nameof(AppSettings.SoundEffectsVolume))
                        {
                            OnPropertyChanged(nameof(CanResetSoundVolume));
                        }
                        else if (e.PropertyName == nameof(AppSettings.AccentColorHex))
                        {
                            OnPropertyChanged(nameof(CanResetAccentColor));
                        }
                        else if (e.PropertyName == nameof(AppSettings.ChatWallpaperPath))
                        {
                            OnPropertyChanged(nameof(CanResetWallpaper));
                        }
                        try { await _storageService.SaveSettingsAsync(_settings); } catch {}
                    };
                }
            }
        }
    }

    private string _initialSettingsSnapshot = "";
    private bool _hasPendingSettingsChanges;
    public bool HasPendingSettingsChanges
    {
        get => _hasPendingSettingsChanges;
        set => SetProperty(ref _hasPendingSettingsChanges, value);
    }

    public void CaptureSettingsSnapshot()
    {
        _initialSettingsSnapshot = GetSettingsSnapshot();
        HasPendingSettingsChanges = false;
    }

    public async Task ApplyAndSaveSettingsAsync()
    {
        try
        {
            await _storageService.SaveSettingsAsync(Settings);

            // Apply Theme & Accent Color & Acrylic DWM
            ThemeManager.ApplyTheme(Settings.ThemeMode, Settings.AccentColorHex);

            // Apply Screenshot Protection to all open windows
            if (Application.Current != null)
            {
                foreach (Window win in Application.Current.Windows)
                {
                    SecurityManager.SetScreenshotProtection(win, Settings.EnableScreenshotProtection);
                }
            }

            // Refresh UI bindings and corner radiuses
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(CanResetAvatarRadius));
            OnPropertyChanged(nameof(CanResetBubbleRadius));
            OnPropertyChanged(nameof(CanResetWallpaperOpacity));
            OnPropertyChanged(nameof(CanResetVideoNoteRadius));
            OnPropertyChanged(nameof(CanResetSoundVolume));
            OnPropertyChanged(nameof(CanResetAccentColor));
            OnPropertyChanged(nameof(CanResetWallpaper));
            OnPropertyChanged(nameof(BubbleCornerRadius));
            OnPropertyChanged(nameof(AvatarCornerRadius));
            OnPropertyChanged(nameof(HeaderAvatarCornerRadius));
            OnPropertyChanged(nameof(DrawerAvatarCornerRadius));
            OnPropertyChanged(nameof(PreviewAvatarCornerRadius));
            OnPropertyChanged(nameof(VideoNoteCornerRadius));
            OnPropertyChanged(nameof(VideoNoteChatCornerRadius));
            OnPropertyChanged(nameof(MyDisplayName));
            OnPropertyChanged(nameof(MyUsername));
            OnPropertyChanged(nameof(IsDarkThemeSelected));
            OnPropertyChanged(nameof(IsLightThemeSelected));
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(LaunchAtStartup));

            CaptureSettingsSnapshot();
            _ = Task.Run(async () =>
            {
                try { await BroadcastMyProfileAndAvatarAsync(); } catch { }
            });

            StatusMessage = "Настройки успешно сохранены и применены";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сохранения настроек: {ex.Message}";
        }
    }

    public void CheckForPendingSettingsChanges()
    {
        string current = GetSettingsSnapshot();
        HasPendingSettingsChanges = !string.Equals(_initialSettingsSnapshot, current, StringComparison.Ordinal);
    }

    private string GetSettingsSnapshot()
    {
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(_settings);
        }
        catch
        {
            return "";
        }
    }

    // Commands

    public ICommand SendMessageCommand { get; }

    public ICommand AttachFileCommand { get; }

    public ICommand AttachPhotoCommand { get; }
    public ICommand AttachGifCommand { get; }

    private bool _isAttachmentTrayOpen;
    public bool IsAttachmentTrayOpen
    {
        get => _isAttachmentTrayOpen;
        set => SetProperty(ref _isAttachmentTrayOpen, value);
    }

    public ICommand CancelReplyCommand { get; }

    public ICommand ReplyToMessageCommand { get; }

    public ICommand CopyMessageCommand { get; }

    public ICommand DeleteMessageCommand { get; }

    public ICommand ScanDevicesCommand { get; }

    public ICommand ConnectChatCommand { get; }

    public ICommand DisconnectChatCommand { get; }

    private bool _isReadingMode;
    public bool IsReadingMode
    {
        get => _isReadingMode;
        set
        {
            if (SetProperty(ref _isReadingMode, value))
            {
                OnPropertyChanged(nameof(ReadingModeButtonText));
                OnPropertyChanged(nameof(ReadingModeButtonIcon));
                OnPropertyChanged(nameof(ReadingModeToolTip));
            }
        }
    }
    public string ReadingModeButtonText => IsReadingMode ? "Соединиться" : "Чтение";
    public string ReadingModeButtonIcon => IsReadingMode ? "🔗" : "📖";
    public string ReadingModeToolTip => IsReadingMode ? "Восстановить соединение с собеседником" : "Режим чтения (принудительно отключить связь)";
    public ICommand ToggleReadingModeCommand { get; }

    public ICommand ToggleSettingsCommand { get; }
    public ICommand OpenSearchCommand { get; }
    public ICommand CloseSearchCommand { get; }
    public ICommand ToggleSearchCommand { get; }

    public ICommand ToggleMediaDrawerCommand { get; }

    public ICommand ConnectCompanionWifiCommand { get; }

    public ICommand ChangeAccentColorCommand { get; }

    public ICommand ChangeContactAvatarCommand { get; }

    public ICommand ChangeMyAvatarCommand { get; }
    public ICommand SelectPresetAvatarCommand { get; }
    public ICommand ResetMyAvatarCommand { get; }
    public ICommand BroadcastProfileCommand { get; }
    public ICommand ClearMediaCacheCommand { get; }
    public ICommand ClearAllChatsCommand { get; }
    public ICommand OpenStorageFolderCommand { get; }
    public ICommand ToggleThemeCommand { get; }

    private string _mediaCacheSizeText = "0 КБ";
    public string MediaCacheSizeText
    {
        get => _mediaCacheSizeText;
        set => SetProperty(ref _mediaCacheSizeText, value);
    }

    public CornerRadius BubbleCornerRadius => new(Settings.BubbleRadius);
    public int BubbleRadius
    {
        get => Settings.BubbleRadius;
        set
        {
            if (Settings.BubbleRadius != value)
            {
                Settings.BubbleRadius = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BubbleCornerRadius));
                _ = _storageService.SaveSettingsAsync(Settings);
            }
        }
    }

    public CornerRadius AvatarCornerRadius => new(42.0 * Settings.AvatarCornerRadiusPercent / 100.0);

    public CornerRadius HeaderAvatarCornerRadius => new(38.0 * Settings.AvatarCornerRadiusPercent / 100.0);

    public CornerRadius DrawerAvatarCornerRadius => new(72.0 * Settings.AvatarCornerRadiusPercent / 100.0);

    public CornerRadius PreviewAvatarCornerRadius => new(64.0 * Settings.AvatarCornerRadiusPercent / 100.0);

    public bool CanResetAvatarRadius => Settings.AvatarCornerRadiusPercent != 15;
    public bool CanResetBubbleRadius => Settings.BubbleRadius != 16;

    public bool LaunchAtStartup
    {
        get => Settings.LaunchAtStartup;
        set
        {
            if (Settings.LaunchAtStartup != value)
            {
                Settings.LaunchAtStartup = value;
                OnPropertyChanged();
                UpdateAutostartRegistry(value);
                _ = _storageService.SaveSettingsAsync(Settings);
            }
        }
    }

    private static void UpdateAutostartRegistry(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key != null)
            {
                if (enable)
                {
                    string? currentModule = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(currentModule) && File.Exists(currentModule))
                    {
                        key.SetValue("PrimeDesktop", $"\"{currentModule}\"");
                    }
                }
                else
                {
                    key.DeleteValue("PrimeDesktop", throwOnMissingValue: false);
                }
            }
        }
        catch { }
    }
    public bool CanResetWallpaperOpacity => Math.Abs(Settings.WallpaperOpacity - 0.35) > 0.001;
    public bool CanResetVideoNoteRadius => Settings.VideoNoteRadiusPercent != 50;
    public bool CanResetSoundVolume => Settings.SoundEffectsVolume != 80;
    public bool CanResetAccentColor => !string.Equals(Settings.AccentColorHex, "#4D9FFF", StringComparison.OrdinalIgnoreCase);
    public bool CanResetWallpaper => !string.IsNullOrEmpty(Settings.ChatWallpaperPath);

    public int AvatarCornerRadiusPercent

    {
        get => Settings.AvatarCornerRadiusPercent;

        set

        {
            if (Settings.AvatarCornerRadiusPercent != value)

            {
                Settings.AvatarCornerRadiusPercent = value;

                OnPropertyChanged();

                OnPropertyChanged(nameof(AvatarCornerRadius));

                OnPropertyChanged(nameof(HeaderAvatarCornerRadius));

                OnPropertyChanged(nameof(DrawerAvatarCornerRadius));

                OnPropertyChanged(nameof(PreviewAvatarCornerRadius));

                _ = _storageService.SaveSettingsAsync(Settings);

            }

        }

    }

    // --- Android Parity: Settings Properties & Commands ---
    private System.Threading.CancellationTokenSource? _profileBroadcastCts;

    public string MyDisplayName
    {
        get => Settings.MyDisplayName;
        set
        {
            if (Settings.MyDisplayName != value)
            {
                Settings.MyDisplayName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Settings));
                DebounceSaveAndBroadcastProfile();
            }
        }
    }

    public string MyUsername
    {
        get => Settings.MyUsername;
        set
        {
            if (Settings.MyUsername != value)
            {
                Settings.MyUsername = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Settings));
                DebounceSaveAndBroadcastProfile();
            }
        }
    }

    private void DebounceSaveAndBroadcastProfile()
    {
        _profileBroadcastCts?.Cancel();
        _profileBroadcastCts = new System.Threading.CancellationTokenSource();
        var token = _profileBroadcastCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, token);
                if (!token.IsCancellationRequested)
                {
                    await _storageService.SaveSettingsAsync(Settings);
                    await BroadcastMyProfileAndAvatarAsync();
                }
            }
            catch { }
        }, token);
    }

    public string[] SearchEngines => new[] { "Google", "Яндекс", "DuckDuckGo", "Bing" };
    public string SelectedSearchEngine
    {
        get => Settings.SearchEngine;
        set
        {
            if (Settings.SearchEngine != value)
            {
                Settings.SearchEngine = value;
                OnPropertyChanged();
                _ = _storageService.SaveSettingsAsync(Settings);
            }
        }
    }
    public ICommand ClearBrowserCacheCommand { get; }

    public int VideoNoteRadiusPercent
    {
        get => Settings.VideoNoteRadiusPercent;
        set
        {
            if (Settings.VideoNoteRadiusPercent != value)
            {
                Settings.VideoNoteRadiusPercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VideoNoteCornerRadius));
                OnPropertyChanged(nameof(VideoNoteChatCornerRadius));
                _ = _storageService.SaveSettingsAsync(Settings);
            }
        }
    }
    public CornerRadius VideoNoteCornerRadius => new(50.0 * Settings.VideoNoteRadiusPercent / 100.0);
    public CornerRadius VideoNoteChatCornerRadius => new(95.0 * Settings.VideoNoteRadiusPercent / 50.0);

    private bool _cbStoragePhotos = true;
    public bool CbStoragePhotos { get => _cbStoragePhotos; set { SetProperty(ref _cbStoragePhotos, value); UpdateClearButtonText(); } }

    private bool _cbStorageVideos = true;
    public bool CbStorageVideos { get => _cbStorageVideos; set { SetProperty(ref _cbStorageVideos, value); UpdateClearButtonText(); } }

    private bool _cbStorageAudio = true;
    public bool CbStorageAudio { get => _cbStorageAudio; set { SetProperty(ref _cbStorageAudio, value); UpdateClearButtonText(); } }

    private bool _cbStorageGifs = true;
    public bool CbStorageGifs { get => _cbStorageGifs; set { SetProperty(ref _cbStorageGifs, value); UpdateClearButtonText(); } }

    private bool _cbStorageFiles = true;
    public bool CbStorageFiles { get => _cbStorageFiles; set { SetProperty(ref _cbStorageFiles, value); UpdateClearButtonText(); } }

    private bool _cbStorageAvatars = true;
    public bool CbStorageAvatars { get => _cbStorageAvatars; set { SetProperty(ref _cbStorageAvatars, value); UpdateClearButtonText(); } }

    private bool _cbStorageCache = true;
    public bool CbStorageCache { get => _cbStorageCache; set { SetProperty(ref _cbStorageCache, value); UpdateClearButtonText(); } }

    private string _storagePhotosText = "0 ф. (0 Б)";
    public string StoragePhotosText { get => _storagePhotosText; set => SetProperty(ref _storagePhotosText, value); }

    private string _storageVideosText = "0 ф. (0 Б)";
    public string StorageVideosText { get => _storageVideosText; set => SetProperty(ref _storageVideosText, value); }

    private string _storageAudioText = "0 ф. (0 Б)";
    public string StorageAudioText { get => _storageAudioText; set => SetProperty(ref _storageAudioText, value); }

    private string _storageGifsText = "0 ф. (0 Б)";
    public string StorageGifsText { get => _storageGifsText; set => SetProperty(ref _storageGifsText, value); }

    private string _storageFilesText = "0 ф. (0 Б)";
    public string StorageFilesText { get => _storageFilesText; set => SetProperty(ref _storageFilesText, value); }

    private string _storageAvatarsText = "0 ф. (0 Б)";
    public string StorageAvatarsText { get => _storageAvatarsText; set => SetProperty(ref _storageAvatarsText, value); }

    private string _storageCacheText = "0 ф. (0 Б)";
    public string StorageCacheText { get => _storageCacheText; set => SetProperty(ref _storageCacheText, value); }

    private string _storageTotalText = "Занято: 0 Б (0 файлов)";
    public string StorageTotalText { get => _storageTotalText; set => SetProperty(ref _storageTotalText, value); }

    private string _clearSelectedStorageText = "🧹 Очистить выбранное";
    public string ClearSelectedStorageText { get => _clearSelectedStorageText; set => SetProperty(ref _clearSelectedStorageText, value); }

    public ICommand ToggleSelectAllStorageCommand { get; }
    public ICommand ClearSelectedStorageCommand { get; }

    public int ThanksHeartCount => Settings.ThanksHeartCount;
    public ICommand SendHeartCommand { get; }

    public string SelectedThemeMode
    {
        get => Settings.ThemeMode ?? "system";
        set
        {
            if (Settings.ThemeMode != value)
            {
                ApplyThemeSelection(value);
            }
        }
    }

    public bool IsDarkTheme => Settings.DarkTheme;

    public bool IsDarkThemeSelected
    {
        get => Settings.ThemeMode == "dark";
        set
        {
            if (value) ApplyThemeSelection("dark");
        }
    }

    public bool IsLightThemeSelected
    {
        get => Settings.ThemeMode == "light";
        set
        {
            if (value) ApplyThemeSelection("light");
        }
    }

    public bool IsSystemTheme
    {
        get => string.IsNullOrEmpty(Settings.ThemeMode) || Settings.ThemeMode == "system";
        set
        {
            if (value) ApplyThemeSelection("system");
        }
    }

    public ICommand SetThemeModeCommand { get; }

    public void ApplyThemeSelection(string mode)
    {
        Settings.ThemeMode = mode;
        Settings.DarkTheme = (mode == "dark") || (mode == "system" && ThemeManager.GetSystemIsDark());
        ThemeManager.ApplyTheme(mode, Settings.AccentColorHex);
        _ = _storageService.SaveSettingsAsync(Settings);

        OnPropertyChanged(nameof(SelectedThemeMode));
        OnPropertyChanged(nameof(IsDarkThemeSelected));
        OnPropertyChanged(nameof(IsLightThemeSelected));
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(Settings));

        StatusMessage = mode switch
        {
            "dark" => "Включена тёмная тема",
            "light" => "Включена светлая тема",
            _ => "Включена системная (адаптивная) тема"
        };
    }
    public ICommand OpenGithubCommand { get; }
    public ICommand OpenDevGithubCommand { get; }
    public ICommand CheckUpdatesCommand { get; }

    public ICommand SaveSettingsCommand { get; }

    public ICommand DeleteChatCommand { get; }

    public ICommand DeleteSelectedChatCommand { get; }

    private bool _isRecordingVoice;

    public bool IsRecordingVoice

    {
        get => _isRecordingVoice;

        set => SetProperty(ref _isRecordingVoice, value);

    }

    public ICommand ToggleVoiceRecordCommand { get; }

    public ICommand PlayVoiceMessageCommand { get; }
    public ICommand SeekVoiceMessageCommand { get; }
    public ICommand ToggleVoiceSpeedCommand { get; }
    public ICommand TestNotificationSoundCommand { get; }
    public ICommand ToggleMuteChatCommand { get; }
    public ICommand ClearChatMessagesCommand { get; }
    public ICommand CopyChatInfoCommand { get; }

    private bool _isPhotoViewerOpen;

    public bool IsPhotoViewerOpen

    {
        get => _isPhotoViewerOpen;

        set => SetProperty(ref _isPhotoViewerOpen, value);

    }

    private string? _viewedPhotoPath;

    public string? ViewedPhotoPath

    {
        get => _viewedPhotoPath;

        set => SetProperty(ref _viewedPhotoPath, value);

    }

    public ICommand OpenPhotoViewerCommand { get; }

    public ICommand ClosePhotoViewerCommand { get; }

    public ICommand SaveViewedPhotoCommand { get; }

    public ICommand CopyViewedPhotoCommand { get; }

    public ICommand ChangeWallpaperCommand { get; }

    public ICommand ResetWallpaperCommand { get; }
    public ICommand ResetAvatarRadiusCommand { get; }
    public ICommand ResetBubbleRadiusCommand { get; }
    public ICommand ResetWallpaperOpacityCommand { get; }
    public ICommand ResetVideoNoteRadiusCommand { get; }
    public ICommand ResetSoundVolumeCommand { get; }
    public ICommand ResetAccentColorCommand { get; }

    public ICommand CloseActiveChatCommand { get; }

    public ICommand QuickReactionCommand { get; }

    public ICommand SetReactionCommand { get; }

    public ICommand ToggleReactionCommand { get; }

    public ICommand OpenDeviceDiscoveryCommand { get; }

    public ICommand CloseDeviceDiscoveryCommand { get; }

    public ICommand StartChatWithDiscoveredCommand { get; }

    public ICommand SelectProfileTabCommand { get; }

    public ICommand SelectSettingsTabCommand { get; }

    public ICommand EditMessageCommand { get; }

    public ICommand CancelEditCommand { get; }

    public ICommand ForwardMessageCommand { get; }

    public ICommand OpenPhotoEditorCommand { get; }

    public ICommand OpenFileAttachmentCommand { get; }

    public ICommand OpenFileCommand { get; }

    public ICommand OpenFileLocationCommand { get; }

    public ICommand NextContactAvatarCommand { get; }

    public ICommand PrevContactAvatarCommand { get; }

    public ICommand DeleteContactAvatarCommand { get; }

    private ChatMessage? _editingMessage;

    public ChatMessage? EditingMessage

    {
        get => _editingMessage;

        set

        {
            if (SetProperty(ref _editingMessage, value))

            {
                OnPropertyChanged(nameof(IsEditingMessage));

            }

        }

    }

    public bool IsEditingMessage => EditingMessage != null;

    private ChatMessage? _contextMenuMessage;

    public ChatMessage? ContextMenuMessage

    {
        get => _contextMenuMessage;

        set => SetProperty(ref _contextMenuMessage, value);

    }

    private int _selectedSettingsTab = 0;

    public int SelectedSettingsTab

    {
        get => _selectedSettingsTab;

        set => SetProperty(ref _selectedSettingsTab, value);

    }    private bool _isDiscoveryOpen;

    public bool IsDiscoveryOpen

    {
        get => _isDiscoveryOpen;

        set => SetProperty(ref _isDiscoveryOpen, value);

    }

    private ObservableCollection<ChatItem> _discoveredDevices = new();

    public ObservableCollection<ChatItem> DiscoveredDevices

    {
        get => _discoveredDevices;

        set => SetProperty(ref _discoveredDevices, value);

    }

    private int _selectedProfileTab = 0;

    public int SelectedProfileTab

    {
        get => _selectedProfileTab;

        set => SetProperty(ref _selectedProfileTab, value);

    }

    private int _profileGridColumns = 3;
    public int ProfileGridColumns
    {
        get => _profileGridColumns;
        set => SetProperty(ref _profileGridColumns, value);
    }

    public void UpdateDrawerGridColumns(double drawerWidth)
    {
        double contentWidth = drawerWidth - 42.0;
        int cols = 3;
        if (contentWidth >= 480.0)
        {
            cols = 5;
        }
        else if (contentWidth >= 380.0)
        {
            cols = 4;
        }
        else
        {
            cols = 3;
        }

        if (ProfileGridColumns != cols)
        {
            ProfileGridColumns = cols;
        }
    }

    public System.Collections.Generic.IEnumerable<ChatMessage> SharedMedia =>
        SelectedChat?.Messages.Where(m => m.IsPhoto || m.IsVideo || m.IsVideoNote) ?? Enumerable.Empty<ChatMessage>();

    public System.Collections.Generic.IEnumerable<SharedMediaItem> SharedMediaList
    {
        get
        {
            if (SelectedChat == null) yield break;
            foreach (var msg in SelectedChat.Messages)
            {
                if (msg.IsCollage && msg.CollagePaths != null && msg.CollagePaths.Count > 0)
                {
                    foreach (var cp in msg.CollagePaths)
                    {
                        if (File.Exists(cp))
                        {
                            yield return new SharedMediaItem
                            {
                                FilePath = cp,
                                IsVideo = false,
                                FormattedTime = msg.FormattedTime,
                                Sender = msg.Sender,
                                SourceMessage = msg
                            };
                        }
                    }
                }
                else if (msg.IsPhoto && !string.IsNullOrEmpty(msg.FilePath) && File.Exists(msg.FilePath))
                {
                    yield return new SharedMediaItem
                    {
                        FilePath = msg.FilePath,
                        IsVideo = false,
                        FormattedTime = msg.FormattedTime,
                        Sender = msg.Sender,
                        SourceMessage = msg
                    };
                }
                else if ((msg.IsVideo || msg.IsVideoNote || Views.PhotoViewerWindow.IsVideoPath(msg.FilePath)) && !string.IsNullOrEmpty(msg.FilePath) && File.Exists(msg.FilePath))
                {
                    yield return new SharedMediaItem
                    {
                        FilePath = msg.FilePath,
                        IsVideo = true,
                        DurationText = !string.IsNullOrEmpty(msg.Duration) && msg.Duration != "0:00" ? msg.Duration : "Видео",
                        FormattedTime = msg.FormattedTime,
                        Sender = msg.Sender,
                        SourceMessage = msg
                    };
                }
            }
        }
    }

    public System.Collections.Generic.IEnumerable<ChatMessage> SharedFiles =>
        SelectedChat?.Messages.Where(m => m.IsFile && !m.IsMusic && !m.IsVoice && !m.IsPhoto && !m.IsVideo && !m.IsVideoNote) ?? Enumerable.Empty<ChatMessage>();

    public System.Collections.Generic.IEnumerable<SharedFileItem> SharedFileList
    {
        get
        {
            if (SelectedChat == null) yield break;
            foreach (var msg in SelectedChat.Messages)
            {
                if (msg.IsFile && !msg.IsMusic && !msg.IsVoice && !msg.IsPhoto && !msg.IsVideo && !msg.IsVideoNote && !Views.PhotoViewerWindow.IsVideoPath(msg.FilePath))
                {
                    string fName = msg.FileName ?? (string.IsNullOrEmpty(msg.FilePath) ? "Документ" : Path.GetFileName(msg.FilePath));
                    var (badge, color) = SharedFileItem.GetExtensionStyle(fName);
                    yield return new SharedFileItem
                    {
                        FilePath = msg.FilePath ?? string.Empty,
                        FileName = fName,
                        ExtensionBadge = badge,
                        BadgeBrushHex = color,
                        FormattedSize = msg.FormattedFileSize,
                        FormattedTime = msg.FormattedTime,
                        SourceMessage = msg
                    };
                }
            }
        }
    }

    public System.Collections.Generic.IEnumerable<ChatMessage> SharedAudio =>
        SelectedChat?.Messages.Where(m => (m.IsVoice || m.IsMusic) && !m.IsPhoto && !m.IsVideo && !m.IsVideoNote) ?? Enumerable.Empty<ChatMessage>();

    public bool HasSharedMedia => SharedMediaList.Any();
    public bool HasSharedFiles => SharedFileList.Any();
    public bool HasSharedAudio => SharedAudio.Any();

    public MainViewModel()

    {
        _settings.PropertyChanged += async (s, e) =>
        {
            CheckForPendingSettingsChanges();
            try { await _storageService.SaveSettingsAsync(_settings); } catch {}
        };

        _audioService.PlaybackStateChanged += (msg) =>
        {
            if (msg != null)
            {
                SendMyActivityState(msg.IsMusic ? "LISTENING_MUSIC" : "LISTENING_AUDIO");
            }
            else
            {
                SendMyActivityState("IDLE", true);
            }
        };

        SendMessageCommand = new RelayCommand(async () => await SendTextMessageAsync(), () => HasSelectedChat && !string.IsNullOrWhiteSpace(InputMessageText));

        AttachFileCommand = new RelayCommand(async () =>
        {
            IsAttachmentTrayOpen = false;
            await SendAttachmentAsync(PacketType.File);
        }, () => HasSelectedChat);

        AttachPhotoCommand = new RelayCommand(async () =>
        {
            IsAttachmentTrayOpen = false;
            await SendAttachmentAsync(PacketType.Photo);
        }, () => HasSelectedChat);

        AttachGifCommand = new RelayCommand(async () =>
        {
            IsAttachmentTrayOpen = false;
            await SendGifAttachmentAsync();
        }, () => HasSelectedChat);

        CancelReplyCommand = new RelayCommand(() => ReplyMessage = null);

        ReplyToMessageCommand = new RelayCommand(param => { if (param is ChatMessage msg) ReplyMessage = msg; });

        CopyMessageCommand = new RelayCommand(param =>

        {
            if (param is ChatMessage msg)

            {
                string textToCopy = !string.IsNullOrEmpty(msg.Text) ? msg.Text :
                                    !string.IsNullOrEmpty(msg.FileName) ? msg.FileName :
                                    msg.FilePath ?? "";

                if (!string.IsNullOrEmpty(textToCopy))
                {
                    Clipboard.SetText(textToCopy);

                    StatusMessage = "Скопировано в буфер обмена";
                }

            }

        });

        DeleteMessageCommand = new RelayCommand(async param =>
        {
            if (param is ChatMessage msg && SelectedChat != null)
            {
                if (!msg.IsFromMe)
                {
                    StatusMessage = "Нельзя удалять чужие сообщения";
                    return;
                }

                SelectedChat.Messages.Remove(msg);

                SelectedChat.LastMessage = SelectedChat.Messages.LastOrDefault()?.Text ?? "";

                byte[] delPayload = System.Text.Encoding.UTF8.GetBytes(msg.Id);

                try

                {
                    
                        await _bluetoothService.SendPacketAsync(SelectedChat.DeviceAddress, PacketType.DeleteMessage, delPayload);

                }

                catch { }

                await _storageService.SaveChatsAsync(_allChats);

            }

        });

        ScanDevicesCommand = new RelayCommand(async () => await ScanDevicesAsync());

        ConnectChatCommand = new RelayCommand(async () => await ConnectSelectedChatAsync(), () => HasSelectedChat);

        DisconnectChatCommand = new RelayCommand(async () => await DisconnectSelectedChatAsync(), () => HasSelectedChat);
        ToggleReadingModeCommand = new RelayCommand(async () =>
        {
            if (SelectedChat == null) return;
            IsReadingMode = !IsReadingMode;
            if (IsReadingMode)
            {
                SendMyActivityState("READING_MODE", true);
                string addr = !string.IsNullOrWhiteSpace(SelectedChat.DeviceAddress) ? SelectedChat.DeviceAddress : SelectedChat.PeerId;
                await Task.Delay(100);
                await _bluetoothService.DisconnectAsync(addr);
                SelectedChat.IsOnline = false;
                SelectedChat.ConnectionState = DeviceConnectionState.Disconnected;
                StatusMessage = "Режим чтения включен (связь принудительно отключена)";
            }
            else
            {
                SendMyActivityState("IDLE", true);
                await ConnectSelectedChatAsync();
                StatusMessage = $"Подключение к {SelectedChat.DisplayName}...";
            }
        }, () => HasSelectedChat);

        ToggleSettingsCommand = new RelayCommand(() =>
        {
            IsSettingsOpen = !IsSettingsOpen;
            if (IsSettingsOpen) {
                UpdateMediaCacheSize();
                _ = CalculateStorageBreakdownAsync();
                Services.WindowManager.OpenSettings(this);
            } else {
                Services.WindowManager.CloseSettings();
            }
        });

        OpenSearchCommand = new RelayCommand(() => IsSearchActive = true);
        CloseSearchCommand = new RelayCommand(() => IsSearchActive = false);
        ToggleSearchCommand = new RelayCommand(() => IsSearchActive = !IsSearchActive);

        SelectPresetAvatarCommand = new RelayCommand(param =>
        {
            if (param is string assetName && !string.IsNullOrWhiteSpace(assetName))
            {
                try
                {
                    var uri = new Uri($"pack://application:,,,/Assets/{assetName}", UriKind.Absolute);
                    var streamInfo = Application.GetResourceStream(uri);
                    if (streamInfo != null)
                    {
                        using var ms = new MemoryStream();
                        streamInfo.Stream.CopyTo(ms);
                        byte[] bytes = ms.ToArray();
                        string? saved = AvatarManager.SaveAvatarBytesToHistory("my_profile", bytes, ".jpg");
                        if (saved != null)
                        {
                            Settings.MyAvatarPath = null;
                            Settings.MyAvatarPath = saved;
                            OnPropertyChanged(nameof(Settings));
                            _ = _storageService.SaveSettingsAsync(Settings);
                            _ = BroadcastMyProfileAndAvatarAsync();
                            StatusMessage = "Аватар профиля успешно установлен";
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Ошибка выбора аватара: {ex.Message}";
                }
            }
        });

        ResetMyAvatarCommand = new RelayCommand(() =>
        {
            AvatarManager.DeleteAvatar("my_profile");
            Settings.MyAvatarPath = null;
            OnPropertyChanged(nameof(Settings));
            _ = _storageService.SaveSettingsAsync(Settings);
            _ = BroadcastMyProfileAndAvatarAsync();
            StatusMessage = "Аватар профиля удален";
        });

        BroadcastProfileCommand = new RelayCommand(async () =>
        {
            await BroadcastMyProfileAndAvatarAsync();
            StatusMessage = "Данные профиля обновлены в сети";
        });

        ClearMediaCacheCommand = new RelayCommand(() =>
        {
            try
            {
                string mediaDir = StorageService.MediaDirectory;
                if (Directory.Exists(mediaDir))
                {
                    foreach (var f in Directory.GetFiles(mediaDir))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
                UpdateMediaCacheSize();
                StatusMessage = "Кэш медиа успешно очищен";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка очистки кэша: {ex.Message}";
            }
        });

        ClearAllChatsCommand = new RelayCommand(async () =>
        {
            var iconTrash = Application.Current?.TryFindResource("IconTrash") as System.Windows.Media.Geometry;
            bool confirmed = PrimeDesktop.Views.PrimeDialogWindow.Confirm(
                title: "Очистка всех чатов",
                message: "Вы уверены, что хотите удалить все чаты и историю сообщений? Это действие необратимо.",
                positiveText: "Удалить все",
                negativeText: "Отмена",
                isDanger: true,
                icon: iconTrash);

            if (confirmed)
            {
                IsMediaDrawerOpen = false;
                _allChats.Clear();
                ApplySearchFilter();
                SelectedChat = null;
                await _storageService.SaveChatsAsync(_allChats);
                StatusMessage = "Все чаты удалены";
            }
        });

        OpenStorageFolderCommand = new RelayCommand(() =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = StorageService.BaseDirectory,
                    UseShellExecute = true
                });
            }
            catch { }
        });

        ClearBrowserCacheCommand = new RelayCommand(() =>
        {
            try
            {
                string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PrimeBrowserCache");
                if (System.IO.Directory.Exists(tempDir))
                {
                    System.IO.Directory.Delete(tempDir, true);
                }
            }
            catch { }
            StatusMessage = "Кэш и данные браузера очищены";
        });

        ToggleSelectAllStorageCommand = new RelayCommand(() =>
        {
            bool all = CbStoragePhotos && CbStorageVideos && CbStorageAudio && CbStorageGifs && CbStorageFiles && CbStorageAvatars && CbStorageCache;
            bool targetState = !all;
            CbStoragePhotos = targetState;
            CbStorageVideos = targetState;
            CbStorageAudio = targetState;
            CbStorageGifs = targetState;
            CbStorageFiles = targetState;
            CbStorageAvatars = targetState;
            CbStorageCache = targetState;
        });

        ClearSelectedStorageCommand = new RelayCommand(async () => await ClearSelectedStorageAsync());

        SendHeartCommand = new RelayCommand(() =>
        {
            Settings.ThanksHeartCount++;
            OnPropertyChanged(nameof(ThanksHeartCount));
            _ = _storageService.SaveSettingsAsync(Settings);
            StatusMessage = $"Спасибо за поддержку! ❤️ ({Settings.ThanksHeartCount})";
        });

        OpenGithubCommand = new RelayCommand(() =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/Vaniahorosh/Prime_messenger_Desktop",
                    UseShellExecute = true
                });
            }
            catch { }
        });

        OpenDevGithubCommand = new RelayCommand(() =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/Vaniahorosh",
                    UseShellExecute = true
                });
            }
            catch { }
        });

        CheckUpdatesCommand = new RelayCommand(() =>
        {
            StatusMessage = "У вас установлена актуальная версия Prime Messenger v0.1 Desk";
        });

        ToggleThemeCommand = new RelayCommand(() =>
        {
            string newMode = Settings.DarkTheme ? "light" : "dark";
            ApplyThemeSelection(newMode);
        });

        SetThemeModeCommand = new RelayCommand(param =>
        {
            if (param is string mode && !string.IsNullOrEmpty(mode))
            {
                ApplyThemeSelection(mode);
            }
        });

        ToggleMediaDrawerCommand = new RelayCommand(() => IsMediaDrawerOpen = !IsMediaDrawerOpen);

        ConnectCompanionWifiCommand = new RelayCommand(async () => await ConnectWifiCompanionAsync());

        ChangeContactAvatarCommand = new RelayCommand(() =>
        {
            if (SelectedChat == null) return;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите аватар для собеседника",
                Filter = "Изображения (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Все файлы (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                string sourceFile = dlg.FileName;
                try
                {
                    var cropper = new Views.AvatarCropperWindow(sourceFile);
                    if (cropper.ShowDialog() == true && !string.IsNullOrEmpty(cropper.CroppedResultPath))
                    {
                        sourceFile = cropper.CroppedResultPath;
                    }
                    else
                    {
                        return;
                    }
                }
                catch { }

                string? saved = AvatarManager.SaveAvatarToHistory(SelectedChat.DeviceAddress, sourceFile);
                if (saved != null)
                {
                    SelectedChat.AddAvatarToHistory(saved);
                    _ = _storageService.SaveChatsAsync(_allChats);
                    StatusMessage = "Аватар добавлен в профиль и историю";
                }
            }
        }, () => HasSelectedChat);

        NextContactAvatarCommand = new RelayCommand(() =>
        {
            SelectedChat?.NextAvatar();
        }, () => SelectedChat != null && SelectedChat.CanGoNextAvatar);

        PrevContactAvatarCommand = new RelayCommand(() =>
        {
            SelectedChat?.PrevAvatar();
        }, () => SelectedChat != null && SelectedChat.CanGoPreviousAvatar);

        DeleteContactAvatarCommand = new RelayCommand(() =>
        {
            if (SelectedChat != null)
            {
                SelectedChat.RemoveCurrentAvatar();
                _ = _storageService.SaveChatsAsync(_allChats);
                StatusMessage = "Аватар удален из истории";
            }
        }, () => SelectedChat != null && SelectedChat.HasAvatar);

        ChangeMyAvatarCommand = new RelayCommand(() =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите свой аватар",
                Filter = "Изображения (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Все файлы (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                string sourceFile = dlg.FileName;
                try
                {
                    var cropper = new Views.AvatarCropperWindow(sourceFile);
                    if (cropper.ShowDialog() == true && !string.IsNullOrEmpty(cropper.CroppedResultPath))
                    {
                        sourceFile = cropper.CroppedResultPath;
                    }
                    else
                    {
                        return;
                    }
                }
                catch { }

                string? saved = AvatarManager.SaveAvatar("my_profile", sourceFile);
                if (saved != null)
                {
                    Settings.MyAvatarPath = null;
                    Settings.MyAvatarPath = saved;
                    OnPropertyChanged(nameof(Settings));
                    _ = _storageService.SaveSettingsAsync(Settings);
                    _ = BroadcastMyProfileAndAvatarAsync();
                    StatusMessage = "Аватар профиля успешно обновлен";
                }
            }
        });

        ToggleVoiceRecordCommand = new RelayCommand(async () =>

        {
            if (SelectedChat == null) return;

            if (!_audioService.IsRecording)

            {
                if (_audioService.StartRecording())
                {
                    IsRecordingVoice = true;
                    SendMyActivityState("RECORDING_VOICE", true);
                    SoundEffectService.PlayRecordStart(Settings);
                    StatusMessage = "Запись голосового сообщения...";
                }
            }
            else
            {
                var (path, duration) = _audioService.StopRecording();
                IsRecordingVoice = false;
                SendMyActivityState("IDLE", true);
                SoundEffectService.PlayRecordStop(Settings);
                StatusMessage = "Запись завершена";

                if (!string.IsNullOrEmpty(path))

                {
                    await SendVoiceMessageAsync(path, duration);

                }

            }

        }, () => HasSelectedChat);

        PlayVoiceMessageCommand = new RelayCommand(param =>
        {
            if (param is ChatMessage msg)
            {
                _audioService.TogglePlay(msg);
            }
        });

        SeekVoiceMessageCommand = new RelayCommand(param =>
        {
            if (param is ValueTuple<object, double> (var ctx, var ratio) && ctx is ChatMessage msg)
            {
                _audioService.Seek(msg, ratio);
            }
            else if (param is (ChatMessage m, double r))
            {
                _audioService.Seek(m, r);
            }
        });

        ToggleVoiceSpeedCommand = new RelayCommand(param =>
        {
            if (param is ChatMessage msg)
            {
                double nextSpeed = msg.PlaybackSpeed switch
                {
                    >= 1.95 => 1.0,
                    >= 1.45 => 2.0,
                    _ => 1.5
                };
                _audioService.SetSpeed(msg, nextSpeed);
            }
        });

        TestNotificationSoundCommand = new RelayCommand(() =>
        {
            SoundEffectService.PlayTest(Settings);
        });

        ToggleMuteChatCommand = new RelayCommand(param =>
        {
            if (param is ChatItem chat)
            {
                chat.IsMuted = !chat.IsMuted;
                _ = _storageService.SaveChatsAsync(_allChats);
            }
            else if (SelectedChat != null)
            {
                SelectedChat.IsMuted = !SelectedChat.IsMuted;
                _ = _storageService.SaveChatsAsync(_allChats);
            }
        });

        ClearChatMessagesCommand = new RelayCommand(async param =>
        {
            var chat = param as ChatItem ?? SelectedChat;
            if (chat != null)
            {
                chat.Messages.Clear();
                chat.LastMessage = "История сообщений очищена";
                await _storageService.SaveChatsAsync(_allChats);
                StatusMessage = $"История чата '{chat.DisplayName}' очищена";
            }
        });

        CopyChatInfoCommand = new RelayCommand(param =>
        {
            var chat = param as ChatItem ?? SelectedChat;
            if (chat != null)
            {
                string info = !string.IsNullOrEmpty(chat.Username) ? $"@{chat.Username}" : chat.DisplayName;
                if (!string.IsNullOrEmpty(info))
                {
                    try
                    {
                        Clipboard.SetText(info);
                        StatusMessage = $"Скопировано в буфер: {info}";
                    }
                    catch { }
                }
            }
        });

        OpenPhotoViewerCommand = new RelayCommand(param =>
        {
            string? path = param as string;
            if (param is ChatMessage msg)
            {
                path = msg.FilePath;
            }
            else if (param is SharedMediaItem smi)
            {
                path = smi.FilePath;
            }
            else if (param is SharedFileItem sfi)
            {
                path = sfi.FilePath;
            }

            if (string.IsNullOrEmpty(path))
            {
                path = SelectedChat?.CurrentAvatarDisplayPath;
            }

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                ViewedPhotoPath = path;
                IsPhotoViewerOpen = true;

                if (SelectedChat != null && SelectedChat.AvatarHistory != null && SelectedChat.AvatarHistory.Contains(path))
                {
                    var avatarMediaList = SelectedChat.AvatarHistory
                        .Where(File.Exists)
                        .Select(p => new MediaViewerItem
                        {
                            FilePath = p,
                            Sender = SelectedChat.DisplayName,
                            FormattedTime = "История аватарок",
                            IsVideo = false,
                            FileName = Path.GetFileName(p)
                        }).ToList();

                    Services.WindowManager.OpenPhotoViewerWithItems(path, avatarMediaList, this);
                    return;
                }

                Services.WindowManager.OpenPhotoViewer(path, this);
            }
        });

        ClosePhotoViewerCommand = new RelayCommand(() =>

        {
            IsPhotoViewerOpen = false;

            Services.WindowManager.ClosePhotoViewer();
            ViewedPhotoPath = null;

        });

        OpenFileAttachmentCommand = new RelayCommand(param =>
        {
            string? path = param as string;
            if (param is ChatMessage msg)
            {
                path = msg.FilePath;
            }
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                if (Views.PhotoViewerWindow.IsVideoPath(path) || (param is ChatMessage cm && (cm.IsPhoto || cm.IsVideoNote)))
                {
                    Services.WindowManager.OpenPhotoViewer(path, this);
                    return;
                }

                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                    }
                    catch { }
                }
            }
        });

        SaveViewedPhotoCommand = new RelayCommand(() =>

        {
            if (string.IsNullOrEmpty(ViewedPhotoPath) || !File.Exists(ViewedPhotoPath)) return;

            var dlg = new Microsoft.Win32.SaveFileDialog

            {
                Title = "Сохранить изображение",

                FileName = Path.GetFileName(ViewedPhotoPath),

                Filter = "Изображение PNG (*.png)|*.png|Изображение JPEG (*.jpg)|*.jpg|Все файлы (*.*)|*.*"

            };

            if (dlg.ShowDialog() == true)

            {
                File.Copy(ViewedPhotoPath, dlg.FileName, overwrite: true);

                StatusMessage = "Изображение сохранено";

            }

        });

        CopyViewedPhotoCommand = new RelayCommand(() =>

        {
            if (string.IsNullOrEmpty(ViewedPhotoPath) || !File.Exists(ViewedPhotoPath)) return;

            try

            {
                var bi = new System.Windows.Media.Imaging.BitmapImage(new Uri(ViewedPhotoPath));

                Clipboard.SetImage(bi);

                StatusMessage = "Изображение скопировано в буфер";

            }

            catch { }

        });

        ChangeWallpaperCommand = new RelayCommand(() =>

        {
            var dlg = new Microsoft.Win32.OpenFileDialog

            {
                Title = "Выберите фоновое изображение для чата",

                Filter = "Изображения (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|Все файлы (*.*)|*.*"

            };

            if (dlg.ShowDialog() == true)

            {
                string wallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrimeDesktop", "Wallpapers");

                Directory.CreateDirectory(wallDir);

                string ext = Path.GetExtension(dlg.FileName);

                string dest = Path.Combine(wallDir, $"chat_wallpaper{ext}");

                File.Copy(dlg.FileName, dest, overwrite: true);

                Settings.ChatWallpaperPath = dest;

                OnPropertyChanged(nameof(Settings));

                _ = _storageService.SaveSettingsAsync(Settings);

                StatusMessage = "Обои чата обновлены";

            }

        });

                CloseActiveChatCommand = new RelayCommand(() =>

        {
            SelectedChat = null;

            IsMediaDrawerOpen = false;

        });

        QuickReactionCommand = new RelayCommand(async param =>

        {
            if (param is ChatMessage msg)

            {
                await SendReactionAsync(msg, "❤️");

            }

        });

        SetReactionCommand = new RelayCommand(async param =>

        {
            if (param is object[] arr && arr.Length >= 2 && arr[0] is ChatMessage m && arr[1] is string r)

            {
                await SendReactionAsync(m, r);

            }

            else if (param is string emoji)

            {
                var target = ContextMenuMessage ?? SelectedChat?.Messages.LastOrDefault();

                if (target != null) await SendReactionAsync(target, emoji);

            }

        });

        ToggleReactionCommand = new RelayCommand(async param =>

        {
            if (param is ChatMessage msg)

            {
                await SendReactionAsync(msg, null);

            }

        });

        OpenDeviceDiscoveryCommand = new RelayCommand(async () =>

        {
            IsDiscoveryOpen = true;

            await ScanDevicesAsync();

        });

        CloseDeviceDiscoveryCommand = new RelayCommand(() =>

        {
            IsDiscoveryOpen = false;

        });

        StartChatWithDiscoveredCommand = new RelayCommand(param =>
        {
            if (param is ChatItem item)
            {
                UnmarkDeviceForgotten(item.DeviceAddress);
                UnmarkDeviceForgotten(item.PeerId);
                _ = _storageService.SaveSettingsAsync(Settings);

                var existing = FindChatByAddressOrPeer(item.PeerId) ?? 
                               FindChatByAddressOrPeer(item.DeviceAddress) ?? 
                               FindChatByAddressOrPeer(null, item.DisplayName);

                if (existing == null)
                {
                    _allChats.Add(item);
                    existing = item;
                }

                SelectedChat = existing;
                IsDiscoveryOpen = false;
                ApplySearchFilter();
            }
        });

        SelectProfileTabCommand = new RelayCommand(param =>

        {
            if (param is string s && int.TryParse(s, out int tab))

            {
                SelectedProfileTab = tab;

            }

            else if (param is int t)

            {
                SelectedProfileTab = t;

            }

        });

        SelectSettingsTabCommand = new RelayCommand(param =>

        {
            if (param is string s && int.TryParse(s, out int tab)) { SelectedSettingsTab = tab; if (tab == 4) _ = CalculateStorageBreakdownAsync(); }

            else if (param is int t) SelectedSettingsTab = t;

        });

        EditMessageCommand = new RelayCommand(param =>

        {
            if (param is ChatMessage msg && msg.IsFromMe)

            {
                EditingMessage = msg;

                InputMessageText = msg.Text;

            }

        });

        CancelEditCommand = new RelayCommand(() =>

        {
            EditingMessage = null;

            InputMessageText = string.Empty;

        });

        ForwardMessageCommand = new RelayCommand(param =>

        {
            if (param is ChatMessage msg)

            {
                InputMessageText = $"[Переслано от {msg.Sender}]: {msg.Text}";

            }

        });

        OpenPhotoEditorCommand = new RelayCommand(async param =>

        {
            IsAttachmentTrayOpen = false;
            string? p = param as string;

            if (string.IsNullOrEmpty(p))

            {
                var dlg = new OpenFileDialog

                {
                    Title = "Выберите фото для редактирования",

                    Filter = "Изображения (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Все файлы (*.*)|*.*"

                };

                if (dlg.ShowDialog() == true) p = dlg.FileName;

            }

            if (!string.IsNullOrEmpty(p) && File.Exists(p))

            {
                var editor = new Views.PhotoEditorWindow(p);

                if (editor.ShowDialog() == true && !string.IsNullOrEmpty(editor.ResultFilePath))

                {
                    await SendPhotoFileAsync(editor.ResultFilePath);

                }

            }

        });

        OpenFileCommand = new RelayCommand(param =>
        {
            string? p = param as string;
            if (param is SharedFileItem sfi) p = sfi.FilePath;
            else if (param is ChatMessage msg) p = msg.FilePath;

            if (!string.IsNullOrEmpty(p) && File.Exists(p))
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = p,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch { }
            }
        });

        OpenFileLocationCommand = new RelayCommand(param =>
        {
            string? path = param as string;
            if (param is SharedFileItem sfi) path = sfi.FilePath;
            else if (param is ChatMessage msg) path = msg.FilePath;

            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true
                    });
                    StatusMessage = $"Файл выделен в проводнике: {Path.GetFileName(path)}";
                    return;
                }

                // If file doesn't exist on disk, open user's Downloads folder
                string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloadsDir))
                {
                    downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrimeDesktop", "Downloads");
                    Directory.CreateDirectory(downloadsDir);
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{downloadsDir}\"",
                    UseShellExecute = true
                });
                StatusMessage = "Открыта папка Загрузки";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Не удалось открыть проводник: {ex.Message}";
            }
        });

        ResetAvatarRadiusCommand = new RelayCommand(() =>
        {
            AvatarCornerRadiusPercent = 15;
            OnPropertyChanged(nameof(CanResetAvatarRadius));
        });

        ResetBubbleRadiusCommand = new RelayCommand(() =>
        {
            BubbleRadius = 16;
            OnPropertyChanged(nameof(CanResetBubbleRadius));
        });

        ResetWallpaperOpacityCommand = new RelayCommand(() =>
        {
            Settings.WallpaperOpacity = 0.35;
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(CanResetWallpaperOpacity));
            _ = _storageService.SaveSettingsAsync(Settings);
        });

        ResetVideoNoteRadiusCommand = new RelayCommand(() =>
        {
            VideoNoteRadiusPercent = 50;
            OnPropertyChanged(nameof(CanResetVideoNoteRadius));
        });

        ResetSoundVolumeCommand = new RelayCommand(() =>
        {
            Settings.SoundEffectsVolume = 80;
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(CanResetSoundVolume));
            _ = _storageService.SaveSettingsAsync(Settings);
        });

        ResetAccentColorCommand = new RelayCommand(() =>
        {
            ChangeAccentColorCommand?.Execute("#4D9FFF");
            OnPropertyChanged(nameof(CanResetAccentColor));
        });

        ResetWallpaperCommand = new RelayCommand(() =>

        {
            Settings.ChatWallpaperPath = null;

            OnPropertyChanged(nameof(Settings));

            _ = _storageService.SaveSettingsAsync(Settings);

            StatusMessage = "Обои чата сброшены к стандартным";

        });

        DeleteChatCommand = new RelayCommand(async param =>

        {
            if (param is ChatItem chat)

            {
                await ConfirmAndDeleteChatAsync(chat);

            }

        });

        DeleteSelectedChatCommand = new RelayCommand(async () =>

        {
            if (SelectedChat != null)

            {
                await ConfirmAndDeleteChatAsync(SelectedChat);

            }

        }, () => HasSelectedChat);

        SaveSettingsCommand = new RelayCommand(async () =>
        {
            await ApplyAndSaveSettingsAsync();
        });

        ChangeAccentColorCommand = new RelayCommand(param =>

        {
            if (param is string hex)

            {
                ThemeManager.ApplyAccentColor(hex);

                Settings.AccentColorHex = hex;

                _ = _storageService.SaveSettingsAsync(Settings);

            }

        });

        // Network wire-up

        _bluetoothService.PacketReceived += OnPacketReceived;

        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;

        _bluetoothService.ErrorOccurred += OnNetworkError;

        _companionService.PacketReceived += OnPacketReceived;

        _companionService.ConnectionStateChanged += OnConnectionStateChanged;

        _companionService.ErrorOccurred += OnNetworkError;

        _ = InitializeAsync();

    }

    private async Task InitializeAsync()
    {
        try
        {
            App.Log("[Init] Starting InitializeAsync...");
            Settings = await _storageService.LoadSettingsAsync();

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ThemeManager.InitializeTheme(Settings.ThemeMode, Settings.AccentColorHex);
            });

            var chats = await _storageService.LoadChatsAsync();
            App.Log($"[Init] Loaded {chats.Count} chats from storage.");

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var cleanChats = new List<ChatItem>();
                foreach (var c in chats)
                {
                    if (IsSelfDevice(c.DeviceAddress, c.DisplayName, c.Username) || IsSelfDevice(c.PeerId, c.DisplayName, c.Username))
           {
               App.Log($"[Init] Filtering out self chat '{c.DisplayName}' ({c.PeerId}) from loaded storage.");
               continue;
           }
           c.DeduplicateAvatarHistory();
                    foreach (var m in c.Messages)
                    {
                        if (m.IsVideoNote)
                        {
                            string f = (m.FileName ?? m.FilePath ?? "").ToLowerInvariant();
                            bool isRealVn = f.StartsWith("vn_") || f.StartsWith("rec_circle_") || f.StartsWith("rec_videonote_") ||
                                            (m.Text != null && m.Text.StartsWith("VIDEO_NOTE:", StringComparison.OrdinalIgnoreCase));
                            if (!isRealVn)
                            {
                                m.IsVideoNote = false;
                            }
                            else
                            {
                                string shapeTag = "";
                                if (!string.IsNullOrEmpty(m.Text) && m.Text.StartsWith("VIDEO_NOTE:", StringComparison.OrdinalIgnoreCase))
                                {
                                    shapeTag = m.Text.Substring("VIDEO_NOTE:".Length).Trim();
                                }
                                else if (!string.IsNullOrEmpty(m.FileName) && m.FileName.StartsWith("vn_", StringComparison.OrdinalIgnoreCase))
                                {
                                    string withoutExt = Path.GetFileNameWithoutExtension(m.FileName);
                                    var fnParts = withoutExt.Split('_');
                                    if (fnParts.Length >= 3) shapeTag = fnParts[2].Trim();
                                }
                                if (!string.IsNullOrWhiteSpace(shapeTag))
                                {
                                    if (int.TryParse(shapeTag, out int r))
                                        m.VideoNoteRadiusPercent = Math.Clamp(r, 0, 50);
                                    else if (shapeTag.Equals("square", StringComparison.OrdinalIgnoreCase))
                                        m.VideoNoteRadiusPercent = 0;
                                    else if (shapeTag.Equals("circle", StringComparison.OrdinalIgnoreCase))
                                        m.VideoNoteRadiusPercent = 50;
                                    else if (shapeTag.Equals("star", StringComparison.OrdinalIgnoreCase))
                                        m.VideoNoteRadiusPercent = 35;
                                    else if (shapeTag.Equals("prime", StringComparison.OrdinalIgnoreCase))
                                        m.VideoNoteRadiusPercent = 25;
                                }
                            }
                        }
                    }
                    // Filter out corrupt / error-named chats and obsolete system chat
                    if (IsStatusKeyword(c.DisplayName) || 
                        c.DisplayName == "Ошибка подключения" || 
                        c.DisplayName == "Установка связи..." ||
                        c.PeerId.Equals("peer_prime_support", StringComparison.OrdinalIgnoreCase) ||
                        c.DisplayName.Equals("Команда Прайм", StringComparison.OrdinalIgnoreCase))
                    {
                        App.Log($"[Init] Skipping corrupt or removed chat '{c.DisplayName}' ({c.PeerId})");
                        continue;
                    }

                    // Anonymize PeerId if it was stored as raw MAC
                    if (DeviceAnonymizer.IsHardwareIdentifier(c.PeerId))
                    {
                        if (string.IsNullOrWhiteSpace(c.DeviceAddress) || c.DeviceAddress == c.PeerId)
                        {
                            c.DeviceAddress = c.PeerId;
                        }
                        c.PeerId = DeviceAnonymizer.GetOpaquePeerId(c.DeviceAddress);
                    }

                    c.DisplayName = DeviceAnonymizer.SanitizeDisplayName(c.DisplayName, c.DeviceAddress);
                    c.Username = DeviceAnonymizer.SanitizeUsername(c.Username, c.DeviceAddress);

                    if (string.IsNullOrEmpty(c.AvatarPath))
                    {
                        c.AvatarPath = AvatarManager.GetAvatarPath(c.DeviceAddress) ?? AvatarManager.GetAvatarPath(c.PeerId);
                    }
                    if (!string.IsNullOrWhiteSpace(c.DeviceAddress) && !string.IsNullOrWhiteSpace(c.PeerId))
                    {
                        _bluetoothService.RegisterPeerAddress(c.PeerId, c.DeviceAddress);
                    }

                    // Deduplicate against already processed chats in cleanChats
                    var dup = cleanChats.FirstOrDefault(existing =>
                        existing.PeerId.Equals(c.PeerId, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(c.DeviceAddress) && !string.IsNullOrWhiteSpace(existing.DeviceAddress) &&
                         BluetoothRfcommService.NormalizeMacAddress(c.DeviceAddress).Equals(BluetoothRfcommService.NormalizeMacAddress(existing.DeviceAddress), StringComparison.OrdinalIgnoreCase)));

                    if (dup != null)
                    {
                        App.Log($"[Init] Merging duplicate saved chat '{c.DisplayName}' into '{dup.DisplayName}'");
                        MergeChats(dup, c);
                    }
                    else
                    {
                        cleanChats.Add(c);
                    }
                }

                foreach (var c in cleanChats)
                {
                    _allChats.Add(c);
                }

                // If any corrupt or duplicate chats were filtered/merged, persist cleaned list
                if (_allChats.Count != chats.Count)
                {
                    _ = _storageService.SaveChatsAsync(_allChats);
                }

                ApplySearchFilter();
                // Always start with empty placeholder view as requested
                SelectedChat = null;
            });

            // Start background servers
            _bluetoothService.MyDisplayName = Settings.MyDisplayName;
            _bluetoothService.MyUsername = Settings.MyUsername;
            App.Log($"[Init] Starting Bluetooth RFCOMM server for '{Settings.MyDisplayName}'...");
            await _bluetoothService.StartServerAsync();

            try
            {
                _companionService.MyDisplayName = Settings.MyDisplayName;
                _companionService.MyUsername = Settings.MyUsername;
                App.Log($"[Init] Starting Companion Wi-Fi server...");
                await _companionService.StartServerAsync();
            }
            catch (Exception ex)
            {
                App.Log($"[Init] Companion Wi-Fi server failed to start: {ex.Message}");
            }

            // Automatically discover paired Bluetooth devices and add them to chats if not present
            var paired = await _bluetoothService.DiscoverDevicesAsync();
            App.Log($"[Init] Discovered {paired.Count} paired devices.");
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var dev in paired)
                {
                    // If device is self or was explicitly deleted/forgotten, do not add it
                    if (IsSelfDevice(dev.DeviceAddress, dev.DisplayName, dev.Username) || IsSelfDevice(dev.PeerId, dev.DisplayName, dev.Username))
                    {
                        App.Log($"[Init] Skipping self device '{dev.DisplayName}' ({dev.PeerId} / {dev.DeviceAddress}).");
                        continue;
                    }
                    if (IsDeviceForgotten(dev.DeviceAddress) || IsDeviceForgotten(dev.PeerId))
                    {
                        App.Log($"[Init] Skipping forgotten/deleted device '{dev.DisplayName}' ({dev.PeerId} / {dev.DeviceAddress}).");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(dev.DeviceAddress) && !string.IsNullOrWhiteSpace(dev.PeerId))
                    {
                        _bluetoothService.RegisterPeerAddress(dev.PeerId, dev.DeviceAddress);
                    }

                    var existing = FindChatByAddressOrPeer(dev.PeerId) ?? 
                                   FindChatByAddressOrPeer(dev.DeviceAddress) ?? 
                                   FindChatByAddressOrPeer(null, dev.DisplayName);

                    if (existing != null)
                    {
                        if (string.IsNullOrWhiteSpace(existing.DeviceAddress) || existing.DeviceAddress == existing.PeerId)
                        {
                            existing.DeviceAddress = dev.DeviceAddress;
                        }
                        if (!string.IsNullOrWhiteSpace(existing.DeviceAddress) && !string.IsNullOrWhiteSpace(existing.PeerId))
                        {
                            _bluetoothService.RegisterPeerAddress(existing.PeerId, existing.DeviceAddress);
                        }
                        if ((DeviceAnonymizer.IsHardwareIdentifier(existing.DisplayName) || existing.DisplayName.StartsWith("Собеседник")) &&
                            !string.IsNullOrWhiteSpace(dev.DisplayName) && !DeviceAnonymizer.IsHardwareIdentifier(dev.DisplayName))
                        {
                            existing.DisplayName = dev.DisplayName;
                        }
                    }
                    else
                    {
                        _allChats.Add(dev);
                        App.Log($"[Init] Added paired device '{dev.DisplayName}' ({dev.PeerId}) to chat list.");
                    }
                }
                ApplySearchFilter();

                // If AutoConnectBluetooth is on, connect to paired devices in background without selecting
                if (Settings.AutoConnectBluetooth)
                {
                    var candidates = _allChats.Where(c => c.IsBluetoothDevice && !c.PeerId.StartsWith("peer_prime")).ToList();
                    foreach (var chat in candidates)
                    {
                        string addr = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;
                        App.Log($"[Init] Auto-connecting to paired device '{chat.DisplayName}' (PeerId={chat.PeerId}, Addr={addr})...");
                        _ = _bluetoothService.ConnectAsync(addr);
                    }
                }
            });

            App.Log("[Init] InitializeAsync completed successfully.");
        }
        catch (Exception ex)
        {
            App.Log($"[Init FATAL ERROR] InitializeAsync crashed: {ex}");
        }
    }

    private void ApplySearchFilter()

    {
        if (string.IsNullOrWhiteSpace(SearchQuery))

        {
            FilteredChats = new ObservableCollection<ChatItem>(_allChats);

        }

        else

        {
            var query = SearchQuery.Trim().ToLowerInvariant();

            var matches = _allChats.Where(c =>

                c.DisplayName.ToLowerInvariant().Contains(query) ||

                c.Username.ToLowerInvariant().Contains(query) ||

                c.LastMessage.ToLowerInvariant().Contains(query));

            FilteredChats = new ObservableCollection<ChatItem>(matches);

        }

    }

    public async Task SendReactionAsync(ChatMessage message, string? emoji)

    {
        if (SelectedChat == null || message == null) return;

        // Toggle off if already placed

        if (message.Reaction == emoji)

        {
            emoji = null;

        }

        message.Reaction = emoji;

        message.ReactionAuthor = Settings.MyDisplayName;

        byte[] payload = PrimePacket.BuildReactionPayload(message.Id, emoji, Settings.MyDisplayName);

        try

        {
            string targetAddr = !string.IsNullOrWhiteSpace(SelectedChat.DeviceAddress) ? SelectedChat.DeviceAddress : SelectedChat.PeerId;

            await _bluetoothService.SendPacketAsync(targetAddr, PacketType.Reaction, payload);

            App.Log($"[Reaction] Sent reaction '{emoji}' for msg '{message.Id}' to '{targetAddr}'");

        }

        catch (Exception ex)
        {
            App.Log($"[Reaction ERROR] Failed to send reaction: {ex.Message}");
        }

        await _storageService.SaveChatsAsync(_allChats);

    }

public async Task SendTextMessageAsync()

    {
        var targetChat = SelectedChat;
        if (targetChat == null || string.IsNullOrWhiteSpace(InputMessageText)) return;

        string text = InputMessageText.Trim();

        InputMessageText = string.Empty;

        if (EditingMessage != null)

        {
            var targetMsg = EditingMessage;

            EditingMessage = null;

            targetMsg.Text = text;

            targetMsg.IsEdited = true;

            targetChat.LastMessage = text;

            byte[] editPayload = System.Text.Encoding.UTF8.GetBytes($"{targetMsg.Id}:::{text}");

            try

            {
                
                    await _bluetoothService.SendPacketAsync(targetChat.DeviceAddress, PacketType.Edit, editPayload);

            }

            catch { }

            await _storageService.SaveChatsAsync(_allChats);

            return;

        }

        string msgId = Guid.NewGuid().ToString();

        var reply = ReplyMessage;

        ReplyMessage = null;

        var message = new ChatMessage

        {
            Id = msgId,

            Sender = Settings.MyDisplayName,

            Text = text,

            IsFromMe = true,

            FormattedTime = DateTime.Now.ToString("HH:mm"),

            ReplyToId = reply?.Id,

            ReplyToSender = reply?.Sender,

            ReplyToText = reply?.Text

        };

        targetChat.Messages.Add(message);
        SoundEffectService.PlaySent(Settings);

        targetChat.LastMessage = text;

        targetChat.LastMessageTime = message.FormattedTime;

        byte[] payload = PrimePacket.BuildTextPayload(

            msgId,

            text,

            reply?.Id,

            reply?.Sender,

            reply?.Text);

        try
        {
            if (targetChat.IsBluetoothDevice && !targetChat.IsOnline && !targetChat.PeerId.StartsWith("peer_prime"))
            {
                StatusMessage = $"Подключение к {targetChat.DisplayName}...";
                await _bluetoothService.ConnectAsync(targetChat.DeviceAddress);
            }

            await _bluetoothService.SendPacketAsync(targetChat.DeviceAddress, PacketType.Text, payload);
            message.IsDelivered = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sending failed: {ex.Message}";
        }

        await _storageService.SaveChatsAsync(_allChats);

    }

    private async Task SendVoiceMessageAsync(string filePath, string duration)

    {
        var targetChat = SelectedChat;
        if (targetChat == null) return;

        var fileBytes = await File.ReadAllBytesAsync(filePath);

        var msg = new ChatMessage

        {
            Sender = Settings.MyDisplayName,

            FilePath = filePath,

            FileName = Path.GetFileName(filePath),

            FileSizeBytes = fileBytes.Length,

            Duration = duration,

            IsFromMe = true,

            Type = PacketType.Voice,

            FormattedTime = DateTime.Now.ToString("HH:mm")

        };

        targetChat.Messages.Add(msg);

        targetChat.LastMessage = $"🎤 Голосовое сообщение ({duration})";

        targetChat.LastMessageTime = msg.FormattedTime;

        try

        {
            
            string vFileName = System.IO.Path.GetFileName(filePath);
            byte[] payload = PrimePacket.BuildFilePayload(msg.Id, vFileName, fileBytes, duration, isVoice: true);
            string targetAddr = !string.IsNullOrWhiteSpace(targetChat.DeviceAddress) ? targetChat.DeviceAddress : targetChat.PeerId;
            await _bluetoothService.SendPacketAsync(targetAddr, PacketType.File, payload);

            msg.IsDelivered = true;

        }

        catch (Exception ex)

        {
            StatusMessage = $"Ошибка отправки аудио: {ex.Message}";

        }

        await _storageService.SaveChatsAsync(_allChats);

    }

    public async Task SendPhotoFileAsync(string filePath)

    {
        var targetChat = SelectedChat;
        if (targetChat == null || !File.Exists(filePath)) return;

        string fileName = Path.GetFileName(filePath);

        byte[] fileBytes = await File.ReadAllBytesAsync(filePath);

        var message = new ChatMessage

        {
            Sender = Settings.MyDisplayName,

            Text = fileName,

            FilePath = filePath,

            FileName = fileName,

            FileSizeBytes = fileBytes.Length,

            Type = PacketType.Photo,

            IsFromMe = true,

            FormattedTime = DateTime.Now.ToString("HH:mm")

        };

        targetChat.Messages.Add(message);
        SoundEffectService.PlaySent(Settings);

        targetChat.LastMessage = "📷 Фотография";

        targetChat.LastMessageTime = message.FormattedTime;

        try

        {
            
                await _bluetoothService.SendPacketAsync(targetChat.DeviceAddress, PacketType.Photo, fileBytes);

            message.IsDelivered = true;

        }

        catch (Exception ex)

        {
            StatusMessage = $"Ошибка отправки фото: {ex.Message}";

        }

        await _storageService.SaveChatsAsync(_allChats);

    }

    public async Task SendAttachmentAsync(PacketType type)
    {
        var targetChat = SelectedChat;
        if (targetChat == null) return;

        SendMyActivityState(type == PacketType.Photo ? "SELECTING_MEDIA" : "SELECTING_FILE", true);

        var dialog = new OpenFileDialog();
        if (type == PacketType.Photo)
        {
            dialog.Title = "Выберите изображение (одно или несколько для коллажа)";
            dialog.Filter = "Изображения|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Все файлы|*.*";
            dialog.Multiselect = true;
        }
        else
        {
            dialog.Title = "Выберите файл для отправки";
            dialog.Filter = "Все файлы|*.*";
            dialog.Multiselect = false;
        }

        if (dialog.ShowDialog() != true)
        {
            SendMyActivityState("IDLE", true);
            return;
        }

        SendMyActivityState(type == PacketType.Photo ? "SENDING_PHOTO" : "SENDING_FILE", true);

        string targetAddr = !string.IsNullOrWhiteSpace(targetChat.DeviceAddress) ? targetChat.DeviceAddress : targetChat.PeerId;

        // Auto-connect if disconnected
        if (targetChat.IsBluetoothDevice && !targetChat.IsOnline && !targetChat.PeerId.StartsWith("peer_prime"))
        {
            StatusMessage = $"Подключение к {targetChat.DisplayName}...";
            await _bluetoothService.ConnectAsync(targetAddr);
        }

        // Multi-photo / collage
        if (type == PacketType.Photo && dialog.FileNames.Length > 1)
        {
            var items = new List<(byte[] data, string ext, bool isVideo)>();
            var collagePaths = new List<string>();
            foreach (var fn in dialog.FileNames)
            {
                byte[] data = await File.ReadAllBytesAsync(fn);
                string ext = Path.GetExtension(fn).TrimStart('.');
                items.Add((data, ext, false));
                collagePaths.Add(fn);
            }

            var message = new ChatMessage
            {
                Sender = Settings.MyDisplayName,
                Text = "",
                FilePath = collagePaths.FirstOrDefault(),
                CollagePaths = collagePaths,
                Type = PacketType.Photo,
                IsFromMe = true,
                FormattedTime = DateTime.Now.ToString("HH:mm")
            };

            targetChat.Messages.Add(message);
        SoundEffectService.PlaySent(Settings);
            targetChat.LastMessage = "📷 Коллаж";
            targetChat.LastMessageTime = message.FormattedTime;

            try
            {
                byte[] payload = PrimePacket.BuildCollagePayload(message.Id, items, "");
                await _bluetoothService.SendPacketAsync(targetAddr, PacketType.Photo, payload);
                message.IsDelivered = true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Collage send failed: {ex.Message}";
            }
            finally
            {
                SendMyActivityState("IDLE", true);
            }

            await _storageService.SaveChatsAsync(_allChats);
            return;
        }

        // Single file or single photo
        string filePath = dialog.FileName;
        string fileName = Path.GetFileName(filePath);
        byte[] fileBytes = await File.ReadAllBytesAsync(filePath);

        string lowerSingle = fileName.ToLowerInvariant();
        bool isVidSingle = lowerSingle.EndsWith(".mp4") || lowerSingle.EndsWith(".mkv") || lowerSingle.EndsWith(".mov") || lowerSingle.EndsWith(".avi") || lowerSingle.EndsWith(".webm");
        bool isVideoNote = isVidSingle && (lowerSingle.StartsWith("vn_") || lowerSingle.StartsWith("rec_circle_") || lowerSingle.StartsWith("rec_videonote_"));

        var singleMsg = new ChatMessage
        {
            Sender = Settings.MyDisplayName,
            Text = fileName,
            FilePath = filePath,
            FileName = fileName,
            FileSizeBytes = fileBytes.Length,
            Type = type,
            IsVideoNote = isVideoNote,
            VideoNoteRadiusPercent = Settings.VideoNoteRadiusPercent,
            IsFromMe = true,
            FormattedTime = DateTime.Now.ToString("HH:mm")
        };

        targetChat.Messages.Add(singleMsg);
        SoundEffectService.PlaySent(Settings);
        if (type == PacketType.Photo) targetChat.LastMessage = "📷 Фотография";
        else if (isVideoNote) targetChat.LastMessage = "📹 Видеосообщение";
        else if (isVidSingle) targetChat.LastMessage = $"🎥 {fileName}";
        else targetChat.LastMessage = $"📎 {fileName}";
        targetChat.LastMessageTime = singleMsg.FormattedTime;

        try
        {
            byte[] payload = (type == PacketType.Photo)
                ? PrimePacket.BuildPhotoPayload(singleMsg.Id, fileBytes, "")
                : PrimePacket.BuildFilePayload(singleMsg.Id, fileName, fileBytes, "00:00", false, isVideoNote ? $"VIDEO_NOTE:{Settings.VideoNoteRadiusPercent}" : string.Empty);

            await _bluetoothService.SendPacketAsync(targetAddr, type, payload);
            singleMsg.IsDelivered = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Attachment send failed: {ex.Message}";
        }
        finally
        {
            SendMyActivityState("IDLE", true);
        }

        await _storageService.SaveChatsAsync(_allChats);
    }

    public async Task SendGifAttachmentAsync()
    {
        var targetChat = SelectedChat;
        if (targetChat == null) return;

        SendMyActivityState("SELECTING_MEDIA", true);

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите GIF-анимацию",
            Filter = "GIF-анимация (*.gif)|*.gif|Все файлы (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            SendMyActivityState("IDLE", true);
            return;
        }

        SendMyActivityState("SENDING_PHOTO", true);

        string targetAddr = !string.IsNullOrWhiteSpace(targetChat.DeviceAddress) ? targetChat.DeviceAddress : targetChat.PeerId;
        if (targetChat.IsBluetoothDevice && !targetChat.IsOnline && !targetChat.PeerId.StartsWith("peer_prime"))
        {
            StatusMessage = $"Подключение к {targetChat.DisplayName}...";
            await _bluetoothService.ConnectAsync(targetAddr);
        }

        string filePath = dialog.FileName;
        string fileName = System.IO.Path.GetFileName(filePath);
        byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);

        var singleMsg = new ChatMessage
        {
            Sender = Settings.MyDisplayName,
            Text = fileName,
            FilePath = filePath,
            FileName = fileName,
            FileSizeBytes = fileBytes.Length,
            Type = PacketType.Photo,
            IsFromMe = true,
            FormattedTime = DateTime.Now.ToString("HH:mm")
        };

        targetChat.Messages.Add(singleMsg);
        targetChat.LastMessage = "👾 GIF-анимация";
        targetChat.LastMessageTime = singleMsg.FormattedTime;

        try
        {
            byte[] payload = PrimePacket.BuildPhotoPayload(singleMsg.Id, fileBytes, "");
            await _bluetoothService.SendPacketAsync(targetAddr, PacketType.Photo, payload);
            singleMsg.IsDelivered = true;
        }
        catch (Exception ex)
        {
            App.Log($"[Attachment] Failed to send GIF: {ex.Message}");
            singleMsg.IsDelivered = false;
            StatusMessage = "Ошибка отправки GIF-анимации";
        }
        finally
        {
            SendMyActivityState("IDLE", true);
        }

        _ = _storageService.SaveChatsAsync(_allChats);
    }

    private DateTime _lastTypingSentTime = DateTime.MinValue;

    public void SendMyActivityState(string state, bool force = false)
    {
        if (SelectedChat == null) return;
        state = string.IsNullOrWhiteSpace(state) ? "IDLE" : state.Trim();

        if (!force && state == _myCurrentActivityState && (DateTime.UtcNow - _lastActivitySentTime).TotalSeconds < 2.5)
        {
            return;
        }

        _myCurrentActivityState = state;
        _lastActivitySentTime = DateTime.UtcNow;

        string targetAddr = !string.IsNullOrWhiteSpace(SelectedChat.DeviceAddress) ? SelectedChat.DeviceAddress : SelectedChat.PeerId;
        if (!string.IsNullOrWhiteSpace(targetAddr))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    byte[] payload = PrimePacket.BuildTypingPayload($"STATE:{state}");
                    await _bluetoothService.SendPacketAsync(targetAddr, PacketType.Typing, payload);
                }
                catch { }
            });
        }

        App.Current?.Dispatcher?.Invoke(() =>
        {
            _myActivityIdleTimer?.Stop();
            if (state != "IDLE")
            {
                double timeoutSeconds = 4.5;
                var upper = state.ToUpperInvariant();
                if (upper.StartsWith("RECORDING") || upper.StartsWith("SENDING")) timeoutSeconds = 12.0;
                else if (upper.StartsWith("SELECTING") || upper.StartsWith("VIEWING") || upper.StartsWith("LISTENING")) timeoutSeconds = 7.0;

                _myActivityIdleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
                _myActivityIdleTimer.Tick += (s, e) =>
                {
                    _myActivityIdleTimer.Stop();
                    if (_myCurrentActivityState == state)
                    {
                        SendMyActivityState("IDLE", true);
                    }
                };
                _myActivityIdleTimer.Start();
            }
        });
    }

    private void NotifyTyping()
    {
        SendMyActivityState("TYPING");
    }

    public async Task ScanDevicesAsync()

    {
        IsScanning = true;

        StatusMessage = "Scanning for paired Bluetooth devices...";

        try

        {
            var found = await _bluetoothService.DiscoverDevicesAsync();

            DiscoveredDevices.Clear();

            foreach (var dev in found)

            {
                DiscoveredDevices.Add(dev);

            }

            StatusMessage = $"Поиск завершен. Найдено устройств: {found.Count}";

        }

        catch (Exception ex)

        {
            StatusMessage = $"Scan error: {ex.Message}";

        }

        finally

        {
            IsScanning = false;

        }

    }

    public async Task ConnectSelectedChatAsync()
    {
        if (SelectedChat == null) return;

        StatusMessage = $"Connecting to {SelectedChat.DisplayName}...";
        string addr = !string.IsNullOrWhiteSpace(SelectedChat.DeviceAddress) ? SelectedChat.DeviceAddress : SelectedChat.PeerId;
        await _bluetoothService.ConnectAsync(addr);
    }

    public async Task DisconnectSelectedChatAsync()
    {
        if (SelectedChat == null) return;

        string addr = !string.IsNullOrWhiteSpace(SelectedChat.DeviceAddress) ? SelectedChat.DeviceAddress : SelectedChat.PeerId;
        await _bluetoothService.DisconnectAsync(addr);
    }

    public bool IsSelfDevice(string? addrOrPeer, string? displayName = null, string? username = null)
    {
        try
        {
            // 1. Check against local username and display name
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(Settings.MyUsername))
            {
                if (string.Equals(username.Trim(), Settings.MyUsername.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(Settings.MyDisplayName))
            {
                if (string.Equals(displayName.Trim(), Settings.MyDisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Also check current machine name / hostname
            string hostName = Environment.MachineName;
            if (!string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(hostName))
            {
                if (string.Equals(displayName.Trim(), hostName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // 2. Check MAC addresses of all local network / bluetooth adapters
            if (!string.IsNullOrWhiteSpace(addrOrPeer))
            {
                string norm = BluetoothRfcommService.NormalizeMacAddress(addrOrPeer);
                if (!string.IsNullOrWhiteSpace(norm))
                {
                    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        var bytes = nic.GetPhysicalAddress().GetAddressBytes();
                        if (bytes != null && bytes.Length == 6)
                        {
                            string nicMac = string.Join(":", bytes.Select(b => b.ToString("X2")));
                            if (string.Equals(norm, nicMac, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return false;
    }

    public bool IsDeviceForgotten(string? addrOrPeer)
    {
        if (string.IsNullOrWhiteSpace(addrOrPeer)) return false;
        var norm = addrOrPeer.Trim().ToUpperInvariant();
        var hw = _bluetoothService.ResolveHardwareAddress(norm);
        var normHw = !string.IsNullOrWhiteSpace(hw) ? BluetoothRfcommService.NormalizeMacAddress(hw) : string.Empty;

        lock (Settings.ForgottenDevices)
        {
            return Settings.ForgottenDevices.Any(f =>
            {
                var fNorm = f.Trim().ToUpperInvariant();
                if (fNorm == norm) return true;
                if (!string.IsNullOrEmpty(normHw) && BluetoothRfcommService.NormalizeMacAddress(fNorm) == normHw) return true;
                return false;
            });
        }
    }

    public void MarkDeviceForgotten(string? addrOrPeer)
    {
        if (string.IsNullOrWhiteSpace(addrOrPeer)) return;
        var norm = addrOrPeer.Trim().ToUpperInvariant();
        lock (Settings.ForgottenDevices)
        {
            if (!Settings.ForgottenDevices.Contains(norm))
            {
                Settings.ForgottenDevices.Add(norm);
            }
            var hw = _bluetoothService.ResolveHardwareAddress(norm);
            if (!string.IsNullOrWhiteSpace(hw))
            {
                var normHw = BluetoothRfcommService.NormalizeMacAddress(hw);
                if (!string.IsNullOrWhiteSpace(normHw) && !Settings.ForgottenDevices.Contains(normHw))
                {
                    Settings.ForgottenDevices.Add(normHw);
                }
            }
        }
    }

    public void UnmarkDeviceForgotten(string? addrOrPeer)
    {
        if (string.IsNullOrWhiteSpace(addrOrPeer)) return;
        var norm = addrOrPeer.Trim().ToUpperInvariant();
        var hw = _bluetoothService.ResolveHardwareAddress(norm);
        var normHw = !string.IsNullOrWhiteSpace(hw) ? BluetoothRfcommService.NormalizeMacAddress(hw) : string.Empty;

        lock (Settings.ForgottenDevices)
        {
            Settings.ForgottenDevices.RemoveAll(f =>
            {
                var fNorm = f.Trim().ToUpperInvariant();
                if (fNorm == norm) return true;
                if (!string.IsNullOrEmpty(normHw) && BluetoothRfcommService.NormalizeMacAddress(fNorm) == normHw) return true;
                return false;
            });
        }
    }

    public async Task ConnectWifiCompanionAsync()
    {
        try
        {
            StatusMessage = "Поиск смартфона по Wi-Fi...";
            var devices = await _companionService.DiscoverDevicesAsync();
            if (devices.Count > 0)
            {
                var target = devices[0];
                StatusMessage = $"Подключение к {target.DisplayName} по Wi-Fi...";
                bool connected = await _companionService.ConnectAsync(target.DeviceAddress);
                if (connected)
                {
                    StatusMessage = $"Синхронизация по Wi-Fi активна: {target.DisplayName}";
                    return;
                }
            }
            StatusMessage = "Смартфон в сети Wi-Fi не найден. Убедитесь, что Prime запущен на телефоне.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка подключения Wi-Fi: {ex.Message}";
        }
    }

    private async Task ConfirmAndDeleteChatAsync(ChatItem chat)
    {
        var iconTrash = Application.Current?.TryFindResource("IconTrash") as System.Windows.Media.Geometry;
        bool confirmed = PrimeDesktop.Views.PrimeDialogWindow.Confirm(
            title: "Удалить чат?",
            message: $"Вы действительно хотите удалить переписку и контакт «{chat.DisplayName}»?\n\nДиалог будет навсегда удален из списка чатов, а устройство полностью забыто.",
            positiveText: "Удалить",
            negativeText: "Отмена",
            isDanger: true,
            icon: iconTrash);

        if (!confirmed) return;

        string targetAddr = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;

        // Remember as forgotten in persistent settings
        MarkDeviceForgotten(chat.DeviceAddress);
        MarkDeviceForgotten(chat.PeerId);
        _ = _storageService.SaveSettingsAsync(Settings);

        byte[] payload = System.Text.Encoding.UTF8.GetBytes($"DELETE_CHAT:name={Settings.MyDisplayName};login={Settings.MyUsername};");

        try
        {
            if (!string.IsNullOrWhiteSpace(targetAddr))
            {
                if (!_bluetoothService.IsConnected(targetAddr) && !_companionService.IsConnected(targetAddr))
                {
                    // Attempt quick connect to deliver deletion packet
                    try
                    {
                        using var cts = new CancellationTokenSource(1500);
                        await _bluetoothService.ConnectAsync(targetAddr);
                    }
                    catch { }
                }

                if (_companionService.IsConnected(targetAddr))
                {
                    await _companionService.SendPacketAsync(targetAddr, PacketType.ChatDeleted, payload);
                    await Task.Delay(100);
                }
                else if (_bluetoothService.IsConnected(targetAddr))
                {
                    await _bluetoothService.SendPacketAsync(targetAddr, PacketType.ChatDeleted, payload);
                    await Task.Delay(100);
                }
            }
        }
        catch { }

        try
        {
            if (!string.IsNullOrWhiteSpace(targetAddr))
            {
                await _bluetoothService.DisconnectAsync(targetAddr);
                await _companionService.DisconnectAsync(targetAddr);
            }
        }
        catch { }

        // Forget device completely
        _bluetoothService.ForgetDevice(chat.DeviceAddress);
        _bluetoothService.ForgetDevice(chat.PeerId);
        _companionService.ForgetDevice(chat.DeviceAddress);
        _companionService.ForgetDevice(chat.PeerId);

        var discovered = DiscoveredDevices.FirstOrDefault(d => d.PeerId == chat.PeerId || d.DeviceAddress == chat.DeviceAddress);
        if (discovered != null) DiscoveredDevices.Remove(discovered);

        AvatarManager.DeleteAvatar(chat.DeviceAddress);
        AvatarManager.DeleteAvatar(chat.PeerId);

        _allChats.Remove(chat);
        ApplySearchFilter();

        bool isCurrent = SelectedChat == null || SelectedChat == chat || (!string.IsNullOrEmpty(chat.PeerId) && SelectedChat.PeerId == chat.PeerId) || (!string.IsNullOrEmpty(chat.DeviceAddress) && SelectedChat.DeviceAddress == chat.DeviceAddress);
        if (isCurrent)
        {
            IsMediaDrawerOpen = false;
            SelectedChat = null;
        }
        await _storageService.SaveChatsAsync(_allChats);

        StatusMessage = $"Чат «{chat.DisplayName}» удален, контакт забыт";

    }

    public static bool IsStatusKeyword(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        var lower = s.Trim().ToLowerInvariant();
        return lower.Contains("ошибка") ||
               lower.Contains("установка") ||
               lower.Contains("подключ") ||
               lower.Contains("отключ") ||
               lower.Contains("не в сети") ||
               lower.Contains("в сети") ||
               lower.Contains("ожидание") ||
               lower.Contains("не найдено");
    }

    private readonly System.Collections.Generic.List<string> _storageCatPhotos = new();
    private readonly System.Collections.Generic.List<string> _storageCatVideos = new();
    private readonly System.Collections.Generic.List<string> _storageCatAudio = new();
    private readonly System.Collections.Generic.List<string> _storageCatGifs = new();
    private readonly System.Collections.Generic.List<string> _storageCatFiles = new();
    private readonly System.Collections.Generic.List<string> _storageCatAvatars = new();
    private readonly System.Collections.Generic.List<string> _storageCatCache = new();

    private long _bytesCatPhotos = 0;
    private long _bytesCatVideos = 0;
    private long _bytesCatAudio = 0;
    private long _bytesCatGifs = 0;
    private long _bytesCatFiles = 0;
    private long _bytesCatAvatars = 0;
    private long _bytesCatCache = 0;

    private static string FormatStorageBytes(long bytes)
    {
        if (bytes <= 0) return "0 Б";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} КБ";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F1} МБ";
        double gb = mb / 1024.0;
        return $"{gb:F2} Р“Р‘";
    }

    private void UpdateClearButtonText()
    {
        long selectedBytes = 0;
        int selectedCount = 0;
        if (CbStoragePhotos) { selectedBytes += _bytesCatPhotos; selectedCount += _storageCatPhotos.Count; }
        if (CbStorageVideos) { selectedBytes += _bytesCatVideos; selectedCount += _storageCatVideos.Count; }
        if (CbStorageAudio) { selectedBytes += _bytesCatAudio; selectedCount += _storageCatAudio.Count; }
        if (CbStorageGifs) { selectedBytes += _bytesCatGifs; selectedCount += _storageCatGifs.Count; }
        if (CbStorageFiles) { selectedBytes += _bytesCatFiles; selectedCount += _storageCatFiles.Count; }
        if (CbStorageAvatars) { selectedBytes += _bytesCatAvatars; selectedCount += _storageCatAvatars.Count; }
        if (CbStorageCache) { selectedBytes += _bytesCatCache; selectedCount += _storageCatCache.Count; }

        if (selectedCount > 0)
            ClearSelectedStorageText = $"🧹 Очистить выбранное ({FormatStorageBytes(selectedBytes)})";
        else
            ClearSelectedStorageText = "🧹 Ничего не выбрано";
    }

    public async Task CalculateStorageBreakdownAsync()
    {
        await Task.Run(() =>
        {
            var pPhotos = new System.Collections.Generic.List<string>(); long bPhotos = 0;
            var pVideos = new System.Collections.Generic.List<string>(); long bVideos = 0;
            var pAudio = new System.Collections.Generic.List<string>(); long bAudio = 0;
            var pGifs = new System.Collections.Generic.List<string>(); long bGifs = 0;
            var pFiles = new System.Collections.Generic.List<string>(); long bFiles = 0;
            var pAvatars = new System.Collections.Generic.List<string>(); long bAvatars = 0;
            var pCache = new System.Collections.Generic.List<string>(); long bCache = 0;

            string baseDir = StorageService.BaseDirectory;
            if (System.IO.Directory.Exists(baseDir))
            {
                var di = new System.IO.DirectoryInfo(baseDir);
                foreach (var fi in di.EnumerateFiles("*", System.IO.SearchOption.AllDirectories))
                {
                    string name = fi.Name.ToLowerInvariant();
                    long len = fi.Length;

                    // Skip profile assets and databases
                    if (name.StartsWith("my_profile_avatar") || name.StartsWith("my_avatar") ||
                        name.StartsWith("wallpaper_img_") || name.EndsWith(".json") || name == "chats.dat")
                    {
                        continue;
                    }

                    if (name.StartsWith("avatar_") || name.StartsWith("rec_avatar_") || name.StartsWith("tmp_contact_avatar_"))
                    {
                        pAvatars.Add(fi.FullName);
                        bAvatars += len;
                    }
                    else if (name.EndsWith(".gif") || name.StartsWith("tenor_"))
                    {
                        pGifs.Add(fi.FullName);
                        bGifs += len;
                    }
                    else if (name.EndsWith(".mp4") || name.EndsWith(".mkv") || name.EndsWith(".3gp") ||
                             name.EndsWith(".webm") || name.EndsWith(".mov") || name.EndsWith(".avi") ||
                             name.StartsWith("rec_video_") || name.StartsWith("rec_circle_") || name.StartsWith("rec_videonote_"))
                    {
                        pVideos.Add(fi.FullName);
                        bVideos += len;
                    }
                    else if (name.EndsWith(".aac") || name.EndsWith(".m4a") || name.EndsWith(".opus") ||
                             name.EndsWith(".mp3") || name.EndsWith(".ogg") || name.EndsWith(".wav") ||
                             name.StartsWith("rec_voice_") || name.StartsWith("audio_") || name.StartsWith("voice_"))
                    {
                        pAudio.Add(fi.FullName);
                        bAudio += len;
                    }
                    else if (name.EndsWith(".jpg") || name.EndsWith(".jpeg") || name.EndsWith(".png") ||
                             name.EndsWith(".webp") || name.StartsWith("rec_photo_") || name.StartsWith("cache_img_") || name.StartsWith("cam_photo_"))
                    {
                        pPhotos.Add(fi.FullName);
                        bPhotos += len;
                    }
                    else if (name.StartsWith("tmp_") || name.EndsWith(".tmp") || name.EndsWith(".log") || name.EndsWith(".dat"))
                    {
                        pCache.Add(fi.FullName);
                        bCache += len;
                    }
                    else
                    {
                        pFiles.Add(fi.FullName);
                        bFiles += len;
                    }
                }
            }

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _storageCatPhotos.Clear(); _storageCatPhotos.AddRange(pPhotos); _bytesCatPhotos = bPhotos;
                _storageCatVideos.Clear(); _storageCatVideos.AddRange(pVideos); _bytesCatVideos = bVideos;
                _storageCatAudio.Clear(); _storageCatAudio.AddRange(pAudio); _bytesCatAudio = bAudio;
                _storageCatGifs.Clear(); _storageCatGifs.AddRange(pGifs); _bytesCatGifs = bGifs;
                _storageCatFiles.Clear(); _storageCatFiles.AddRange(pFiles); _bytesCatFiles = bFiles;
                _storageCatAvatars.Clear(); _storageCatAvatars.AddRange(pAvatars); _bytesCatAvatars = bAvatars;
                _storageCatCache.Clear(); _storageCatCache.AddRange(pCache); _bytesCatCache = bCache;

                StoragePhotosText = $"{pPhotos.Count} С„. ({FormatStorageBytes(bPhotos)})";
                StorageVideosText = $"{pVideos.Count} С„. ({FormatStorageBytes(bVideos)})";
                StorageAudioText = $"{pAudio.Count} С„. ({FormatStorageBytes(bAudio)})";
                StorageGifsText = $"{pGifs.Count} С„. ({FormatStorageBytes(bGifs)})";
                StorageFilesText = $"{pFiles.Count} С„. ({FormatStorageBytes(bFiles)})";
                StorageAvatarsText = $"{pAvatars.Count} С„. ({FormatStorageBytes(bAvatars)})";
                StorageCacheText = $"{pCache.Count} С„. ({FormatStorageBytes(bCache)})";

                long totalBytes = bPhotos + bVideos + bAudio + bGifs + bFiles + bAvatars + bCache;
                int totalCount = pPhotos.Count + pVideos.Count + pAudio.Count + pGifs.Count + pFiles.Count + pAvatars.Count + pCache.Count;
                StorageTotalText = $"Занято: {FormatStorageBytes(totalBytes)} ({totalCount} файлов)";

                UpdateClearButtonText();
            });
        });
    }

    public async Task ClearSelectedStorageAsync()
    {
        var toDelete = new System.Collections.Generic.List<string>();
        if (CbStoragePhotos) toDelete.AddRange(_storageCatPhotos);
        if (CbStorageVideos) toDelete.AddRange(_storageCatVideos);
        if (CbStorageAudio) toDelete.AddRange(_storageCatAudio);
        if (CbStorageGifs) toDelete.AddRange(_storageCatGifs);
        if (CbStorageFiles) toDelete.AddRange(_storageCatFiles);
        if (CbStorageAvatars) toDelete.AddRange(_storageCatAvatars);
        if (CbStorageCache) toDelete.AddRange(_storageCatCache);

        await Task.Run(() =>
        {
            foreach (var f in toDelete)
            {
                try { if (System.IO.File.Exists(f)) System.IO.File.Delete(f); } catch { }
            }
        });

        await CalculateStorageBreakdownAsync();
        UpdateMediaCacheSize();
        StatusMessage = "Выбранные данные успешно очищены";
    }

    public void UpdateMediaCacheSize()
    {
        try
        {
            long totalBytes = 0;
            string baseDir = StorageService.BaseDirectory;
            if (Directory.Exists(baseDir))
            {
                var di = new DirectoryInfo(baseDir);
                foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    totalBytes += fi.Length;
                }
            }
            if (totalBytes < 1024 * 1024)
                MediaCacheSizeText = $"{totalBytes / 1024.0:F1} КБ";
            else
                MediaCacheSizeText = $"{totalBytes / (1024.0 * 1024.0):F1} МБ";
        }
        catch
        {
            MediaCacheSizeText = "—";
        }
    }

    public async Task SendNetworkPacketAsync(string targetAddressOrPeer, PacketType type, byte[] payload)
    {
        if (_companionService.IsConnected(targetAddressOrPeer))
        {
            await _companionService.SendPacketAsync(targetAddressOrPeer, type, payload);
        }
        else
        {
            await _bluetoothService.SendPacketAsync(targetAddressOrPeer, type, payload);
        }
    }

    public async Task BroadcastMyProfileAndAvatarAsync()
    {
        try
        {
            var targetChats = _allChats.Where(c => c.IsOnline || _bluetoothService.IsConnected(c.DeviceAddress) || _companionService.IsConnected(c.DeviceAddress)).ToList();
            if (targetChats.Count == 0) return;

            byte[] profilePayload = PrimePacket.BuildProfileUpdatePayload(Settings.MyDisplayName, Settings.MyUsername);
            foreach (var chat in targetChats)
            {
                string addr = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;
                await SendNetworkPacketAsync(addr, PacketType.ProfileUpdate, profilePayload);
            }

            if (!string.IsNullOrWhiteSpace(Settings.MyAvatarPath) && File.Exists(Settings.MyAvatarPath))
            {
                byte[] avatarBytes = await File.ReadAllBytesAsync(Settings.MyAvatarPath);
                if (avatarBytes.Length > 0)
                {
                    foreach (var chat in targetChats)
                    {
                        string addr = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;
                        await SendNetworkPacketAsync(addr, PacketType.Avatar, avatarBytes);
                        App.Log($"[Avatar] Sent my avatar ({avatarBytes.Length} bytes) to '{chat.DisplayName}' ({addr})");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Broadcast Profile ERROR] {ex.Message}");
        }
    }

    private void OnConnectionStateChanged(string address, DeviceConnectionState state, string name)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var chat = FindChatByAddressOrPeer(address, name);

            if (chat == null)
            {
                // Only create chat when real connection is established!
                if (state != DeviceConnectionState.Connected)
                {
                    return;
                }

                string rawHw = _bluetoothService.ResolveHardwareAddress(address);
                if (IsSelfDevice(address, name) || IsSelfDevice(rawHw, name))
                {
                    App.Log($"[Connection] Ignoring connection from self device '{name}' ({address})");
                    return;
                }
                if (IsDeviceForgotten(address) || IsDeviceForgotten(rawHw) || IsDeviceForgotten(name))
                {
                    App.Log($"[Connection] Ignoring connection from forgotten device '{name}' ({address})");
                    return;
                }

                string peerId = _bluetoothService.ResolvePeerId(address);
                string initialName = !IsStatusKeyword(name)
                    ? DeviceAnonymizer.SanitizeDisplayName(name, rawHw)
                    : DeviceAnonymizer.SanitizeDisplayName(null, rawHw);

                chat = new ChatItem
                {
                    DeviceAddress = !string.IsNullOrWhiteSpace(rawHw) ? rawHw : address,
                    PeerId = !string.IsNullOrWhiteSpace(peerId) ? peerId : DeviceAnonymizer.GetOpaquePeerId(address),
                    DisplayName = initialName,
                    Username = DeviceAnonymizer.SanitizeUsername(null, address),
                    AvatarPath = AvatarManager.GetAvatarPath(address),
                    IsBluetoothDevice = true
                };

                _allChats.Add(chat);
                ApplySearchFilter();
            }

            chat.ConnectionState = state;
            chat.IsOnline = (state == DeviceConnectionState.Connected);

            // NEVER overwrite DisplayName with status messages like 'Ошибка подключения'
            if (!string.IsNullOrWhiteSpace(name) && !IsStatusKeyword(name))
            {
                chat.DisplayName = DeviceAnonymizer.SanitizeDisplayName(name, chat.DeviceAddress);
            }

            string statusDesc = state switch
            {
                DeviceConnectionState.Connected => "подключено",
                DeviceConnectionState.Connecting => "подключение...",
                _ => "отключено"
            };
            string friendlyName = !string.IsNullOrWhiteSpace(chat.DisplayName) && !DeviceAnonymizer.IsHardwareIdentifier(chat.DisplayName)
                ? chat.DisplayName
                : "Устройство";
            StatusMessage = $"{friendlyName}: {statusDesc}";

            if (state == DeviceConnectionState.Connected)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(400);
                    string target = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;

                    // 1. Send Presence ONLINE
                    try { await SendNetworkPacketAsync(target, PacketType.Presence, System.Text.Encoding.UTF8.GetBytes("ONLINE")); } catch { }
                    await Task.Delay(150);

                    // 2. Broadcast Profile & Avatar
                    await BroadcastMyProfileAndAvatarAsync();
                    await Task.Delay(150);

                });
            }
        });
    }

    private void OnPacketReceived(string fromAddress, PacketType type, byte[] payload)

    {
        Application.Current.Dispatcher.Invoke(() =>

        {
            var chat = FindChatByAddressOrPeer(fromAddress);

            // Handle ChatDeleted robustly before device resolution
            if (type == PacketType.ChatDeleted)
            {
                string delPayload = payload != null ? System.Text.Encoding.UTF8.GetString(payload) : "";
                string? delName = null;
                string? delLogin = null;
                if (delPayload.StartsWith("DELETE_CHAT:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = delPayload.Substring(12).Split(';');
                    foreach (var p in parts)
                    {
                        if (p.StartsWith("name=", StringComparison.OrdinalIgnoreCase)) delName = p.Substring(5).Trim();
                        else if (p.StartsWith("login=", StringComparison.OrdinalIgnoreCase)) delLogin = p.Substring(6).Trim();
                    }
                }

                var targetChat = chat ?? _allChats.FirstOrDefault(c =>
                    (!string.IsNullOrEmpty(delLogin) && string.Equals(c.Username, delLogin, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(delName) && string.Equals(c.DisplayName, delName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(c.DeviceAddress) && (string.Equals(c.DeviceAddress, fromAddress, StringComparison.OrdinalIgnoreCase) || string.Equals(c.PeerId, fromAddress, StringComparison.OrdinalIgnoreCase))));

                if (targetChat != null)
                {
                    try
                    {
                        _ = _bluetoothService.DisconnectAsync(targetChat.DeviceAddress);
                        _ = _companionService.DisconnectAsync(targetChat.DeviceAddress);
                    }
                    catch { }

                    MarkDeviceForgotten(targetChat.DeviceAddress);
                    MarkDeviceForgotten(targetChat.PeerId);
                    if (!string.IsNullOrEmpty(targetChat.Username)) MarkDeviceForgotten(targetChat.Username);
                    _ = _storageService.SaveSettingsAsync(Settings);

                    _bluetoothService.ForgetDevice(targetChat.DeviceAddress);
                    _bluetoothService.ForgetDevice(targetChat.PeerId);
                    _companionService.ForgetDevice(targetChat.DeviceAddress);
                    _companionService.ForgetDevice(targetChat.PeerId);

                    var disc = DiscoveredDevices.FirstOrDefault(d => d.PeerId == targetChat.PeerId || d.DeviceAddress == targetChat.DeviceAddress);
                    if (disc != null) DiscoveredDevices.Remove(disc);

                    _allChats.Remove(targetChat);

                    AvatarManager.DeleteAvatar(targetChat.DeviceAddress);
                    AvatarManager.DeleteAvatar(targetChat.PeerId);
                    if (!string.IsNullOrEmpty(targetChat.Username)) AvatarManager.DeleteAvatar(targetChat.Username);

                    ApplySearchFilter();

                    bool isCurrent = SelectedChat == null || SelectedChat == targetChat ||
                                     (!string.IsNullOrEmpty(targetChat.PeerId) && SelectedChat.PeerId == targetChat.PeerId) ||
                                     (!string.IsNullOrEmpty(targetChat.DeviceAddress) && SelectedChat.DeviceAddress == targetChat.DeviceAddress);
                    if (isCurrent)
                    {
                        IsMediaDrawerOpen = false;
                        SelectedChat = null;
                    }
                    _ = _storageService.SaveChatsAsync(_allChats);

                    MainWindow.TrayService?.ShowNotification("Прайм Мессенджер", $"Собеседник {targetChat.DisplayName} удалил чат.");
                    StatusMessage = $"Собеседник {targetChat.DisplayName} удалил чат. Устройство забыто";
                }
                return;
            }

            if (chat == null)
            {
                // Ignore disconnect packets for non-existent chats
                if (type == PacketType.Disconnect)
                {
                    return;
                }

                string rawHw = _bluetoothService.ResolveHardwareAddress(fromAddress);
                if (IsSelfDevice(fromAddress) || IsSelfDevice(rawHw))
                {
                    return;
                }
                if (IsDeviceForgotten(fromAddress) || IsDeviceForgotten(rawHw))
                {
                    return;
                }

                string peerId = _bluetoothService.ResolvePeerId(fromAddress);

                chat = new ChatItem
                {
                    DeviceAddress = !string.IsNullOrWhiteSpace(rawHw) ? rawHw : fromAddress,
                    PeerId = !string.IsNullOrWhiteSpace(peerId) ? peerId : DeviceAnonymizer.GetOpaquePeerId(fromAddress),
                    DisplayName = DeviceAnonymizer.SanitizeDisplayName(null, fromAddress),
                    Username = DeviceAnonymizer.SanitizeUsername(null, fromAddress),
                    IsBluetoothDevice = true,
                    IsOnline = true
                };

                _allChats.Add(chat);
                ApplySearchFilter();
            }

            switch (type)

            {
                case PacketType.Text:

                    string rawText = System.Text.Encoding.UTF8.GetString(payload);

                    if (rawText.StartsWith("HANDSHAKE:", StringComparison.OrdinalIgnoreCase) ||

                        rawText.StartsWith("HANDSHAKE_ACK:", StringComparison.OrdinalIgnoreCase))

                    {
                        var (hName, hLogin, hPresence, isAck) = PrimePacket.ParseHandshakePayload(payload);

                        if (!string.IsNullOrEmpty(hName)) chat.DisplayName = DeviceAnonymizer.SanitizeDisplayName(hName, fromAddress);

                        if (!string.IsNullOrEmpty(hLogin)) chat.Username = DeviceAnonymizer.SanitizeUsername(hLogin, fromAddress);

                        chat.IsOnline = true;

                        chat.ConnectionState = DeviceConnectionState.Connected;

                        // Auto-reply to Handshake if passive peer

                        if (!isAck)

                        {
                            _ = Task.Run(async () =>

                            {
                                try

                                {
                                    byte[] ackBytes = PrimePacket.BuildHandshakePayload(Settings.MyDisplayName, Settings.MyUsername, isAck: true);

                                    
                                        await _bluetoothService.SendPacketAsync(chat.DeviceAddress, PacketType.Text, ackBytes);

                                    byte[] presBytes = System.Text.Encoding.UTF8.GetBytes("ONLINE");

                                    
                                        await _bluetoothService.SendPacketAsync(chat.DeviceAddress, PacketType.Presence, presBytes);

                                }

                                catch { }

                            });

                        }

                        _ = _storageService.SaveChatsAsync(_allChats);

                        return; // Do NOT add Handshake as a chat message!

                    }

                    var (msgId, text, rId, rSender, rText) = PrimePacket.ParseTextPayload(payload);

                    // Deduplicate message
                    if (!string.IsNullOrEmpty(msgId) && chat.Messages.Any(m => m.Id == msgId))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                byte[] ackPayload = System.Text.Encoding.UTF8.GetBytes(msgId);
                                string addr = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;
                                await _bluetoothService.SendPacketAsync(addr, PacketType.ReadReceipt, ackPayload);
                            }
                            catch { }
                        });
                        break;
                    }

                    var msg = new ChatMessage

                    {
                        Id = msgId,

                        Sender = chat.DisplayName,

                        Text = text,

                        IsFromMe = false,

                        FormattedTime = DateTime.Now.ToString("HH:mm"),

                        ReplyToId = rId,

                        ReplyToSender = rSender,

                        ReplyToText = rText

                    };

                    chat.Messages.Add(msg);

                    chat.LastMessage = text;

                    chat.LastMessageTime = msg.FormattedTime;

                    if (SelectedChat != chat) chat.UnreadCount++;
                    NotifyIncomingMessage(chat, text);

                    _ = Task.Run(async () =>

                    {
                        try

                        {
                            byte[] ackPayload = System.Text.Encoding.UTF8.GetBytes(msgId);

                            
                                await _bluetoothService.SendPacketAsync(chat.DeviceAddress, PacketType.ReadReceipt, ackPayload);

                        }

                        catch { }

                    });

                    break;

                case PacketType.Photo:
                    var photoResult = PrimePacket.ParsePhotoPayload(payload);
                    if (!string.IsNullOrEmpty(photoResult.MsgId) && chat.Messages.Any(m => m.Id == photoResult.MsgId))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                byte[] ackPayload = System.Text.Encoding.UTF8.GetBytes(photoResult.MsgId);
                                string addr = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;
                                await _bluetoothService.SendPacketAsync(addr, PacketType.ReadReceipt, ackPayload);
                            }
                            catch { }
                        });
                        break;
                    }
                    string mediaDir = StorageService.MediaDirectory;
                    Directory.CreateDirectory(mediaDir);

                    var photoMsg = new ChatMessage
                    {
                        Id = photoResult.MsgId,
                        Sender = chat.DisplayName,
                        Text = photoResult.Caption,
                        Type = PacketType.Photo,
                        IsFromMe = false,
                        FormattedTime = DateTime.Now.ToString("HH:mm")
                    };

                    if (photoResult.IsCollage)
                    {
                        for (int i = 0; i < photoResult.Items.Count; i++)
                        {
                            string cPath = Path.Combine(mediaDir, $"collage_{photoResult.MsgId}_{i}.jpg");
                            File.WriteAllBytes(cPath, photoResult.Items[i]);
                            photoMsg.CollagePaths.Add(cPath);
                        }
                        photoMsg.FilePath = photoMsg.CollagePaths.FirstOrDefault();
                        chat.LastMessage = string.IsNullOrWhiteSpace(photoResult.Caption) ? "📷 Коллаж" : $"📷 {photoResult.Caption}";
                    }
                    else if (photoResult.Items.Count > 0)
                    {
                        string sPath = Path.Combine(mediaDir, $"photo_{photoResult.MsgId}.jpg");
                        File.WriteAllBytes(sPath, photoResult.Items[0]);
                        photoMsg.FilePath = sPath;
                        photoMsg.FileName = Path.GetFileName(sPath);
                        photoMsg.FileSizeBytes = photoResult.Items[0].Length;
                        chat.LastMessage = string.IsNullOrWhiteSpace(photoResult.Caption) ? "📷 Фотография" : $"📷 {photoResult.Caption}";
                    }

                    chat.Messages.Add(photoMsg);
                    chat.LastMessageTime = photoMsg.FormattedTime;
                    if (SelectedChat != chat) chat.UnreadCount++;
                    NotifyIncomingMessage(chat, chat.LastMessage);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            byte[] ackPayload = System.Text.Encoding.UTF8.GetBytes(photoResult.MsgId);
                            string addr = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;
                            await _bluetoothService.SendPacketAsync(addr, PacketType.ReadReceipt, ackPayload);
                        }
                        catch { }
                    });
                    break;

                case PacketType.File:
                    var (fMsgId, fName, fDuration, fIsVoice, fBytes, fCaption) = PrimePacket.ParseFilePayload(payload);
                    if (!string.IsNullOrEmpty(fMsgId) && chat.Messages.Any(m => m.Id == fMsgId))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                byte[] ackPayload = System.Text.Encoding.UTF8.GetBytes(fMsgId);
                                string addr = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;
                                await _bluetoothService.SendPacketAsync(addr, PacketType.ReadReceipt, ackPayload);
                            }
                            catch { }
                        });
                        break;
                    }
                    string fDir = StorageService.MediaDirectory;
                    Directory.CreateDirectory(fDir);
                    string fPath = Path.Combine(fDir, $"file_{DateTime.Now:yyyyMMdd_HHmmss}_{fName}");
                    File.WriteAllBytes(fPath, fBytes);

                    string lowerName = fName.ToLowerInvariant();
                    bool isVoiceMsg = fIsVoice || lowerName.Contains("voice_") || lowerName.StartsWith("rec_voice_") || lowerName.EndsWith(".opus");
                    bool isAudioMsg = !isVoiceMsg && (lowerName.EndsWith(".mp3") || lowerName.EndsWith(".m4a") || lowerName.EndsWith(".aac") || lowerName.EndsWith(".wav") || lowerName.EndsWith(".ogg") || lowerName.EndsWith(".flac"));
                    bool isPhotoMsg = !isVoiceMsg && !isAudioMsg && (lowerName.EndsWith(".jpg") || lowerName.EndsWith(".jpeg") || lowerName.EndsWith(".png") || lowerName.EndsWith(".webp") || lowerName.EndsWith(".gif"));
                    bool isVideoFile = !isVoiceMsg && !isAudioMsg && !isPhotoMsg && (lowerName.EndsWith(".mp4") || lowerName.EndsWith(".mkv") || lowerName.EndsWith(".mov") || lowerName.EndsWith(".avi") || lowerName.EndsWith(".webm"));

                    // A Video Note (кружочек) is strictly a video specifically tagged with VIDEO_NOTE or with vn_/rec_circle_/rec_videonote_ prefix
                    bool isVideoNote = isVideoFile && (
                        (!string.IsNullOrEmpty(fCaption) && fCaption.StartsWith("VIDEO_NOTE:", StringComparison.OrdinalIgnoreCase)) ||
                        lowerName.StartsWith("vn_") ||
                        lowerName.StartsWith("rec_circle_") ||
                        lowerName.StartsWith("rec_videonote_")
                    );

                    int parsedRadius = 50;
                    string shapeTag = "";
                    if (!string.IsNullOrEmpty(fCaption) && fCaption.StartsWith("VIDEO_NOTE:", StringComparison.OrdinalIgnoreCase))
                    {
                        shapeTag = fCaption.Substring("VIDEO_NOTE:".Length).Trim();
                    }
                    else if (fName.StartsWith("vn_", StringComparison.OrdinalIgnoreCase))
                    {
                        string withoutExt = Path.GetFileNameWithoutExtension(fName);
                        var fnParts = withoutExt.Split('_');
                        if (fnParts.Length >= 3) shapeTag = fnParts[2].Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(shapeTag))
                    {
                        if (int.TryParse(shapeTag, out int r))
                            parsedRadius = Math.Clamp(r, 0, 50);
                        else if (shapeTag.Equals("square", StringComparison.OrdinalIgnoreCase))
                            parsedRadius = 0;
                        else if (shapeTag.Equals("circle", StringComparison.OrdinalIgnoreCase))
                            parsedRadius = 50;
                        else if (shapeTag.Equals("star", StringComparison.OrdinalIgnoreCase))
                            parsedRadius = 35;
                        else if (shapeTag.Equals("prime", StringComparison.OrdinalIgnoreCase))
                            parsedRadius = 25;
                    }

                    var incomingFileMsg = new ChatMessage
                    {
                        Id = fMsgId,
                        Sender = chat.DisplayName,
                        Text = fName,
                        FilePath = fPath,
                        FileName = fName,
                        FileSizeBytes = fBytes.Length,
                        Duration = !string.IsNullOrWhiteSpace(fDuration) && fDuration != "00:00" ? fDuration : (isVoiceMsg ? "0:00" : ""),
                        Type = isPhotoMsg ? PacketType.Photo : (isVoiceMsg ? PacketType.Voice : PacketType.File),
                        IsVideoNote = isVideoNote,
                        VideoNoteRadiusPercent = parsedRadius,
                        IsFromMe = false,
                        FormattedTime = DateTime.Now.ToString("HH:mm")
                    };

                    chat.Messages.Add(incomingFileMsg);
                    if (isVoiceMsg) chat.LastMessage = "🎤 Голосовое сообщение";
                    else if (isVideoNote) chat.LastMessage = "📹 Видеосообщение";
                    else if (isVideoFile) chat.LastMessage = $"🎥 {fName}";
                    else if (isPhotoMsg) chat.LastMessage = $"📷 {fName}";
                    else if (isAudioMsg) chat.LastMessage = $"🎵 {fName}";
                    else chat.LastMessage = $"📎 {fName}";
                    chat.LastMessageTime = incomingFileMsg.FormattedTime;
                    if (SelectedChat != chat) chat.UnreadCount++;
                    NotifyIncomingMessage(chat, chat.LastMessage);
                    OnPropertyChanged(nameof(SharedMedia));
                    OnPropertyChanged(nameof(SharedMediaList));
                    OnPropertyChanged(nameof(SharedFiles));
                    OnPropertyChanged(nameof(SharedFileList));
                    OnPropertyChanged(nameof(SharedAudio));

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            byte[] ackPayload = System.Text.Encoding.UTF8.GetBytes(fMsgId);
                            string addr = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;
                            await _bluetoothService.SendPacketAsync(addr, PacketType.ReadReceipt, ackPayload);
                        }
                        catch { }
                    });
                    break;

                case PacketType.Typing:
                    string typingData = System.Text.Encoding.UTF8.GetString(payload).Trim();
                    string action = "TYPING";
                    if (typingData.StartsWith("STATE:", StringComparison.OrdinalIgnoreCase))
                    {
                        action = typingData.Substring(6).Trim();
                    }
                    else if (!string.IsNullOrEmpty(typingData))
                    {
                        action = typingData;
                    }

                    string chatIdKey = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;
                    if (string.IsNullOrWhiteSpace(chatIdKey)) chatIdKey = chat.DisplayName;

                    if (_chatActivityTimers.TryGetValue(chatIdKey, out var existingTimer))
                    {
                        existingTimer.Stop();
                        _chatActivityTimers.Remove(chatIdKey);
                    }

                    if (string.Equals(action, "IDLE", StringComparison.OrdinalIgnoreCase))
                    {
                        chat.IsTyping = false;
                        chat.ActivityState = "IDLE";
                    }
                    else
                    {
                        chat.ActivityState = action;
                        chat.IsTyping = action.Equals("TYPING", StringComparison.OrdinalIgnoreCase);

                        double timeoutSeconds = 5.0;
                        var upper = action.ToUpperInvariant();
                        if (upper.StartsWith("RECORDING") || upper.StartsWith("SENDING")) timeoutSeconds = 12.0;
                        else if (upper.StartsWith("SELECTING") || upper.StartsWith("VIEWING") || upper.StartsWith("LISTENING")) timeoutSeconds = 7.0;
                        else if (upper == "TYPING") timeoutSeconds = 4.5;

                        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
                        timer.Tick += (s, e) =>
                        {
                            timer.Stop();
                            _chatActivityTimers.Remove(chatIdKey);
                            if (chat.ActivityState == action)
                            {
                                chat.ActivityState = "IDLE";
                                chat.IsTyping = false;
                            }
                        };
                        _chatActivityTimers[chatIdKey] = timer;
                        timer.Start();
                    }
                    break;

                case PacketType.Edit:
                    var (editId, editText) = PrimePacket.ParseEditMessagePayload(payload);
                    var msgToEdit = chat.Messages.FirstOrDefault(m => m.Id == editId);
                    if (msgToEdit != null)
                    {
                        msgToEdit.Text = editText;
                        msgToEdit.IsEdited = true;
                        if (chat.Messages.LastOrDefault() == msgToEdit)
                        {
                            chat.LastMessage = editText;
                        }
                    }
                    break;

                case PacketType.DeleteMessage:
                    string delId = PrimePacket.ParseDeleteMessagePayload(payload);
                    var msgToDelete = chat.Messages.FirstOrDefault(m => m.Id == delId);
                    if (msgToDelete != null)
                    {
                        chat.Messages.Remove(msgToDelete);
                        var newLast = chat.Messages.LastOrDefault();
                        chat.LastMessage = newLast?.Text ?? "";
                        chat.LastMessageTime = newLast?.FormattedTime ?? "";
                    }
                    break;

                case PacketType.Voice:

                    string voiceDir = StorageService.MediaDirectory;

                    Directory.CreateDirectory(voiceDir);

                    string voicePath = Path.Combine(voiceDir, $"voice_recv_{Guid.NewGuid():N}.wav");

                    File.WriteAllBytes(voicePath, payload);

                    var voiceMsg = new ChatMessage

                    {
                        Sender = chat.DisplayName,

                        FilePath = voicePath,

                        FileName = Path.GetFileName(voicePath),

                        FileSizeBytes = payload.Length,

                        Duration = "0:00",

                        IsFromMe = false,

                        Type = PacketType.Voice,

                        FormattedTime = DateTime.Now.ToString("HH:mm")

                    };

                    chat.Messages.Add(voiceMsg);

                    chat.LastMessage = "🎤 Голосовое сообщение";

                    chat.LastMessageTime = voiceMsg.FormattedTime;

                    if (SelectedChat != chat) chat.UnreadCount++;
                    NotifyIncomingMessage(chat, chat.LastMessage);

                    break;

                case PacketType.Reaction:

                    var (rxMsgId, rxEmoji, rxAuthor) = PrimePacket.ParseReactionPayload(payload);

                    App.Log($"[Reaction] Received: msgId='{rxMsgId}', emoji='{rxEmoji}', author='{rxAuthor}'");

                    var targetMsg = chat.Messages.FirstOrDefault(m => m.Id == rxMsgId);

                    if (targetMsg == null && !string.IsNullOrEmpty(rxMsgId))
                    {
                        targetMsg = chat.Messages.FirstOrDefault(m => m.Id.Contains(rxMsgId) || rxMsgId.Contains(m.Id));
                    }

                    if (targetMsg == null)
                    {
                        targetMsg = chat.Messages.LastOrDefault();
                    }

                    if (targetMsg != null)

                    {
                        targetMsg.Reaction = rxEmoji;

                        targetMsg.ReactionAuthor = rxAuthor;

                        App.Log($"[Reaction] Updated message '{targetMsg.Id}' with reaction '{rxEmoji}' by '{rxAuthor}'");

                    }

                    break;

                case PacketType.ProfileUpdate:

                    var (newName, newLogin) = PrimePacket.ParseProfileUpdatePayload(payload);

                    if (!string.IsNullOrEmpty(newName)) chat.DisplayName = DeviceAnonymizer.SanitizeDisplayName(newName);

                    if (!string.IsNullOrEmpty(newLogin)) chat.Username = DeviceAnonymizer.SanitizeUsername(newLogin);

                    break;

                case PacketType.Avatar:
                    try
                    {
                        if (payload == null || payload.Length == 0)
                        {
                            chat.AvatarPath = null;
                            chat.AvatarHistory.Clear();
                            AvatarManager.DeleteAvatar(chat.PeerId);
                            if (!string.IsNullOrEmpty(chat.DeviceAddress))
                                AvatarManager.DeleteAvatar(chat.DeviceAddress);
                            _ = _storageService.SaveChatsAsync(_allChats);
                            break;
                        }

                        // Fast check: if payload is identical to current avatar, ignore duplicate update
                        string? currentPath = chat.CurrentAvatarDisplayPath ?? chat.AvatarPath;
                        if (!string.IsNullOrEmpty(currentPath) && File.Exists(currentPath))
                        {
                            string? currentHash = AvatarManager.ComputeFileSha256(currentPath);
                            string incomingHash = AvatarManager.ComputeSha256(payload);
                            if (!string.IsNullOrEmpty(currentHash) && string.Equals(currentHash, incomingHash, StringComparison.OrdinalIgnoreCase))
                            {
                                // Byte-for-byte identical, no change!
                                break;
                            }
                        }

                        string? newAvatarPath = AvatarManager.SaveAvatarBytesToHistory(chat.PeerId, payload);
                        if (!string.IsNullOrEmpty(newAvatarPath))
                        {
                            chat.AddAvatarToHistory(newAvatarPath);
                            chat.DeduplicateAvatarHistory();
                            App.Log($"[Avatar] Saved to history and updated avatar for '{chat.DisplayName}' ({payload.Length} bytes)");
                            _ = _storageService.SaveChatsAsync(_allChats);
                        }
                    }
                    catch (Exception avEx)
                    {
                        App.Log($"[Avatar ERROR] Failed to save avatar for '{chat.PeerId}': {avEx.Message}");
                    }
                    break;

                case PacketType.ReadReceipt:

                    string ackId = System.Text.Encoding.UTF8.GetString(payload).Trim();

                    var myMsg = chat.Messages.FirstOrDefault(m => m.Id == ackId);

                    if (myMsg != null)

                    {
                        myMsg.IsDelivered = true;

                        myMsg.IsRead = true;

                    }

                    break;

                case PacketType.Ping:

                    chat.IsOnline = true;

                    break;

                case PacketType.Ack:

                    chat.IsOnline = true;

                    break;

                case PacketType.Presence:

                    string pres = System.Text.Encoding.UTF8.GetString(payload).Trim();

                    chat.IsOnline = pres.StartsWith("ONLINE", StringComparison.OrdinalIgnoreCase);
                    chat.ConnectionState = chat.IsOnline ? DeviceConnectionState.Connected : DeviceConnectionState.Disconnected;

                    break;


                case PacketType.ChatDeleted:
                    try
                    {
                        _ = _bluetoothService.DisconnectAsync(chat.DeviceAddress);
                        _ = _companionService.DisconnectAsync(chat.DeviceAddress);
                    }
                    catch { }

                    MarkDeviceForgotten(chat.DeviceAddress);
                    MarkDeviceForgotten(chat.PeerId);
                    _ = _storageService.SaveSettingsAsync(Settings);

                    _bluetoothService.ForgetDevice(chat.DeviceAddress);
                    _bluetoothService.ForgetDevice(chat.PeerId);
                    _companionService.ForgetDevice(chat.DeviceAddress);
                    _companionService.ForgetDevice(chat.PeerId);

                    var disc = DiscoveredDevices.FirstOrDefault(d => d.PeerId == chat.PeerId || d.DeviceAddress == chat.DeviceAddress);
                    if (disc != null) DiscoveredDevices.Remove(disc);

                    _allChats.Remove(chat);

                    AvatarManager.DeleteAvatar(chat.DeviceAddress);
                    AvatarManager.DeleteAvatar(chat.PeerId);

                    ApplySearchFilter();

                    bool isCurrent = SelectedChat == null || SelectedChat == chat || (!string.IsNullOrEmpty(chat.PeerId) && SelectedChat.PeerId == chat.PeerId) || (!string.IsNullOrEmpty(chat.DeviceAddress) && SelectedChat.DeviceAddress == chat.DeviceAddress);
                    if (isCurrent)
                    {
                        IsMediaDrawerOpen = false;
                        SelectedChat = null;
                    }
                    _ = _storageService.SaveChatsAsync(_allChats);

                    MainWindow.TrayService?.ShowNotification("Прайм Мессенджер", $"Собеседник {chat.DisplayName} удалил чат.");
                    StatusMessage = $"Собеседник {chat.DisplayName} удалил чат. Устройство забыто";
                    break;

            }

            _ = _storageService.SaveChatsAsync(_allChats);

        });

    }

    private void NotifyIncomingMessage(ChatItem chat, string messageText)
    {
        if (chat == null || chat.IsMuted) return;

        SoundEffectService.PlayReceived(Settings);

        if (Settings.ShowToastNotifications)
        {
            bool isCurrentChatFocused = Application.Current != null &&
                                        Application.Current.MainWindow != null &&
                                        Application.Current.MainWindow.IsActive &&
                                        SelectedChat == chat;
            if (!isCurrentChatFocused)
            {
                string preview = Settings.ShowMessagePreviewInNotifications ? messageText : "Новое сообщение";
                ToastNotificationService.Show(chat, preview, targetChat =>
                {
                    if (Application.Current != null && Application.Current.MainWindow != null)
                    {
                        if (Application.Current.MainWindow.WindowState == WindowState.Minimized)
                            Application.Current.MainWindow.WindowState = WindowState.Normal;
                        Application.Current.MainWindow.Activate();
                    }
                    SelectedChat = targetChat;
                });
            }
        }
        else
        {
            MainWindow.TrayService?.ShowNotification(chat.DisplayName, messageText);
        }
    }

    private void OnNetworkError(string source, string message)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var chat = FindChatByAddressOrPeer(source);
            string sender = chat != null && !DeviceAnonymizer.IsHardwareIdentifier(chat.DisplayName)
                ? chat.DisplayName
                : "Связь";
            string cleanMsg = SanitizeStatusText(message);
            StatusMessage = $"{sender}: {cleanMsg}";
        });
    }
}

