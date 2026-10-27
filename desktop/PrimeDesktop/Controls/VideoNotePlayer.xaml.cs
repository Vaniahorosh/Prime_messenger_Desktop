using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PrimeDesktop.Controls
{
    public partial class VideoNotePlayer : UserControl
    {
        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(nameof(Source), typeof(string), typeof(VideoNotePlayer),
                new PropertyMetadata(null, OnSourceChanged));

        public static readonly DependencyProperty DurationProperty =
            DependencyProperty.Register(nameof(Duration), typeof(string), typeof(VideoNotePlayer),
                new PropertyMetadata("0:15", OnDurationChanged));

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(VideoNotePlayer),
                new PropertyMetadata(new CornerRadius(95), OnCornerRadiusChanged));

        private readonly DispatcherTimer _timer;
        private bool _isPlaying = true;
        private double _speed = 1.0;
        private bool _isMuted = false;

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

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public VideoNotePlayer()
        {
            InitializeComponent();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += Timer_Tick;

            Loaded += VideoNotePlayer_Loaded;
            Unloaded += VideoNotePlayer_Unloaded;
            SizeChanged += VideoNotePlayer_SizeChanged;
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoNotePlayer player)
            {
                player.LoadSource();
            }
        }

        private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoNotePlayer player && !player._isPlaying)
            {
                player.TxtTime.Text = player.Duration;
            }
        }

        private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoNotePlayer player)
            {
                player.UpdateClip();
            }
        }

        private void VideoNotePlayer_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateClip();
            LoadSource();
        }

        private void VideoNotePlayer_Unloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            try { PART_Media.Stop(); } catch { }
        }

        private void VideoNotePlayer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateClip();
        }

        private void UpdateClip()
        {
            var cr = CornerRadius;
            PART_RootBorder.CornerRadius = cr;

            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;
            if (w <= 0) w = 190;
            if (h <= 0) h = 190;

            PART_Media.Clip = new RectangleGeometry(new Rect(0, 0, w, h), cr.TopLeft, cr.TopLeft);
        }

        private void LoadSource()
        {
            if (string.IsNullOrWhiteSpace(Source) || !File.Exists(Source))
            {
                _timer.Stop();
                TxtTime.Text = Duration ?? "0:00";
                return;
            }

            try
            {
                PART_Media.Source = new Uri(Path.GetFullPath(Source), UriKind.Absolute);
                PART_Media.SpeedRatio = _speed;
                PART_Media.IsMuted = _isMuted;
                PART_Media.Play();
                _isPlaying = true;
                PART_PauseOverlay.Visibility = Visibility.Collapsed;
                _timer.Start();
            }
            catch
            {
                TxtTime.Text = Duration ?? "0:00";
            }
        }

        private void Media_MediaOpened(object sender, RoutedEventArgs e)
        {
            PART_Media.SpeedRatio = _speed;
            PART_Media.IsMuted = _isMuted;
            if (_isPlaying)
            {
                PART_Media.Play();
                _timer.Start();
            }
            UpdateClip();
        }

        private void Media_MediaEnded(object sender, RoutedEventArgs e)
        {
            try
            {
                PART_Media.Position = TimeSpan.Zero;
                if (_isPlaying)
                {
                    PART_Media.Play();
                }
            }
            catch { }
        }

        private void Media_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            _timer.Stop();
            _isPlaying = false;
            PART_PauseOverlay.Visibility = Visibility.Visible;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                var pos = PART_Media.Position;
                string total = !string.IsNullOrEmpty(Duration) ? Duration : "0:15";
                TxtTime.Text = $"{(int)pos.TotalMinutes}:{pos.Seconds:D2} / {total}";
            }
            catch { }
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                if (IsChildOf(dep, BtnMute) || IsChildOf(dep, BtnSpeed))
                    return;
            }

            TogglePlay();
            e.Handled = true;
        }

        private static bool IsChildOf(DependencyObject child, DependencyObject parent)
        {
            while (child != null)
            {
                if (child == parent) return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }

        private void TogglePlay()
        {
            try
            {
                if (_isPlaying)
                {
                    PART_Media.Pause();
                    _isPlaying = false;
                    _timer.Stop();
                    PART_PauseOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    PART_Media.Play();
                    _isPlaying = true;
                    _timer.Start();
                    PART_PauseOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            _isMuted = !_isMuted;
            PART_Media.IsMuted = _isMuted;
            IconSoundOn.Visibility = _isMuted ? Visibility.Collapsed : Visibility.Visible;
            IconSoundOff.Visibility = _isMuted ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnSpeed_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            _speed = _speed switch
            {
                >= 1.95 => 1.0,
                >= 1.45 => 2.0,
                _ => 1.5
            };
            PART_Media.SpeedRatio = _speed;
            TxtSpeed.Text = _speed switch
            {
                >= 1.95 => "2x",
                >= 1.45 => "1.5x",
                _ => "1x"
            };
        }
    }
}
