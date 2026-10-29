using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

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
    public string DisplayText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_text)) return string.Empty;
            if (HasAttachment || IsVoice || IsVideoNote)
            {
                string t = _text.Trim();
                if (IsRedundantAttachmentText(t))
                    return string.Empty;
            }
            return _text;
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
