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

    [JsonIgnore]
    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetField(ref _isPlaying, value);
    }

    // Reply context
    public string? ReplyToId { get; set; }
    public string? ReplyToSender { get; set; }
    public string? ReplyToText { get; set; }

    [JsonIgnore]
    public bool HasReply => !string.IsNullOrEmpty(ReplyToId);

    [JsonIgnore]
    public bool HasAttachment => !string.IsNullOrEmpty(FilePath);

    [JsonIgnore]
    public bool IsPhoto => Type == PacketType.Photo;

    [JsonIgnore]
    public bool IsFile => Type == PacketType.File && !IsVideoNote;

    [JsonIgnore]
    public bool IsVoice => Type == PacketType.Voice;

    [JsonIgnore]
    public bool IsMusic => HasAttachment && !string.IsNullOrEmpty(FileName) &&
                           (FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                            FileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase));

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
