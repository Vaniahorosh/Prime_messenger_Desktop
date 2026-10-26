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
                    _selectedChat.UnreadCount = 0;

                    _ = _storageService.SaveChatsAsync(_allChats);

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

    public ICommand CancelReplyCommand { get; }

    public ICommand ReplyToMessageCommand { get; }

    public ICommand CopyMessageCommand { get; }

    public ICommand DeleteMessageCommand { get; }

    public ICommand ScanDevicesCommand { get; }

    public ICommand ConnectChatCommand { get; }

    public ICommand DisconnectChatCommand { get; }

    public ICommand ToggleSettingsCommand { get; }

    public ICommand ToggleMediaDrawerCommand { get; }

    public ICommand ConnectCompanionWifiCommand { get; }

    public ICommand ChangeAccentColorCommand { get; }

    public ICommand ChangeContactAvatarCommand { get; }

    public ICommand ChangeMyAvatarCommand { get; }

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

        SelectedChat?.Messages.Where(m => m.IsPhoto || m.IsVideoNote) ?? Enumerable.Empty<ChatMessage>();

    public System.Collections.Generic.IEnumerable<ChatMessage> SharedFiles =>

        SelectedChat?.Messages.Where(m => m.IsFile) ?? Enumerable.Empty<ChatMessage>();

    public System.Collections.Generic.IEnumerable<ChatMessage> SharedAudio =>

        SelectedChat?.Messages.Where(m => m.IsVoice || m.IsMusic) ?? Enumerable.Empty<ChatMessage>();

    public MainViewModel()

    {
        SendMessageCommand = new RelayCommand(async () => await SendTextMessageAsync(), () => HasSelectedChat && !string.IsNullOrWhiteSpace(InputMessageText));

        AttachFileCommand = new RelayCommand(async () => await SendAttachmentAsync(PacketType.File), () => HasSelectedChat);

        AttachPhotoCommand = new RelayCommand(async () => await SendAttachmentAsync(PacketType.Photo), () => HasSelectedChat);

        CancelReplyCommand = new RelayCommand(() => ReplyMessage = null);

        ReplyToMessageCommand = new RelayCommand(param => { if (param is ChatMessage msg) ReplyMessage = msg; });

        CopyMessageCommand = new RelayCommand(param =>

        {
            if (param is ChatMessage msg && !string.IsNullOrEmpty(msg.Text))

            {
                Clipboard.SetText(msg.Text);

                StatusMessage = "Текст скопирован в буфер";

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

        ToggleSettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);

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
                    Settings.MyAvatarPath = saved;

                    OnPropertyChanged(nameof(Settings));

                    _ = _storageService.SaveSettingsAsync(Settings);

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
            if (param is string s && int.TryParse(s, out int tab)) SelectedSettingsTab = tab;

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
        Settings = await _storageService.LoadSettingsAsync();

        ThemeManager.ApplyAccentColor(Settings.AccentColorHex);

        var chats = await _storageService.LoadChatsAsync();

        foreach (var c in chats)

        {
            if (string.IsNullOrEmpty(c.AvatarPath))

            {
                c.AvatarPath = AvatarManager.GetAvatarPath(c.DeviceAddress);

            }

            _allChats.Add(c);

        }

        if (_allChats.Count == 0 && !_storageService.HasChatsFile)

        {
            // Seed a welcome demo chat so the UI looks active right away

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

        // Start background servers

        _bluetoothService.MyDisplayName = Settings.MyDisplayName;

        _bluetoothService.MyUsername = Settings.MyUsername;

        await _bluetoothService.StartServerAsync();

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
            
                await _bluetoothService.SendPacketAsync(SelectedChat.DeviceAddress, PacketType.Reaction, payload);

        }

        catch { }

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
            
                await _bluetoothService.SendPacketAsync(targetChat.DeviceAddress, PacketType.Voice, fileBytes);

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
            dialog.Title = "Выберите изображение";

            dialog.Filter = "Изображения|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Все файлы|*.*";

        }

        else

        {
            dialog.Title = "Выберите файл для отправки";

            dialog.Filter = "Все файлы|*.*";

        }

        if (dialog.ShowDialog() == true)

        {
            string filePath = dialog.FileName;

            string fileName = Path.GetFileName(filePath);

            byte[] fileBytes = await File.ReadAllBytesAsync(filePath);

            var message = new ChatMessage

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

            targetChat.Messages.Add(message);

            targetChat.LastMessage = type == PacketType.Photo ? "📷 Фотография" : $"📎 {fileName}";

            targetChat.LastMessageTime = message.FormattedTime;

            try

            {
                await _bluetoothService.SendPacketAsync(targetChat.DeviceAddress, type, fileBytes);

                message.IsDelivered = true;

            }

            catch (Exception ex)

            {
                StatusMessage = $"Attachment send failed: {ex.Message}";

            }

            await _storageService.SaveChatsAsync(_allChats);

        }

    }

    private void NotifyTyping()

    {
        if (SelectedChat == null) return;

        _ = Task.Run(async () =>

        {
            try

            {
                byte[] empty = Array.Empty<byte>();

                
                    await _bluetoothService.SendPacketAsync(SelectedChat.DeviceAddress, PacketType.Typing, empty);

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

        await _bluetoothService.ConnectAsync(SelectedChat.DeviceAddress);

    }

    public async Task DisconnectSelectedChatAsync()

    {
        if (SelectedChat == null) return;

        await _bluetoothService.DisconnectAsync(SelectedChat.DeviceAddress);

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

    private void OnConnectionStateChanged(string address, DeviceConnectionState state, string name)

    {
        Application.Current.Dispatcher.Invoke(() =>

        {
            var chat = _allChats.FirstOrDefault(c => 

                c.DeviceAddress.Equals(address, StringComparison.OrdinalIgnoreCase) ||

                c.PeerId.Equals(address, StringComparison.OrdinalIgnoreCase));

            if (chat == null)

            {
                // CRITICAL FIX: Never recreate a chat item if the state is Disconnected!

                // When a chat is deleted or device forgotten, disconnect event must NOT bring it back!

                if (state != DeviceConnectionState.Connected && state != DeviceConnectionState.Connecting)

                {
                    return;

                }

                chat = new ChatItem

                {
                    DeviceAddress = address,

                    PeerId = address,

                    DisplayName = DeviceAnonymizer.SanitizeDisplayName(name, address),

                    Username = DeviceAnonymizer.SanitizeUsername(null, address),

                    AvatarPath = AvatarManager.GetAvatarPath(address),

                    IsBluetoothDevice = true

                };

                _allChats.Add(chat);

                ApplySearchFilter();

            }

            chat.ConnectionState = state;

            chat.IsOnline = (state == DeviceConnectionState.Connected);

            if (!string.IsNullOrEmpty(name)) chat.DisplayName = DeviceAnonymizer.SanitizeDisplayName(name, address);

            StatusMessage = $"{chat.DisplayName}: {state}";

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

                case PacketType.File:

                    string ext = (type == PacketType.Photo) ? ".jpg" : ".bin";

                    string savedName = $"media_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString().Substring(0, 6)}{ext}";

                    string savedPath = Path.Combine(StorageService.MediaDirectory, savedName);

                    File.WriteAllBytes(savedPath, payload);

                    var fileMsg = new ChatMessage

                    {
                        Sender = chat.DisplayName,

                        Text = savedName,

                        FilePath = savedPath,

                        FileName = savedName,

                        FileSizeBytes = payload.Length,

                        Type = type,

                        IsFromMe = false,

                        FormattedTime = DateTime.Now.ToString("HH:mm")

                    };

                    chat.Messages.Add(fileMsg);

                    chat.LastMessage = (type == PacketType.Photo) ? "📷 Фотография" : "📎 Файл";

                    chat.LastMessageTime = fileMsg.FormattedTime;

                    if (SelectedChat != chat) chat.UnreadCount++;

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

                    var targetMsg = chat.Messages.FirstOrDefault(m => m.Id == rxMsgId);

                    if (targetMsg != null)

                    {
                        targetMsg.Reaction = rxEmoji;

                        targetMsg.ReactionAuthor = rxAuthor;

                    }

                    break;

                case PacketType.ProfileUpdate:

                    var (newName, newLogin) = PrimePacket.ParseProfileUpdatePayload(payload);

                    if (!string.IsNullOrEmpty(newName)) chat.DisplayName = DeviceAnonymizer.SanitizeDisplayName(newName);

                    if (!string.IsNullOrEmpty(newLogin)) chat.Username = DeviceAnonymizer.SanitizeUsername(newLogin);

                    break;

                case PacketType.Avatar:

                    string aDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrimeDesktop", "Avatars");

                    Directory.CreateDirectory(aDir);

                    string aPath = Path.Combine(aDir, $"avatar_{chat.PeerId}.png");

                    File.WriteAllBytes(aPath, payload);

                    chat.AvatarPath = aPath;

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

                case PacketType.Edit:

                    string editRaw = System.Text.Encoding.UTF8.GetString(payload);

                    int eSep = editRaw.IndexOf(":::");

                    if (eSep != -1)

                    {
                        string eId = editRaw.Substring(0, eSep);

                        string eText = editRaw.Substring(eSep + 3);

                        var toEdit = chat.Messages.FirstOrDefault(m => m.Id == eId);

                        if (toEdit != null)

                        {
                            toEdit.Text = eText;

                            chat.LastMessage = chat.Messages.LastOrDefault()?.Text ?? "";

                        }

                    }

                    break;

                case PacketType.DeleteMessage:

                    string delMsgId = System.Text.Encoding.UTF8.GetString(payload).Trim();

                    var toDelete = chat.Messages.FirstOrDefault(m => m.Id == delMsgId);

                    if (toDelete != null)

                    {
                        chat.Messages.Remove(toDelete);

                        chat.LastMessage = chat.Messages.LastOrDefault()?.Text ?? "";

                    }

                    break;

                case PacketType.Typing:

                    chat.IsTyping = true;

                    Task.Delay(3000).ContinueWith(_ => Application.Current.Dispatcher.Invoke(() => chat.IsTyping = false));

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

