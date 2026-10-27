using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PrimeDesktop.Models;

namespace PrimeDesktop.Services
{
    public class AudioService
    {
        [DllImport("winmm.dll", EntryPoint = "mciSendStringA", CharSet = CharSet.Ansi)]
        private static extern int mciSendString(string lpstrCommand, StringBuilder? lpstrReturnString, int uReturnLength, IntPtr hwndCallback);

        private readonly Stopwatch _recordTimer = new();
        private MediaPlayer? _mediaPlayer;
        private DispatcherTimer? _playbackTimer;
        private ChatMessage? _activeMessage;
        private double? _pendingSeekRatio;
        private Action? _legacyCompletedCallback;
        private bool _isStopping;

        public bool IsRecording { get; private set; }
        public TimeSpan RecordingDuration => _recordTimer.Elapsed;
        public ChatMessage? ActiveMessage => _activeMessage;
        public event Action<ChatMessage?>? PlaybackStateChanged;

        public AudioService()
        {
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _playbackTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(35)
                    };
                    _playbackTimer.Tick += PlaybackTimer_Tick;
                });
            }
        }

        private void InitMediaPlayer()
        {
            if (_mediaPlayer != null) return;
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
        }

        private void CleanupMediaPlayer()
        {
            if (_mediaPlayer != null)
            {
                try
                {
                    _mediaPlayer.MediaOpened -= MediaPlayer_MediaOpened;
                    _mediaPlayer.MediaEnded -= MediaPlayer_MediaEnded;
                    _mediaPlayer.MediaFailed -= MediaPlayer_MediaFailed;
                    _mediaPlayer.Stop();
                    _mediaPlayer.Close();
                }
                catch { }
                _mediaPlayer = null;
            }
        }

        private void MediaPlayer_MediaOpened(object? sender, EventArgs e)
        {
            if (_activeMessage != null && _mediaPlayer != null)
            {
                try
                {
                    if (_pendingSeekRatio.HasValue && _mediaPlayer.NaturalDuration.HasTimeSpan)
                    {
                        var total = _mediaPlayer.NaturalDuration.TimeSpan;
                        var target = TimeSpan.FromMilliseconds(total.TotalMilliseconds * Math.Clamp(_pendingSeekRatio.Value, 0.0, 1.0));
                        _mediaPlayer.Position = target;
                        _activeMessage.PlaybackProgress = _pendingSeekRatio.Value;
                        _activeMessage.CurrentPlayTime = $"{(int)target.TotalMinutes}:{target.Seconds:D2}";
                        _pendingSeekRatio = null;
                    }
                    _mediaPlayer.SpeedRatio = _activeMessage.PlaybackSpeed;
                    _playbackTimer?.Start();
                }
                catch { }
            }
        }

        private void MediaPlayer_MediaEnded(object? sender, EventArgs e)
        {
            var cb = _legacyCompletedCallback;
            _legacyCompletedCallback = null;
            Stop();
            cb?.Invoke();
        }

        private void MediaPlayer_MediaFailed(object? sender, ExceptionEventArgs e)
        {
            var cb = _legacyCompletedCallback;
            _legacyCompletedCallback = null;
            Stop();
            cb?.Invoke();
        }

        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            if (_activeMessage == null || _mediaPlayer == null)
            {
                _playbackTimer?.Stop();
                return;
            }

            try
            {
                if (_mediaPlayer.NaturalDuration.HasTimeSpan)
                {
                    var total = _mediaPlayer.NaturalDuration.TimeSpan;
                    if (total.TotalMilliseconds > 0)
                    {
                        var pos = _mediaPlayer.Position;
                        if (pos >= total)
                        {
                            Stop();
                            return;
                        }
                        _activeMessage.PlaybackProgress = Math.Clamp(pos.TotalMilliseconds / total.TotalMilliseconds, 0.0, 1.0);
                        _activeMessage.CurrentPlayTime = $"{(int)pos.TotalMinutes}:{pos.Seconds:D2}";
                    }
                }
            }
            catch { }
        }

        public bool StartRecording()
        {
            try
            {
                mciSendString("close prime_rec", null, 0, IntPtr.Zero);
                int r1 = mciSendString("open new type waveaudio alias prime_rec", null, 0, IntPtr.Zero);
                int r2 = mciSendString("record prime_rec", null, 0, IntPtr.Zero);
                if (r1 == 0 && r2 == 0)
                {
                    IsRecording = true;
                    _recordTimer.Restart();
                    return true;
                }
            }
            catch { }
            return false;
        }

        public (string? path, string durationStr) StopRecording()
        {
            if (!IsRecording) return (null, "0:00");

            _recordTimer.Stop();
            var dur = _recordTimer.Elapsed;
            string durStr = $"{(int)dur.TotalMinutes}:{dur.Seconds:D2}";
            IsRecording = false;

            string mediaDir = StorageService.MediaDirectory;
            Directory.CreateDirectory(mediaDir);
            string filePath = Path.Combine(mediaDir, $"voice_{Guid.NewGuid():N}.wav");

            try
            {
                mciSendString($"save prime_rec \"{filePath}\"", null, 0, IntPtr.Zero);
                mciSendString("close prime_rec", null, 0, IntPtr.Zero);

                if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                {
                    return (filePath, durStr);
                }
            }
            catch { }

            return (null, durStr);
        }

        public void TogglePlay(ChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.FilePath) || !File.Exists(message.FilePath))
                return;

            if (_activeMessage == message)
            {
                if (message.IsPlaying)
                {
                    try { _mediaPlayer?.Pause(); } catch { }
                    message.IsPlaying = false;
                    _playbackTimer?.Stop();
                    PlaybackStateChanged?.Invoke(null);
                }
                else
                {
                    try
                    {
                        if (_mediaPlayer != null && _mediaPlayer.NaturalDuration.HasTimeSpan &&
                            _mediaPlayer.Position >= _mediaPlayer.NaturalDuration.TimeSpan)
                        {
                            _mediaPlayer.Position = TimeSpan.Zero;
                        }
                        _mediaPlayer?.Play();
                        message.IsPlaying = true;
                        _playbackTimer?.Start();
                        PlaybackStateChanged?.Invoke(message);
                    }
                    catch
                    {
                        Stop();
                    }
                }
                return;
            }

            // Switching message or start from beginning
            Stop();

            _activeMessage = message;
            _activeMessage.IsPlaying = true;
            _activeMessage.PlaybackProgress = 0.0;
            _activeMessage.CurrentPlayTime = "0:00";

            try
            {
                InitMediaPlayer();
                _mediaPlayer?.Open(new Uri(Path.GetFullPath(message.FilePath), UriKind.Absolute));
                _mediaPlayer?.Play();
                _playbackTimer?.Start();
                PlaybackStateChanged?.Invoke(message);
            }
            catch
            {
                Stop();
            }
        }

        public void Seek(ChatMessage message, double ratio)
        {
            if (message == null) return;
            ratio = Math.Clamp(ratio, 0.0, 1.0);

            if (_activeMessage != message)
            {
                _pendingSeekRatio = ratio;
                TogglePlay(message);
                return;
            }

            if (_mediaPlayer != null && _mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                try
                {
                    var total = _mediaPlayer.NaturalDuration.TimeSpan;
                    var target = TimeSpan.FromMilliseconds(total.TotalMilliseconds * ratio);
                    _mediaPlayer.Position = target;
                    message.PlaybackProgress = ratio;
                    message.CurrentPlayTime = $"{(int)target.TotalMinutes}:{target.Seconds:D2}";
                    if (!message.IsPlaying)
                    {
                        _mediaPlayer.Play();
                        message.IsPlaying = true;
                        _playbackTimer?.Start();
                    }
                }
                catch { }
            }
            else
            {
                _pendingSeekRatio = ratio;
            }
        }

        public void SetSpeed(ChatMessage message, double speed)
        {
            if (message == null) return;
            message.PlaybackSpeed = speed;
            if (_activeMessage == message && _mediaPlayer != null)
            {
                try { _mediaPlayer.SpeedRatio = speed; } catch { }
            }
        }

        public void Stop()
        {
            if (_isStopping) return;
            _isStopping = true;

            try
            {
                _playbackTimer?.Stop();

                var currentMsg = _activeMessage;
                _activeMessage = null;
                _pendingSeekRatio = null;

                CleanupMediaPlayer();

                if (currentMsg != null)
                {
                    currentMsg.IsPlaying = false;
                    currentMsg.PlaybackProgress = 0.0;
                    currentMsg.CurrentPlayTime = "0:00";
                    PlaybackStateChanged?.Invoke(null);
                }
            }
            catch { }
            finally
            {
                _isStopping = false;
            }
        }

        public void Play(string filePath, Action onCompleted)
        {
            try
            {
                Stop();
                if (!File.Exists(filePath))
                {
                    onCompleted?.Invoke();
                    return;
                }

                InitMediaPlayer();
                _legacyCompletedCallback = onCompleted;
                _mediaPlayer?.Open(new Uri(Path.GetFullPath(filePath), UriKind.Absolute));
                _mediaPlayer?.Play();
            }
            catch
            {
                onCompleted?.Invoke();
            }
        }

        public bool IsPlaying(string filePath) =>
            _activeMessage != null && _activeMessage.IsPlaying && _activeMessage.FilePath == filePath;
    }
}
