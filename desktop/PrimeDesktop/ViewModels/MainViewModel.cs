using System;

using System.Collections.ObjectModel;

using System.IO;

using System.Linq;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Input;

using Microsoft.Win32;

using PrimeDesktop.Models;

using PrimeDesktop.Network;

using PrimeDesktop.Services;

namespace PrimeDesktop.ViewModels;

public class MainViewModel : ViewModelBase

{
    private readonly StorageService _storageService = new();

    private readonly AudioService _audioService = new();

    private readonly BluetoothRfcommService _bluetoothService = new();

    // Local network (Wi-Fi) disabled - purely Bluetooth RFCOMM

    private ObservableCollection<ChatItem> _allChats = new();

    private ObservableCollection<ChatItem> _filteredChats = new();

    private ChatItem? _selectedChat;

    private string _searchQuery = string.Empty;

    private string _inputMessageText = string.Empty;

    private ChatMessage? _replyMessage;

    private bool _isSettingsOpen;

    private bool _isMediaDrawerOpen;

    private bool _isScanning;

    private string _statusMessage = "Ready";

    private AppSettings _settings = new();

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
                if (_selectedChat != null)
                {
                    IsReadingMode = false;
                    OnPropertyChanged(nameof(SharedMedia));
                    OnPropertyChanged(nameof(SharedFiles));
                    OnPropertyChanged(nameof(SharedAudio));

                    _ = _storageService.SaveChatsAsync(_allChats);

                    if (Settings != null && Settings.AutoConnectBluetooth && _selectedChat.IsBluetoothDevice && 
                        !_selectedChat.IsOnline && _selectedChat.ConnectionState != DeviceConnectionState.Connecting &&
                        !_selectedChat.PeerId.StartsWith("peer_prime"))
                    {
                        _ = ConnectSelectedChatAsync();
                    }
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

    public string InputMessageText

    {
        get => _inputMessageText;

        set

        {
            if (SetProperty(ref _inputMessageText, value))

            {
                NotifyTyping();

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

        set => SetProperty(ref _statusMessage, value);

    }

    public AppSettings Settings

    {
        get => _settings;

        set => SetProperty(ref _settings, value);

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

    public ICommand ToggleMediaDrawerCommand { get; }

    public ICommand ConnectCompanionWifiCommand { get; }

    public ICommand ChangeAccentColorCommand { get; }

    public ICommand ChangeContactAvatarCommand { get; }

    public ICommand ChangeMyAvatarCommand { get; }
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
                _ = _storageService.SaveSettingsAsync(Settings);
            }
        }
    }
    public CornerRadius VideoNoteCornerRadius => new(50.0 * Settings.VideoNoteRadiusPercent / 100.0);

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

    public System.Collections.Generic.IEnumerable<ChatMessage> SharedMedia =>
        SelectedChat?.Messages.Where(m => m.IsPhoto || m.IsVideoNote || (!string.IsNullOrEmpty(m.FileName) && (m.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || m.FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || m.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || m.FileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || m.FileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || m.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)))) ?? Enumerable.Empty<ChatMessage>();

    public System.Collections.Generic.IEnumerable<ChatMessage> SharedFiles =>
        SelectedChat?.Messages.Where(m => m.IsFile && !m.IsMusic && !m.IsVoice && !m.IsPhoto) ?? Enumerable.Empty<ChatMessage>();

    public System.Collections.Generic.IEnumerable<ChatMessage> SharedAudio =>
        SelectedChat?.Messages.Where(m => m.IsVoice || m.IsMusic || (!string.IsNullOrEmpty(m.FileName) && (m.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || m.FileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || m.FileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) || m.FileName.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) || m.FileName.EndsWith(".opus", StringComparison.OrdinalIgnoreCase) || m.FileName.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)))) ?? Enumerable.Empty<ChatMessage>();

    public MainViewModel()

    {
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

        AttachPhotoCommand = new RelayCommand(async () => await SendAttachmentAsync(PacketType.Photo), () => HasSelectedChat);

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
                string addr = !string.IsNullOrWhiteSpace(SelectedChat.DeviceAddress) ? SelectedChat.DeviceAddress : SelectedChat.PeerId;
                await _bluetoothService.DisconnectAsync(addr);
                SelectedChat.IsOnline = false;
                SelectedChat.ConnectionState = DeviceConnectionState.Disconnected;
                StatusMessage = "Режим чтения включен (связь принудительно отключена)";
            }
            else
            {
                await ConnectSelectedChatAsync();
                StatusMessage = $"Подключение к {SelectedChat.DisplayName}...";
            }
        }, () => HasSelectedChat);

        ToggleSettingsCommand = new RelayCommand(() =>
        {
            IsSettingsOpen = !IsSettingsOpen;
            if (IsSettingsOpen) { UpdateMediaCacheSize(); _ = CalculateStorageBreakdownAsync(); }
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
            var res = MessageBox.Show(
                "Вы уверены, что хотите удалить все чаты и историю сообщений?",
                "Очистка всех чатов",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
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
                string? saved = AvatarManager.SaveAvatar(SelectedChat.DeviceAddress, dlg.FileName);

                if (saved != null)

                {
                    SelectedChat.AvatarPath = saved;

                    _ = _storageService.SaveChatsAsync(_allChats);

                }

            }

        }, () => HasSelectedChat);

        ChangeMyAvatarCommand = new RelayCommand(() =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите свой аватар",
                Filter = "Изображения (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Все файлы (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                string? saved = AvatarManager.SaveAvatar("my_profile", dlg.FileName);
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

                    StatusMessage = "Запись голосового сообщения...";

                }

            }

            else

            {
                var (path, duration) = _audioService.StopRecording();

                IsRecordingVoice = false;

                StatusMessage = "Запись завершена";

                if (!string.IsNullOrEmpty(path))

                {
                    await SendVoiceMessageAsync(path, duration);

                }

            }

        }, () => HasSelectedChat);

        PlayVoiceMessageCommand = new RelayCommand(param =>

        {
            if (param is ChatMessage msg && !string.IsNullOrEmpty(msg.FilePath))

            {
                if (msg.IsPlaying)

                {
                    _audioService.Stop();

                    msg.IsPlaying = false;

                }

                else

                {
                    if (SelectedChat != null)

                    {
                        foreach (var m in SelectedChat.Messages) m.IsPlaying = false;

                    }

                    msg.IsPlaying = true;

                    _audioService.Play(msg.FilePath, () =>

                    {
                        Application.Current.Dispatcher.Invoke(() => msg.IsPlaying = false);

                    });

                }

            }

        });

        OpenPhotoViewerCommand = new RelayCommand(param =>

        {
            if (param is string path && File.Exists(path))

            {
                ViewedPhotoPath = path;

                IsPhotoViewerOpen = true;

            }

        });

        ClosePhotoViewerCommand = new RelayCommand(() =>

        {
            IsPhotoViewerOpen = false;

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
                var existing = _allChats.FirstOrDefault(c => c.PeerId.Equals(item.PeerId, StringComparison.OrdinalIgnoreCase));

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
            if (param is string p && File.Exists(p))

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
            await _storageService.SaveSettingsAsync(Settings);

            if (Application.Current.MainWindow != null)

            {
                SecurityManager.SetScreenshotProtection(Application.Current.MainWindow, Settings.EnableScreenshotProtection);

            }

            IsSettingsOpen = false;
            await BroadcastMyProfileAndAvatarAsync();

            StatusMessage = "Настройки успешно сохранены";

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
                foreach (var c in chats)
                {
                    // Filter out corrupt / error-named chats
                    if (IsStatusKeyword(c.DisplayName) || c.DisplayName == "Ошибка подключения" || c.DisplayName == "Установка связи...")
                    {
                        App.Log($"[Init] Skipping corrupt saved chat '{c.DisplayName}' ({c.PeerId})");
                        continue;
                    }

                    if (string.IsNullOrEmpty(c.AvatarPath))
                    {
                        c.AvatarPath = AvatarManager.GetAvatarPath(c.DeviceAddress) ?? AvatarManager.GetAvatarPath(c.PeerId);
                    }
                    if (!string.IsNullOrWhiteSpace(c.DeviceAddress) && !string.IsNullOrWhiteSpace(c.PeerId))
                    {
                        _bluetoothService.RegisterPeerAddress(c.PeerId, c.DeviceAddress);
                    }
                    _allChats.Add(c);
                }

                // If any corrupt chats were filtered, persist cleaned list
                if (_allChats.Count != chats.Count)
                {
                    _ = _storageService.SaveChatsAsync(_allChats);
                }

                if (_allChats.Count == 0 && !_storageService.HasChatsFile)
                {
                    var welcomeChat = new ChatItem
                    {
                        DisplayName = "Команда Прайм",
                        Username = "prime_support",
                        PeerId = "peer_prime_support",
                        LastMessage = "Всё нужное без лишнего!",
                        LastMessageTime = DateTime.Now.ToString("HH:mm"),
                        IsOnline = true
                    };

                    welcomeChat.Messages.Add(new ChatMessage
                    {
                        Sender = "Команда Прайм",
                        Text = @"Добро пожаловать в компьютерную версию мессенджера «Прайм»!

Всё нужное без лишнего!

Поддерживается прямое защищенное общение по Bluetooth без серверов.",
                        IsFromMe = false,
                        FormattedTime = DateTime.Now.ToString("HH:mm")
                    });

                    _allChats.Add(welcomeChat);
                }

                ApplySearchFilter();
                SelectedChat = _filteredChats.FirstOrDefault();
            });

            // Start background servers
            _bluetoothService.MyDisplayName = Settings.MyDisplayName;
            _bluetoothService.MyUsername = Settings.MyUsername;
            App.Log($"[Init] Starting Bluetooth RFCOMM server for '{Settings.MyDisplayName}'...");
            await _bluetoothService.StartServerAsync();

            // Automatically discover paired Bluetooth devices and add them to chats if not present
            var paired = await _bluetoothService.DiscoverDevicesAsync();
            App.Log($"[Init] Discovered {paired.Count} paired devices.");
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var dev in paired)
                {
                    if (!string.IsNullOrWhiteSpace(dev.DeviceAddress) && !string.IsNullOrWhiteSpace(dev.PeerId))
                    {
                        _bluetoothService.RegisterPeerAddress(dev.PeerId, dev.DeviceAddress);

                        var existing = _allChats.FirstOrDefault(c => c.PeerId.Equals(dev.PeerId, StringComparison.OrdinalIgnoreCase));
                        if (existing != null && string.IsNullOrWhiteSpace(existing.DeviceAddress))
                        {
                            existing.DeviceAddress = dev.DeviceAddress;
                        }
                    }

                    if (!_allChats.Any(c => c.PeerId.Equals(dev.PeerId, StringComparison.OrdinalIgnoreCase) ||
                                            (!string.IsNullOrEmpty(dev.DeviceAddress) && c.DeviceAddress.Equals(dev.DeviceAddress, StringComparison.OrdinalIgnoreCase))))
                    {
                        _allChats.Add(dev);
                        App.Log($"[Init] Added paired device '{dev.DisplayName}' ({dev.PeerId}) to chat list.");
                    }
                }
                ApplySearchFilter();

                // If AutoConnectBluetooth is on, connect to paired devices
                if (Settings.AutoConnectBluetooth)
                {
                    var candidates = _allChats.Where(c => c.IsBluetoothDevice && !c.PeerId.StartsWith("peer_prime")).ToList();
                    var target = candidates.FirstOrDefault();
                    if (target != null)
                    {
                        SelectedChat = target;
                    }

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

        if (dialog.ShowDialog() != true) return;

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

            await _storageService.SaveChatsAsync(_allChats);
            return;
        }

        // Single file or single photo
        string filePath = dialog.FileName;
        string fileName = Path.GetFileName(filePath);
        byte[] fileBytes = await File.ReadAllBytesAsync(filePath);

        var singleMsg = new ChatMessage
        {
            Sender = Settings.MyDisplayName,
            Text = fileName,
            FilePath = filePath,
            FileName = fileName,
            FileSizeBytes = fileBytes.Length,
            Type = type,
            IsFromMe = true,
            FormattedTime = DateTime.Now.ToString("HH:mm")
        };

        targetChat.Messages.Add(singleMsg);
        targetChat.LastMessage = type == PacketType.Photo ? "📷 Фотография" : $"📎 {fileName}";
        targetChat.LastMessageTime = singleMsg.FormattedTime;

        try
        {
            byte[] payload = (type == PacketType.Photo)
                ? PrimePacket.BuildPhotoPayload(singleMsg.Id, fileBytes, "")
                : PrimePacket.BuildFilePayload(singleMsg.Id, fileName, fileBytes);

            await _bluetoothService.SendPacketAsync(targetAddr, type, payload);
            singleMsg.IsDelivered = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Attachment send failed: {ex.Message}";
        }

        await _storageService.SaveChatsAsync(_allChats);
    }

    public async Task SendGifAttachmentAsync()
    {
        var targetChat = SelectedChat;
        if (targetChat == null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите GIF-анимацию",
            Filter = "GIF-анимация (*.gif)|*.gif|Все файлы (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true) return;

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

        _ = _storageService.SaveChatsAsync(_allChats);
    }

    private DateTime _lastTypingSentTime = DateTime.MinValue;

    private void NotifyTyping()
    {
        if (SelectedChat == null) return;
        if ((DateTime.UtcNow - _lastTypingSentTime).TotalSeconds < 2.5) return;
        _lastTypingSentTime = DateTime.UtcNow;

        _ = Task.Run(async () =>
        {
            try
            {
                string targetAddr = !string.IsNullOrWhiteSpace(SelectedChat.DeviceAddress) ? SelectedChat.DeviceAddress : SelectedChat.PeerId;
                byte[] typingPayload = PrimePacket.BuildTypingPayload("STATE:TYPING");
                await _bluetoothService.SendPacketAsync(targetAddr, PacketType.Typing, typingPayload);
            }
            catch { }
        });
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

    public async Task ConnectWifiCompanionAsync()
    {
        await Task.CompletedTask;
    }

    private async Task ConfirmAndDeleteChatAsync(ChatItem chat)

    {
        var result = MessageBox.Show(

            $"Вы действительно хотите удалить переписку и контакт «{chat.DisplayName}»?\n\nДиалог будет навсегда удален из списка чатов, а устройство полностью забыто.",

            "Удаление чата",

            MessageBoxButton.YesNo,

            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        byte[] payload = System.Text.Encoding.UTF8.GetBytes($"DELETE_CHAT:name={Settings.MyDisplayName};");

        try

        {
            
                await _bluetoothService.SendPacketAsync(chat.DeviceAddress, PacketType.ChatDeleted, payload);

        }

        catch { }

        try

        {
            
                await _bluetoothService.DisconnectAsync(chat.DeviceAddress);

        }

        catch { }

        // Forget device completely

        _bluetoothService.ForgetDevice(chat.DeviceAddress);

        _bluetoothService.ForgetDevice(chat.PeerId);

        var discovered = DiscoveredDevices.FirstOrDefault(d => d.PeerId == chat.PeerId || d.DeviceAddress == chat.DeviceAddress);

        if (discovered != null) DiscoveredDevices.Remove(discovered);

        AvatarManager.DeleteAvatar(chat.DeviceAddress);

        AvatarManager.DeleteAvatar(chat.PeerId);

        _allChats.Remove(chat);

        ApplySearchFilter();

        if (SelectedChat == chat)

        {
            SelectedChat = null;

            IsMediaDrawerOpen = false;

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

    public async Task BroadcastMyProfileAndAvatarAsync()
    {
        try
        {
            var onlineChats = _allChats.Where(c => c.IsOnline).ToList();
            if (onlineChats.Count == 0) return;

            byte[] profilePayload = PrimePacket.BuildProfileUpdatePayload(Settings.MyDisplayName, Settings.MyUsername);
            foreach (var chat in onlineChats)
            {
                string addr = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;
                await _bluetoothService.SendPacketAsync(addr, PacketType.ProfileUpdate, profilePayload);
            }

            if (!string.IsNullOrWhiteSpace(Settings.MyAvatarPath) && File.Exists(Settings.MyAvatarPath))
            {
                byte[] avatarBytes = await File.ReadAllBytesAsync(Settings.MyAvatarPath);
                if (avatarBytes.Length > 0)
                {
                    foreach (var chat in onlineChats)
                    {
                        string addr = !string.IsNullOrWhiteSpace(chat.DeviceAddress) ? chat.DeviceAddress : chat.PeerId;
                        await _bluetoothService.SendPacketAsync(addr, PacketType.Avatar, avatarBytes);
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
            var chat = _allChats.FirstOrDefault(c => 
                c.DeviceAddress.Equals(address, StringComparison.OrdinalIgnoreCase) ||
                c.PeerId.Equals(address, StringComparison.OrdinalIgnoreCase));

            if (chat == null)
            {
                // Only create chat when real connection is established!
                if (state != DeviceConnectionState.Connected)
                {
                    return;
                }

                string initialName = !IsStatusKeyword(name)
                    ? DeviceAnonymizer.SanitizeDisplayName(name, address)
                    : DeviceAnonymizer.SanitizeDisplayName(null, address);

                chat = new ChatItem
                {
                    DeviceAddress = address,
                    PeerId = address,
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
                chat.DisplayName = DeviceAnonymizer.SanitizeDisplayName(name, address);
            }

            StatusMessage = $"{chat.DisplayName}: {name}";

            if (state == DeviceConnectionState.Connected)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(600);
                    await BroadcastMyProfileAndAvatarAsync();
                });
            }
        });
    }

    private void OnPacketReceived(string fromAddress, PacketType type, byte[] payload)

    {
        Application.Current.Dispatcher.Invoke(() =>

        {
            var chat = _allChats.FirstOrDefault(c => 

                c.DeviceAddress.Equals(fromAddress, StringComparison.OrdinalIgnoreCase) ||

                c.PeerId.Equals(fromAddress, StringComparison.OrdinalIgnoreCase));

            if (chat == null)

            {
                // Ignore disconnect/delete packets for non-existent chats

                if (type == PacketType.ChatDeleted || type == PacketType.Disconnect)

                {
                    return;

                }

                chat = new ChatItem

                {
                    DeviceAddress = fromAddress,

                    PeerId = fromAddress,

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

                    MainWindow.TrayService?.ShowNotification(chat.DisplayName, text);

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
                    var (fMsgId, fName, fDuration, fIsVoice, fBytes) = PrimePacket.ParseFilePayload(payload);
                    string fDir = StorageService.MediaDirectory;
                    Directory.CreateDirectory(fDir);
                    string fPath = Path.Combine(fDir, $"file_{DateTime.Now:yyyyMMdd_HHmmss}_{fName}");
                    File.WriteAllBytes(fPath, fBytes);

                    string lowerName = fName.ToLowerInvariant();
                    bool isVoiceMsg = fIsVoice || lowerName.Contains("voice_") || lowerName.StartsWith("rec_voice_") || lowerName.EndsWith(".opus");
                    bool isAudioMsg = !isVoiceMsg && (lowerName.EndsWith(".mp3") || lowerName.EndsWith(".m4a") || lowerName.EndsWith(".aac") || lowerName.EndsWith(".wav") || lowerName.EndsWith(".ogg") || lowerName.EndsWith(".flac"));
                    bool isPhotoMsg = !isVoiceMsg && !isAudioMsg && (lowerName.EndsWith(".jpg") || lowerName.EndsWith(".jpeg") || lowerName.EndsWith(".png") || lowerName.EndsWith(".webp") || lowerName.EndsWith(".gif"));
                    bool isVideoMsg = !isVoiceMsg && !isAudioMsg && !isPhotoMsg && (lowerName.EndsWith(".mp4") || lowerName.EndsWith(".mkv") || lowerName.EndsWith(".mov") || lowerName.EndsWith(".avi") || lowerName.EndsWith(".webm"));

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
                        IsVideoNote = isVideoMsg,
                        IsFromMe = false,
                        FormattedTime = DateTime.Now.ToString("HH:mm")
                    };

                    chat.Messages.Add(incomingFileMsg);
                    if (isVoiceMsg) chat.LastMessage = "🎤 Голосовое сообщение";
                    else if (isAudioMsg) chat.LastMessage = $"🎵 {fName}";
                    else if (isPhotoMsg) chat.LastMessage = $"📷 {fName}";
                    else if (isVideoMsg) chat.LastMessage = $"📹 {fName}";
                    else chat.LastMessage = $"📎 {fName}";
                    chat.LastMessageTime = incomingFileMsg.FormattedTime;
                    if (SelectedChat != chat) chat.UnreadCount++;
                    OnPropertyChanged(nameof(SharedMedia));
                    OnPropertyChanged(nameof(SharedFiles));
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
                    bool isTypingState = typingData.Contains("TYPING");
                    chat.IsTyping = isTypingState;
                    if (isTypingState)
                    {
                        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                        timer.Tick += (s, e) =>
                        {
                            chat.IsTyping = false;
                            timer.Stop();
                        };
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
                        string aDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrimeDesktop", "Avatars");
                        Directory.CreateDirectory(aDir);
                        string safeKey = AvatarManager.GetSafePeerKey(chat.PeerId);
                        string aPath = Path.Combine(aDir, $"avatar_{safeKey}.png");
                        using (var fs = new FileStream(aPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                        {
                            fs.Write(payload, 0, payload.Length);
                        }
                        chat.AvatarPath = null;
                        chat.AvatarPath = aPath;
                        App.Log($"[Avatar] Saved and updated avatar for '{chat.DisplayName}' ({payload.Length} bytes)");
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

                    break;

                case PacketType.SyncChats:
                    try
                    {
                        string syncJson = System.Text.Encoding.UTF8.GetString(payload);
                        if (!string.IsNullOrWhiteSpace(syncJson))
                        {
                            var syncedList = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<ChatItem>>(syncJson);
                            if (syncedList != null && syncedList.Count > 0)
                            {
                                foreach (var syncChat in syncedList)
                                {
                                    var existing = _allChats.FirstOrDefault(c =>
                                        (!string.IsNullOrEmpty(c.DeviceAddress) && c.DeviceAddress.Equals(syncChat.DeviceAddress, StringComparison.OrdinalIgnoreCase)) ||
                                        (!string.IsNullOrEmpty(c.PeerId) && c.PeerId.Equals(syncChat.PeerId, StringComparison.OrdinalIgnoreCase)));
                                    if (existing != null)
                                    {
                                        foreach (var m in syncChat.Messages)
                                        {
                                            if (!existing.Messages.Any(x => x.Id == m.Id))
                                            {
                                                existing.Messages.Add(m);
                                            }
                                        }
                                        if (syncChat.Messages.Count > 0)
                                        {
                                            var last = existing.Messages.LastOrDefault();
                                            if (last != null)
                                            {
                                                existing.LastMessage = last.Text ?? existing.LastMessage;
                                                existing.LastMessageTime = last.FormattedTime;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        _allChats.Add(syncChat);
                                    }
                                }
                                _ = _storageService.SaveChatsAsync(_allChats);
                                ApplySearchFilter();
                                StatusMessage = "Чаты синхронизированы с телефоном";
                            }
                        }
                    }
                    catch (Exception syncEx)
                    {
                        App.Log($"[SyncChats ERROR] Failed to parse sync payload: {syncEx.Message}");
                    }
                    break;

                case PacketType.ChatDeleted:

                    try

                    {
                        
                            _ = _bluetoothService.DisconnectAsync(chat.DeviceAddress);

                    }

                    catch { }

                    _bluetoothService.ForgetDevice(chat.DeviceAddress);

                    _bluetoothService.ForgetDevice(chat.PeerId);

                    var disc = DiscoveredDevices.FirstOrDefault(d => d.PeerId == chat.PeerId || d.DeviceAddress == chat.DeviceAddress);

                    if (disc != null) DiscoveredDevices.Remove(disc);

                    _allChats.Remove(chat);

                    AvatarManager.DeleteAvatar(chat.DeviceAddress);

                    AvatarManager.DeleteAvatar(chat.PeerId);

                    ApplySearchFilter();

                    if (SelectedChat == chat)

                    {
                        SelectedChat = null;

                        IsMediaDrawerOpen = false;

                    }

                    _ = _storageService.SaveChatsAsync(_allChats);

                    MainWindow.TrayService?.ShowNotification("Прайм Мессенджер", $"Собеседник {chat.DisplayName} удалил чат.");

                    StatusMessage = $"Собеседник {chat.DisplayName} удалил чат. Устройство забыто";

                    break;

            }

            _ = _storageService.SaveChatsAsync(_allChats);

        });

    }

    private void OnNetworkError(string source, string message)

    {
        Application.Current.Dispatcher.Invoke(() =>

        {
            StatusMessage = $"{source}: {message}";

        });

    }
}

