using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PrimeDesktop.Services
{
    public class AudioService
    {
        [DllImport("winmm.dll", EntryPoint = "mciSendStringA", CharSet = CharSet.Ansi)]
        private static extern int mciSendString(string lpstrCommand, StringBuilder? lpstrReturnString, int uReturnLength, IntPtr hwndCallback);

        private readonly Stopwatch _recordTimer = new();
        private SoundPlayer? _currentPlayer;
        private string? _currentPlayingPath;
        public bool IsRecording { get; private set; }

        public TimeSpan RecordingDuration => _recordTimer.Elapsed;

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

        public void Play(string filePath, Action onCompleted)
        {
            try
            {
                Stop();
                if (!File.Exists(filePath)) return;

                _currentPlayingPath = filePath;
                _currentPlayer = new SoundPlayer(filePath);
                _ = Task.Run(() =>
                {
                    try
                    {
                        _currentPlayer.PlaySync();
                    }
                    catch { }
                    finally
                    {
                        _currentPlayingPath = null;
                        onCompleted?.Invoke();
                    }
                });
            }
            catch
            {
                onCompleted?.Invoke();
            }
        }

        public void Stop()
        {
            try
            {
                _currentPlayer?.Stop();
                _currentPlayer?.Dispose();
                _currentPlayer = null;
                _currentPlayingPath = null;
            }
            catch { }
        }

        public bool IsPlaying(string filePath) => _currentPlayingPath == filePath;
    }
}