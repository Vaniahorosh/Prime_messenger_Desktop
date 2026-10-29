using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PrimeDesktop.Controls
{
    public partial class VideoPreviewPlayer : UserControl
    {
        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(nameof(Source), typeof(string), typeof(VideoPreviewPlayer),
                new PropertyMetadata(null, OnSourceChanged));

        public static readonly DependencyProperty DurationProperty =
            DependencyProperty.Register(nameof(Duration), typeof(string), typeof(VideoPreviewPlayer),
                new PropertyMetadata("0:00", OnDurationChanged));

        public static readonly DependencyProperty ClickCommandProperty =
            DependencyProperty.Register(nameof(ClickCommand), typeof(ICommand), typeof(VideoPreviewPlayer),
                new PropertyMetadata(null));

        public static readonly DependencyProperty ClickCommandParameterProperty =
            DependencyProperty.Register(nameof(ClickCommandParameter), typeof(object), typeof(VideoPreviewPlayer),
                new PropertyMetadata(null));

        private readonly DispatcherTimer _timer;
        private double _loopDurationSeconds = 2.5;

        public string? Source
        {
            get => (string?)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public string Duration
        {
            get => (string)GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        public ICommand? ClickCommand
        {
            get => (ICommand?)GetValue(ClickCommandProperty);
            set => SetValue(ClickCommandProperty, value);
        }

        public object? ClickCommandParameter
        {
            get => GetValue(ClickCommandParameterProperty);
            set => SetValue(ClickCommandParameterProperty, value);
        }

        public VideoPreviewPlayer()
        {
            InitializeComponent();

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _timer.Tick += Timer_Tick;

            Loaded += VideoPreviewPlayer_Loaded;
            Unloaded += VideoPreviewPlayer_Unloaded;
            IsVisibleChanged += VideoPreviewPlayer_IsVisibleChanged;
        }

        private static bool IsVideoFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path);
            return ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".3gp", StringComparison.OrdinalIgnoreCase);
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoPreviewPlayer player)
            {
                player.LoadSource();
            }
        }

        private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoPreviewPlayer player && !string.IsNullOrWhiteSpace(player.Duration) && player.Duration != "0:00")
            {
                player.TxtDuration.Text = player.Duration;
            }
        }

        private void VideoPreviewPlayer_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSource();
        }

        private void VideoPreviewPlayer_Unloaded(object sender, RoutedEventArgs e)
        {
            StopAndClear();
        }

        private void VideoPreviewPlayer_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible)
            {
                StopAndClear();
            }
            else
            {
                LoadSource();
            }
        }

        private void StopAndClear()
        {
            _timer.Stop();
            try
            {
                PART_Media.Stop();
                PART_Media.Close();
                PART_Media.Source = null;
            }
            catch { }
        }

        private void LoadSource()
        {
            if (!IsVisible || string.IsNullOrWhiteSpace(Source) || !IsVideoFile(Source) || !File.Exists(Source))
            {
                StopAndClear();
                return;
            }

            try
            {
                PART_Media.Source = new Uri(Path.GetFullPath(Source), UriKind.Absolute);
                PART_Media.IsMuted = true;
                PART_Media.Volume = 0;
                PART_Media.Play();
            }
            catch
            {
                StopAndClear();
            }
        }

        private void Media_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (!IsVisible || string.IsNullOrWhiteSpace(Source) || !IsVideoFile(Source))
            {
                StopAndClear();
                return;
            }

            try
            {
                PART_Media.IsMuted = true;
                PART_Media.Volume = 0;

                if (PART_Media.NaturalDuration.HasTimeSpan)
                {
                    double totalSec = PART_Media.NaturalDuration.TimeSpan.TotalSeconds;
                    // Preview loop duration depending on video:
                    if (totalSec <= 3.0)
                        _loopDurationSeconds = Math.Max(1.0, totalSec);
                    else if (totalSec <= 7.0)
                        _loopDurationSeconds = 2.0;
                    else
                        _loopDurationSeconds = 2.8;

                    if ((string.IsNullOrWhiteSpace(Duration) || Duration == "0:00") && totalSec > 0)
                    {
                        var ts = PART_Media.NaturalDuration.TimeSpan;
                        TxtDuration.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
                    }
                }
                else
                {
                    _loopDurationSeconds = 2.5;
                }

                PART_Media.Play();
                _timer.Start();
            }
            catch
            {
                StopAndClear();
            }
        }

        private void Media_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (!IsVisible)
            {
                StopAndClear();
                return;
            }

            try
            {
                PART_Media.Position = TimeSpan.Zero;
                PART_Media.Play();
            }
            catch { }
        }

        private void Media_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            StopAndClear();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (!IsVisible)
                {
                    StopAndClear();
                    return;
                }

                if (PART_Media.Position.TotalSeconds >= _loopDurationSeconds)
                {
                    PART_Media.Position = TimeSpan.Zero;
                    PART_Media.Play();
                }
            }
            catch { }
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ClickCommand?.Execute(ClickCommandParameter ?? Source);
            e.Handled = true;
        }
    }
}
