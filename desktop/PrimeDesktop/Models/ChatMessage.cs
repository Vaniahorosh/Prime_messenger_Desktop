using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;

namespace PrimeDesktop.Models;

public class ChatMessage : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _isDelivered;
    private bool _isRead;
    private bool _isEdited;
    private bool _isPlaying;
    private double _playbackProgress;
    private string _currentPlayTime = "0:00";
    private double _playbackSpeed = 1.0;
    private double[]? _waveform;
    private bool _isVideoPlaying = true;
    private bool _isVideoMuted;
    private double _videoSpeed = 1.0;
    private string? _reaction;
    private string? _reactionAuthor;
    private bool _isVideoNote;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Sender { get; set; } = string.Empty;
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public string FormattedTime { get; set; } = DateTime.Now.ToString("HH:mm");
    public bool IsFromMe { get; set; }
    public PacketType Type { get; set; } = PacketType.Text;

    public string Text
    {
        get => _text;
        set
        {
            if (SetField(ref _text, value))
            {
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(HasText));
                OnPropertyChanged(nameof(IsEmojiOnly));
                OnPropertyChanged(nameof(MessageFontSize));
                OnPropertyChanged(nameof(BubblePadding));
                OnPropertyChanged(nameof(IsRich));
                TryLoadLinkPreview();
            }
        }
    }

    public bool IsDelivered
    {
        get => _isDelivered;
        set { if (SetField(ref _isDelivered, value)) OnPropertyChanged(nameof(StatusTick)); }
    }

    public bool IsRead
    {
        get => _isRead;
        set { if (SetField(ref _isRead, value)) OnPropertyChanged(nameof(StatusTick)); }
    }

    public bool IsEdited
    {
        get => _isEdited;
        set => SetField(ref _isEdited, value);
    }

    // Reaction support (0x0D TYPE_REACTION)
    public string? Reaction
    {
        get => _reaction;
        set
        {
            if (SetField(ref _reaction, value))
            {
                OnPropertyChanged(nameof(HasReaction));
            }
        }
    }

    public string? ReactionAuthor
    {
        get => _reactionAuthor;
        set => SetField(ref _reactionAuthor, value);
    }

    [JsonIgnore]
    public bool HasReaction => !string.IsNullOrWhiteSpace(_reaction);

    // Video Notes (Кружки)
    public bool IsVideoNote
    {
        get => _isVideoNote;
        set
        {
            if (SetField(ref _isVideoNote, value))
            {
                OnPropertyChanged(nameof(VideoNotePath));
            }
        }
    }

    [JsonIgnore]
    public string? VideoNotePath => IsVideoNote ? FilePath : null;

    private int _videoNoteRadiusPercent = 50;
    public int VideoNoteRadiusPercent
    {
        get => _videoNoteRadiusPercent;
        set
        {
            if (SetField(ref _videoNoteRadiusPercent, value))
            {
                OnPropertyChanged(nameof(VideoNoteCornerRadius));
            }
        }
    }

    [JsonIgnore]
    public System.Windows.CornerRadius VideoNoteCornerRadius => new(95.0 * Math.Clamp(_videoNoteRadiusPercent, 0, 50) / 50.0);

    [JsonIgnore]
    public string StatusTick
    {
        get
        {
            if (!IsFromMe) return "";
            if (IsRead) return "✓✓";
            if (IsDelivered) return "✓";
            return "🕒";
        }
    }

    // Attachments
    private string? _filePath;
    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (SetField(ref _filePath, value))
            {
                OnPropertyChanged(nameof(EffectiveFileName));
                OnPropertyChanged(nameof(HasAttachment));
                OnPropertyChanged(nameof(IsPhoto));
                OnPropertyChanged(nameof(IsSinglePhoto));
                OnPropertyChanged(nameof(IsVideo));
                OnPropertyChanged(nameof(IsSingleVideo));
                OnPropertyChanged(nameof(HasFileAttachmentCard));
                OnPropertyChanged(nameof(VideoNotePath));
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(HasText));
                OnPropertyChanged(nameof(BubblePadding));
            }
        }
    }

    private string? _fileName;
    public string? FileName
    {
        get => _fileName;
        set
        {
            if (SetField(ref _fileName, value))
            {
                OnPropertyChanged(nameof(EffectiveFileName));
                OnPropertyChanged(nameof(HasAttachment));
                OnPropertyChanged(nameof(IsPhoto));
                OnPropertyChanged(nameof(IsSinglePhoto));
                OnPropertyChanged(nameof(IsVideo));
                OnPropertyChanged(nameof(IsSingleVideo));
                OnPropertyChanged(nameof(HasFileAttachmentCard));
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(HasText));
                OnPropertyChanged(nameof(BubblePadding));
            }
        }
    }
    public long FileSizeBytes { get; set; }
    public string Duration { get; set; } = "0:00";
    public List<string> CollagePaths { get; set; } = new();

    [JsonIgnore]
    public bool IsCollage => CollagePaths != null && CollagePaths.Count > 1;

    [JsonIgnore]
    public string FormattedFileSize
    {
        get
        {
            if (FileSizeBytes <= 0) return "";
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F1} KB";
            return $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }

    [JsonIgnore]
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetField(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(DisplayPlayTime));
            }
        }
    }

    [JsonIgnore]
    public double PlaybackProgress
    {
        get => _playbackProgress;
        set => SetField(ref _playbackProgress, value);
    }

    [JsonIgnore]
    public string CurrentPlayTime
    {
        get => _currentPlayTime;
        set
        {
            if (SetField(ref _currentPlayTime, value))
            {
                OnPropertyChanged(nameof(DisplayPlayTime));
            }
        }
    }

    [JsonIgnore]
    public string DisplayPlayTime => IsPlaying ? CurrentPlayTime : (string.IsNullOrWhiteSpace(Duration) ? "0:00" : Duration);

    [JsonIgnore]
    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            if (SetField(ref _playbackSpeed, value))
            {
                OnPropertyChanged(nameof(SpeedText));
            }
        }
    }

    [JsonIgnore]
    public string SpeedText => PlaybackSpeed switch
    {
        >= 1.95 => "2x",
        >= 1.45 => "1.5x",
        _ => "1x"
    };

    [JsonIgnore]
    public double[] Waveform
    {
        get => _waveform ??= GenerateWaveform(Id);
        set => SetField(ref _waveform, value);
    }

    public static double[] GenerateWaveform(string? seed)
    {
        int hash = string.IsNullOrEmpty(seed) ? 42 : Math.Abs(seed.GetHashCode());
        var rng = new Random(hash);
        var bars = new double[30];
        double prev = 0.35;
        for (int i = 0; i < bars.Length; i++)
        {
            double envelope = Math.Sin(Math.PI * (i + 1) / (bars.Length + 1));
            double target = 0.15 + (rng.NextDouble() * 0.70 + 0.15) * Math.Pow(envelope, 0.45);
            prev = (prev * 0.35) + (target * 0.65);
            bars[i] = Math.Clamp(prev, 0.15, 1.0);
        }
        return bars;
    }

    [JsonIgnore]
    public bool IsVideoPlaying
    {
        get => _isVideoPlaying;
        set => SetField(ref _isVideoPlaying, value);
    }

    [JsonIgnore]
    public bool IsVideoMuted
    {
        get => _isVideoMuted;
        set => SetField(ref _isVideoMuted, value);
    }

    [JsonIgnore]
    public double VideoSpeed
    {
        get => _videoSpeed;
        set
        {
            if (SetField(ref _videoSpeed, value))
            {
                OnPropertyChanged(nameof(VideoSpeedText));
            }
        }
    }

    [JsonIgnore]
    public string VideoSpeedText => VideoSpeed switch
    {
        >= 1.95 => "2x",
        >= 1.45 => "1.5x",
        _ => "1x"
    };

    // Reply context
    public string? ReplyToId { get; set; }
    public string? ReplyToSender { get; set; }
    public string? ReplyToText { get; set; }

    [JsonIgnore]
    public bool HasReply => !string.IsNullOrEmpty(ReplyToId);



    [JsonIgnore]
    public string EffectiveFileName => !string.IsNullOrEmpty(FileName) ? FileName : (!string.IsNullOrEmpty(FilePath) ? System.IO.Path.GetFileName(FilePath) : "");

    [JsonIgnore]
    public bool HasAttachment => !string.IsNullOrEmpty(FilePath) || !string.IsNullOrEmpty(FileName) || (CollagePaths != null && CollagePaths.Count > 0);

    [JsonIgnore]
    public bool IsSingleVideo => IsVideo && !IsVideoNote && !IsCollage && !string.IsNullOrEmpty(FilePath) && System.IO.File.Exists(FilePath);

    [JsonIgnore]
    public Thickness BubblePadding
    {
        get
        {
            if (IsSinglePhoto || IsSingleVideo || IsCollage)
                return new Thickness(3, 3, 3, 4);
            if (IsEmojiOnly)
                return new Thickness(10, 6, 10, 6);
            return new Thickness(12, 8, 12, 8);
        }
    }

    [JsonIgnore]
    private PrimeDesktop.Services.LinkPreviewData? _linkPreview;

    [JsonIgnore]
    public PrimeDesktop.Services.LinkPreviewData? LinkPreview
    {
        get => _linkPreview;
        set
        {
            if (SetField(ref _linkPreview, value))
            {
                OnPropertyChanged(nameof(HasLinkPreview));
            }
        }
    }

    [JsonIgnore]
    public bool HasLinkPreview => LinkPreview != null && !string.IsNullOrWhiteSpace(LinkPreview.Title);

    [JsonIgnore]
    private bool _showDateHeader;

    [JsonIgnore]
    public bool ShowDateHeader
    {
        get => _showDateHeader;
        set => SetField(ref _showDateHeader, value);
    }

    [JsonIgnore]
    public DateTime DateTimeLocal
    {
        get
        {
            try
            {
                long ts = Timestamp;
                if (ts <= 0) return DateTime.Now;
                if (ts < 100000000000L) ts *= 1000L;
                return DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
            }
            catch
            {
                return DateTime.Now;
            }
        }
    }

    [JsonIgnore]
    private string _dateHeaderText = string.Empty;

    [JsonIgnore]
    public string DateHeaderText
    {
        get => _dateHeaderText;
        set => SetField(ref _dateHeaderText, value);
    }

    public static string FormatDateHeader(DateTime date)
    {
        DateTime today = DateTime.Today;
        if (date.Date == today) return "Сегодня";
        if (date.Date == today.AddDays(-1)) return "Вчера";
        if (date.Year == today.Year)
            return date.ToString("d MMMM", new System.Globalization.CultureInfo("ru-RU"));
        return date.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("ru-RU"));
    }

    public void TryLoadLinkPreview()
    {
        string? url = PrimeDesktop.Services.LinkPreviewService.ExtractFirstUrl(_text);
        if (!string.IsNullOrEmpty(url))
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                var preview = await PrimeDesktop.Services.LinkPreviewService.FetchPreviewAsync(url);
                if (preview != null)
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        LinkPreview = preview;
                    });
                }
            });
        }
    }

    [JsonIgnore]
    public bool IsRich => !string.IsNullOrEmpty(_text) && _text.StartsWith("[RICH]", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string RichRawHtml => IsRich ? (_text.Length > 6 ? _text.Substring(6) : string.Empty) : string.Empty;

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_text)) return string.Empty;
            if (IsRich)
            {
                return CleanRichHtml(_text);
            }
            if (HasAttachment || IsVoice || IsVideoNote)
            {
                string t = _text.Trim();
                if (IsRedundantAttachmentText(t))
                    return string.Empty;
            }
            return _text;
        }
    }

    public static string CleanRichHtml(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string content = raw;
        if (content.StartsWith("[RICH]", StringComparison.OrdinalIgnoreCase))
        {
            content = content.Length > 6 ? content.Substring(6) : string.Empty;
        }

        try
        {
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<h[1-3][^>]*>(.*?)</h[1-3]>", "\n📌 $1\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<h[4-6][^>]*>(.*?)</h[4-6]>", "\n🔹 $1\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<blockquote[^>]*>(.*?)</blockquote>", "\n▍ $1\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<li[^>]*>(.*?)</li>", "\n• $1", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<(br|p|div|tr)\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"</(td|th)>", "  │  ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<[^>]+>", string.Empty);
            content = System.Net.WebUtility.HtmlDecode(content);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"\n{3,}", "\n\n");
            return content.Trim();
        }
        catch
        {
            return content.Trim();
        }
    }

    private bool IsRedundantAttachmentText(string t)
    {
        if (string.IsNullOrWhiteSpace(t)) return true;

        if (!string.IsNullOrEmpty(FileName) && string.Equals(t, FileName.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(EffectiveFileName) && string.Equals(t, EffectiveFileName.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(FilePath) && string.Equals(t, System.IO.Path.GetFileName(FilePath).Trim(), StringComparison.OrdinalIgnoreCase)) return true;

        string lower = t.ToLowerInvariant();
        if (lower == "📷 фото" || lower == "📷 фотография" || lower == "фото" || lower == "фотография" || lower == "photo" ||
            lower == "🎥 видео" || lower == "видео" || lower == "video" ||
            lower == "📎 файл" || lower == "файл" || lower == "file" || lower == "документ" ||
            lower == "медиа" || lower == "media" ||
            lower == "🎤 голосовое сообщение" || lower == "голосовое сообщение" || lower == "голосовое" ||
            lower == "📹 видеосообщение" || lower == "видеосообщение" ||
            lower == "👾 gif-анимация" || lower == "gif-анимация" || lower == "gif" ||
            lower == "🎵 аудио" || lower == "🎵 музыка" || lower == "аудио" || lower == "музыка")
        {
            return true;
        }

        if (!string.IsNullOrEmpty(FileName))
        {
            string fn = FileName.Trim();
            if (t.EndsWith(fn, StringComparison.OrdinalIgnoreCase))
            {
                string prefix = t.Substring(0, t.Length - fn.Length).Trim();
                if (prefix == "📷" || prefix == "🎥" || prefix == "📎" || prefix == "🎵" || prefix == "📹" || prefix == "👾" || string.IsNullOrEmpty(prefix))
                    return true;
            }
        }
        if (!string.IsNullOrEmpty(EffectiveFileName))
        {
            string fn = EffectiveFileName.Trim();
            if (t.EndsWith(fn, StringComparison.OrdinalIgnoreCase))
            {
                string prefix = t.Substring(0, t.Length - fn.Length).Trim();
                if (prefix == "📷" || prefix == "🎥" || prefix == "📎" || prefix == "🎵" || prefix == "📹" || prefix == "👾" || string.IsNullOrEmpty(prefix))
                    return true;
            }
        }

        return false;
    }

    [JsonIgnore]
    public bool HasText => !string.IsNullOrWhiteSpace(DisplayText);

    [JsonIgnore]
    public bool IsEmojiOnly => IsEmojiOnlyString(DisplayText, out _);

    [JsonIgnore]
    public double MessageFontSize
    {
        get
        {
            if (IsEmojiOnlyString(DisplayText, out int count))
            {
                return count switch
                {
                    1 => 36.0,
                    2 => 30.0,
                    _ => 26.0
                };
            }
            return 13.0;
        }
    }

    public static bool IsEmojiOnlyString(string text, out int count)
    {
        count = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return false;

        var matches = Emoji.Wpf.EmojiData.MatchOne.Matches(trimmed);
        if (matches.Count == 0 || matches.Count > 4) return false;

        int totalMatchLength = 0;
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            totalMatchLength += m.Length;
        }

        int nonWhitespace = 0;
        for (int i = 0; i < trimmed.Length; i++)
        {
            if (!char.IsWhiteSpace(trimmed[i])) nonWhitespace++;
        }

        if (nonWhitespace == totalMatchLength)
        {
            count = matches.Count;
            return true;
        }

        return false;
    }

    [JsonIgnore]
    public bool IsPhoto => Type == PacketType.Photo || (HasAttachment && !string.IsNullOrEmpty(EffectiveFileName) &&
                           (EffectiveFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)));

    [JsonIgnore]
    public bool IsSinglePhoto => IsPhoto && !IsCollage;

    [JsonIgnore]
    public bool IsVoice => Type == PacketType.Voice || (HasAttachment && !string.IsNullOrEmpty(EffectiveFileName) &&
                           (EffectiveFileName.StartsWith("voice_", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.StartsWith("rec_voice_", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".opus", StringComparison.OrdinalIgnoreCase) ||
                            Text == "Голосовое сообщение"));

    [JsonIgnore]
    public bool IsMusic => !IsVoice && HasAttachment && !string.IsNullOrEmpty(EffectiveFileName) &&
                           (EffectiveFileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".aac", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".wma", StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public bool IsVideo => !IsVideoNote && HasAttachment && !string.IsNullOrEmpty(EffectiveFileName) &&
                           (EffectiveFileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                            EffectiveFileName.EndsWith(".3gp", StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public string FileIcon => IsVideo ? "🎥" : (IsMusic ? "🎵" : "📎");

    [JsonIgnore]
    public bool IsFile => !IsPhoto && !IsVideo && !IsVideoNote && !IsVoice && !IsMusic && (Type == PacketType.File || HasAttachment);

    [JsonIgnore]
    public bool HasFileAttachmentCard => IsFile || IsMusic || (IsVideo && !IsSingleVideo);

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
