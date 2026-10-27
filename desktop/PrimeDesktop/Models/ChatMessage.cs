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
        set => SetField(ref _text, value);
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
        set => SetField(ref _isVideoNote, value);
    }

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
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
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
    public bool HasAttachment => !string.IsNullOrEmpty(FilePath);

    [JsonIgnore]
    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    [JsonIgnore]
    public bool IsPhoto => Type == PacketType.Photo || (HasAttachment && !string.IsNullOrEmpty(FileName) &&
                           (FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)));

    [JsonIgnore]
    public bool IsSinglePhoto => IsPhoto && !IsCollage;

    [JsonIgnore]
    public bool IsVoice => Type == PacketType.Voice || (HasAttachment && !string.IsNullOrEmpty(FileName) &&
                           (FileName.StartsWith("voice_", StringComparison.OrdinalIgnoreCase) ||
                            FileName.StartsWith("rec_voice_", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".opus", StringComparison.OrdinalIgnoreCase) ||
                            Text == "Голосовое сообщение"));

    [JsonIgnore]
    public bool IsMusic => HasAttachment && !string.IsNullOrEmpty(FileName) &&
                           (FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".aac", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".wma", StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public bool IsFile => !IsPhoto && !IsVoice && !IsVideoNote && (Type == PacketType.File || HasAttachment);

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
