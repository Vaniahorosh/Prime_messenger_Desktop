using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PrimeDesktop.Services;

namespace PrimeDesktop.Views
{
    public class MediaViewerItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public string FormattedTime { get; set; } = string.Empty;
        public bool IsVideo { get; set; }
        public string FileName { get; set; } = string.Empty;
    }

    public partial class PhotoViewerWindow : Window
    {
        private List<MediaViewerItem> _items = new();
        private int _currentIndex;

        private BitmapImage? _bitmap;
        private Point _lastDragPoint;
        private bool _isDragging;
        private double _currentZoom = 1.0;

        private readonly DispatcherTimer _videoTimer;
        private readonly DispatcherTimer _volumeHudTimer;
        private bool _isVideoPlaying;
        private bool _isVideoMuted;
        private double _videoVolume = 1.0;
        private double _videoSpeed = 1.0;
        private bool _isUserSeeking;

        public PhotoViewerWindow(string initialPath) : this(initialPath, null)
        {
        }

        public PhotoViewerWindow(string initialPath, List<MediaViewerItem>? items)
        {
            InitializeComponent();

            _videoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _videoTimer.Tick += VideoTimer_Tick;

            _volumeHudTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _volumeHudTimer.Tick += (s, e) =>
            {
                _volumeHudTimer.Stop();
                VolumeHud.Visibility = Visibility.Collapsed;
            };

            PreviewMouseWheel += Window_PreviewMouseWheel;
            PreviewKeyDown += Window_PreviewKeyDown;
            StateChanged += Window_StateChanged;
            SourceInitialized += Window_SourceInitialized;
            Loaded += Window_Loaded;
            SizeChanged += Window_SizeChanged;
            Closed += Window_Closed;

            SetMediaList(initialPath, items);
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            ThemeManager.ApplyWindowDwm(this, forceDark: true);
            WindowUtils.EnableNativeWindowAnimations(this);
            WindowUtils.EnableWindowMinMaxHook(this);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySavedWindowPlacement();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SaveWindowPlacement();
        }

        private void ApplySavedWindowPlacement()
        {
            try
            {
                var vm = DataContext as ViewModels.MainViewModel;
                var s = vm?.Settings;
                if (s == null)
                {
                    WindowState = WindowState.Maximized;
                    return;
                }

                // If user previously set a custom window size, restore it
                if (s.PhotoViewerWidth >= MinWidth && s.PhotoViewerHeight >= MinHeight)
                {
                    Width = s.PhotoViewerWidth;
                    Height = s.PhotoViewerHeight;
                }

                // Default is Maximized (fullscreen), or restore last state
                if (s.PhotoViewerWasMaximized)
                {
                    WindowState = WindowState.Maximized;
                }
                else
                {
                    WindowState = WindowState.Normal;
                }
            }
            catch
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void SaveWindowPlacement()
        {
            try
            {
                var vm = DataContext as ViewModels.MainViewModel;
                var s = vm?.Settings;
                if (s == null) return;

                if (WindowState == WindowState.Maximized)
                {
                    s.PhotoViewerWasMaximized = true;
                }
                else if (WindowState == WindowState.Normal)
                {
                    s.PhotoViewerWasMaximized = false;
                    if (ActualWidth >= MinWidth) s.PhotoViewerWidth = ActualWidth;
                    if (ActualHeight >= MinHeight) s.PhotoViewerHeight = ActualHeight;
                }
            }
            catch { }
        }

        public static bool IsVideoPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".mp4" || ext == ".webm" || ext == ".mov" || ext == ".avi" ||
                   ext == ".mkv" || ext == ".3gp" || ext == ".wmv";
        }

        public void SetMediaList(string initialPath, List<MediaViewerItem>? items)
        {
            if (items != null && items.Count > 0)
            {
                _items = new List<MediaViewerItem>(items);
                int found = _items.FindIndex(m => m.FilePath.Equals(initialPath, StringComparison.OrdinalIgnoreCase));
                _currentIndex = found >= 0 ? found : 0;
            }
            else
            {
                _items = new List<MediaViewerItem>
                {
                    new MediaViewerItem
                    {
                        FilePath = initialPath,
                        Sender = "Прайм",
                        FormattedTime = DateTime.Now.ToString("HH:mm"),
                        IsVideo = IsVideoPath(initialPath),
                        FileName = Path.GetFileName(initialPath)
                    }
                };
                _currentIndex = 0;
            }

            DisplayCurrentMedia();
        }

        public void LoadPhoto(string path)
        {
            SetMediaList(path, null);
        }

        private void DisplayCurrentMedia()
        {
            if (_items.Count == 0 || _currentIndex < 0 || _currentIndex >= _items.Count)
                return;

            var item = _items[_currentIndex];
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.SendMyActivityState(item.IsVideo ? "VIEWING_VIDEO" : "VIEWING_PHOTO");
            }

            if (!File.Exists(item.FilePath))
            {
                TxtFileName.Text = item.FileName ?? "Файл не найден";
                TxtMediaDetails.Text = "Файл отсутствует на диске";
                return;
            }

            TxtFileName.Text = !string.IsNullOrEmpty(item.FileName) ? item.FileName : Path.GetFileName(item.FilePath);

            // Update bottom-right info badge
            string senderPart = string.IsNullOrWhiteSpace(item.FormattedTime)
                ? item.Sender
                : $"{item.Sender} • {item.FormattedTime}";
            TxtInfoSender.Text = !string.IsNullOrWhiteSpace(senderPart) ? senderPart : "Прайм";

            string typeName = item.IsVideo ? "Видео" : "Фото";
            TxtInfoCounter.Text = $"{typeName} {_currentIndex + 1} из {_items.Count}";

            // Update navigation arrows
            BtnPrevMedia.Visibility = _currentIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
            BtnNextMedia.Visibility = _currentIndex < _items.Count - 1 ? Visibility.Visible : Visibility.Collapsed;

            long sizeKb = new FileInfo(item.FilePath).Length / 1024;

            if (item.IsVideo)
            {
                // VIDEO MODE
                PhotoScrollViewer.Visibility = Visibility.Collapsed;
                PhotoZoomPanel.Visibility = Visibility.Collapsed;

                VideoContainer.Visibility = Visibility.Visible;
                VideoControlBar.Visibility = Visibility.Visible;

                try
                {
                    PART_Video.Source = new Uri(Path.GetFullPath(item.FilePath), UriKind.Absolute);
                    PART_Video.SpeedRatio = _videoSpeed;
                    PART_Video.IsMuted = _isVideoMuted;
                    PART_Video.Play();
                    _isVideoPlaying = true;
                    IconVidPlay.Visibility = Visibility.Collapsed;
                    IconVidPause.Visibility = Visibility.Visible;
                    VideoPauseOverlay.Visibility = Visibility.Collapsed;
                    _videoTimer.Start();
                }
                catch (Exception ex)
                {
                    App.Log($"[MediaViewer] Failed to play video: {ex.Message}");
                }

                TxtMediaDetails.Text = $"• {sizeKb} КБ";
            }
            else
            {
                // PHOTO MODE
                StopVideo();
                VideoContainer.Visibility = Visibility.Collapsed;
                VideoControlBar.Visibility = Visibility.Collapsed;

                PhotoScrollViewer.Visibility = Visibility.Visible;
                PhotoZoomPanel.Visibility = Visibility.Visible;

                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(Path.GetFullPath(item.FilePath), UriKind.Absolute);
                    bi.EndInit();
                    bi.Freeze();

                    _bitmap = bi;
                    TargetImage.Source = bi;
                    TxtMediaDetails.Text = $"• {bi.PixelWidth} × {bi.PixelHeight} px ({sizeKb} КБ)";
                    ResetZoomAndPosition();
                }
                catch (Exception ex)
                {
                    App.Log($"[MediaViewer] Failed to load image: {ex.Message}");
                    TxtMediaDetails.Text = $"• {sizeKb} КБ";
                }
            }
        }

        private void StopVideo()
        {
            _videoTimer.Stop();
            _isVideoPlaying = false;
            try
            {
                PART_Video.Stop();
                PART_Video.Close();
                PART_Video.Source = null;
            }
            catch { }
        }

        private void NavigatePrev()
        {
            if (_currentIndex > 0)
            {
                _currentIndex--;
                DisplayCurrentMedia();
            }
        }

        private void NavigateNext()
        {
            if (_currentIndex < _items.Count - 1)
            {
                _currentIndex++;
                DisplayCurrentMedia();
            }
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            NavigatePrev();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            NavigateNext();
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // If Control is pressed and looking at a photo: zoom in / out
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var cur = _items[_currentIndex];
                if (!cur.IsVideo)
                {
                    double factor = e.Delta > 0 ? 1.2 : 0.8;
                    ApplyZoom(_currentZoom * factor);
                    e.Handled = true;
                    return;
                }
            }

            // Normal mouse wheel: navigate next / previous media!
            if (e.Delta < 0)
            {
                NavigateNext();
                e.Handled = true;
            }
            else if (e.Delta > 0)
            {
                NavigatePrev();
                e.Handled = true;
            }
        }

        private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // If user clicked the transparent backdrop area outside the media
            if (e.OriginalSource == PART_Backdrop)
            {
                Close();
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                BtnMaximize_Click(sender, e);
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (WindowState == WindowState.Maximized)
                {
                    var mousePos = PointToScreen(e.GetPosition(this));
                    var ratio = e.GetPosition(this).X / Math.Max(ActualWidth, 1);
                    WindowState = WindowState.Normal;
                    Left = mousePos.X - (ActualWidth * ratio);
                    Top = mousePos.Y - 20;
                }
                DragMove();
            }
        }

        #region Video Player Events

        private void Video_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (PART_Video.NaturalDuration.HasTimeSpan)
            {
                var dur = PART_Video.NaturalDuration.TimeSpan;
                TxtVideoTotal.Text = $"{(int)dur.TotalMinutes}:{dur.Seconds:D2}";
                SliderVideoProgress.Maximum = dur.TotalSeconds;
            }
        }

        private void Video_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Loop video
            try
            {
                PART_Video.Position = TimeSpan.Zero;
                PART_Video.Play();
            }
            catch { }
        }

        private void Video_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            _videoTimer.Stop();
            _isVideoPlaying = false;
            VideoPauseOverlay.Visibility = Visibility.Visible;
        }

        private void VideoTimer_Tick(object? sender, EventArgs e)
        {
            if (_isUserSeeking) return;

            try
            {
                var pos = PART_Video.Position;
                TxtVideoCurrent.Text = $"{(int)pos.TotalMinutes}:{pos.Seconds:D2}";
                if (SliderVideoProgress.Maximum > 0)
                {
                    SliderVideoProgress.Value = pos.TotalSeconds;
                }
            }
            catch { }
        }

        private void Video_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ToggleVideoPlayPause();
            e.Handled = true;
        }

        private void BtnVideoPlayPause_Click(object sender, RoutedEventArgs e)
        {
            ToggleVideoPlayPause();
            e.Handled = true;
        }

        private void ToggleVideoPlayPause()
        {
            try
            {
                if (_isVideoPlaying)
                {
                    PART_Video.Pause();
                    _isVideoPlaying = false;
                    _videoTimer.Stop();
                    IconVidPlay.Visibility = Visibility.Visible;
                    IconVidPause.Visibility = Visibility.Collapsed;
                    VideoPauseOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    PART_Video.Play();
                    _isVideoPlaying = true;
                    _videoTimer.Start();
                    IconVidPlay.Visibility = Visibility.Collapsed;
                    IconVidPause.Visibility = Visibility.Visible;
                    VideoPauseOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void BtnVideoMute_Click(object sender, RoutedEventArgs e)
        {
            _isVideoMuted = !_isVideoMuted;
            PART_Video.IsMuted = _isVideoMuted;
            IconVidSoundOn.Visibility = _isVideoMuted ? Visibility.Collapsed : Visibility.Visible;
            IconVidSoundOff.Visibility = _isVideoMuted ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnVideoSpeed_Click(object sender, RoutedEventArgs e)
        {
            _videoSpeed = _videoSpeed switch
            {
                >= 1.95 => 1.0,
                >= 1.45 => 2.0,
                _ => 1.5
            };
            PART_Video.SpeedRatio = _videoSpeed;
            TxtVideoSpeed.Text = _videoSpeed switch
            {
                >= 1.95 => "2x",
                >= 1.45 => "1.5x",
                _ => "1x"
            };
        }

        private void Slider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isUserSeeking = true;
        }

        private void Slider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isUserSeeking = false;
            try
            {
                PART_Video.Position = TimeSpan.FromSeconds(SliderVideoProgress.Value);
            }
            catch { }
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUserSeeking)
            {
                var ts = TimeSpan.FromSeconds(e.NewValue);
                TxtVideoCurrent.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            }
        }

        private void AdjustVideoVolume(double delta)
        {
            _videoVolume = Math.Clamp(_videoVolume + delta, 0.0, 1.0);
            PART_Video.Volume = _videoVolume;
            if (_videoVolume <= 0.001)
            {
                _isVideoMuted = true;
                PART_Video.IsMuted = true;
                IconVidSoundOn.Visibility = Visibility.Collapsed;
                IconVidSoundOff.Visibility = Visibility.Visible;
                IconHudVolume.Data = (System.Windows.Media.Geometry)FindResource("IconVolumeMute");
            }
            else
            {
                _isVideoMuted = false;
                PART_Video.IsMuted = false;
                IconVidSoundOn.Visibility = Visibility.Visible;
                IconVidSoundOff.Visibility = Visibility.Collapsed;
                IconHudVolume.Data = (System.Windows.Media.Geometry)FindResource("IconVolumeUp");
            }

            TxtHudVolume.Text = $"{Math.Round(_videoVolume * 100)}%";
            VolumeHud.Visibility = Visibility.Visible;
            _volumeHudTimer.Stop();
            _volumeHudTimer.Start();
        }

        #endregion

        #region Photo Zoom & Pan

        private void ResetZoomAndPosition()
        {
            _currentZoom = 1.0;
            ImageScale.CenterX = TargetImage.ActualWidth / 2.0;
            ImageScale.CenterY = TargetImage.ActualHeight / 2.0;
            ImageScale.ScaleX = 1.0;
            ImageScale.ScaleY = 1.0;
            ImageTranslate.X = 0;
            ImageTranslate.Y = 0;
            BtnZoomReset.Content = "100%";
        }

        private void ApplyZoom(double newZoom)
        {
            newZoom = Math.Clamp(newZoom, 0.1, 10.0);
            _currentZoom = newZoom;

            ImageScale.CenterX = TargetImage.ActualWidth / 2.0;
            ImageScale.CenterY = TargetImage.ActualHeight / 2.0;
            ImageScale.ScaleX = newZoom;
            ImageScale.ScaleY = newZoom;
            BtnZoomReset.Content = $"{Math.Round(newZoom * 100)}%";

            if (newZoom <= 1.0)
            {
                ImageTranslate.X = 0;
                ImageTranslate.Y = 0;
            }
        }

        private void ScrollViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (_currentZoom > 1.05)
                    ResetZoomAndPosition();
                else
                    ApplyZoom(2.0);
                return;
            }

            if (_currentZoom > 1.0)
            {
                _isDragging = true;
                _lastDragPoint = e.GetPosition(PhotoScrollViewer);
                PhotoScrollViewer.CaptureMouse();
            }
        }

        private void ScrollViewer_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && _currentZoom > 1.0)
            {
                var currentPoint = e.GetPosition(PhotoScrollViewer);
                double deltaX = currentPoint.X - _lastDragPoint.X;
                double deltaY = currentPoint.Y - _lastDragPoint.Y;

                ImageTranslate.X += deltaX;
                ImageTranslate.Y += deltaY;

                _lastDragPoint = currentPoint;
            }
        }

        private void ScrollViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                PhotoScrollViewer.ReleaseMouseCapture();
            }
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => ApplyZoom(_currentZoom * 1.25);
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => ApplyZoom(_currentZoom * 0.8);
        private void BtnZoom100_Click(object sender, RoutedEventArgs e) => ResetZoomAndPosition();
        private void BtnZoomFit_Click(object sender, RoutedEventArgs e) => ResetZoomAndPosition();

        #endregion

        #region Actions & Keyboard

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0) return;
            var cur = _items[_currentIndex];
            try
            {
                if (!cur.IsVideo && _bitmap != null)
                {
                    Clipboard.SetImage(_bitmap);
                }
                else if (File.Exists(cur.FilePath))
                {
                    var files = new System.Collections.Specialized.StringCollection { cur.FilePath };
                    Clipboard.SetFileDropList(files);
                }
            }
            catch (Exception ex)
            {
                App.Log($"[MediaViewer] Clipboard copy failed: {ex.Message}");
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0) return;
            var cur = _items[_currentIndex];
            if (string.IsNullOrEmpty(cur.FilePath) || !File.Exists(cur.FilePath)) return;

            try
            {
                string ext = Path.GetExtension(cur.FilePath).ToLowerInvariant();
                var dlg = new SaveFileDialog
                {
                    Title = "Сохранить медиафайл",
                    FileName = Path.GetFileName(cur.FilePath),
                    Filter = ext switch
                    {
                        ".mp4" => "Видео MP4 (*.mp4)|*.mp4|Все файлы (*.*)|*.*",
                        ".webm" => "Видео WebM (*.webm)|*.webm|Все файлы (*.*)|*.*",
                        ".png" => "Изображение PNG (*.png)|*.png|Все файлы (*.*)|*.*",
                        ".gif" => "GIF-анимация (*.gif)|*.gif|Все файлы (*.*)|*.*",
                        _ => "Все файлы (*.*)|*.*"
                    }
                };

                if (dlg.ShowDialog(this) == true)
                {
                    File.Copy(cur.FilePath, dlg.FileName, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                App.Log($"[MediaViewer] Save failed: {ex.Message}");
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Left)
            {
                NavigatePrev();
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                NavigateNext();
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                var cur = _items[_currentIndex];
                if (cur.IsVideo)
                {
                    AdjustVideoVolume(+0.05);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Down)
            {
                var cur = _items[_currentIndex];
                if (cur.IsVideo)
                {
                    AdjustVideoVolume(-0.05);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Space)
            {
                var cur = _items[_currentIndex];
                if (cur.IsVideo)
                {
                    ToggleVideoPlayPause();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnSave_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnCopy_Click(sender, e);
                e.Handled = true;
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized) ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #region Bottom Action Tray Hover & Animations

        private void BottomActionTray_MouseEnter(object sender, MouseEventArgs e)
        {
            TrayExpandedContent.Visibility = Visibility.Visible;

            var duration = TimeSpan.FromMilliseconds(260);
            var backEase = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.3 };
            var cubicEase = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

            // 1. Expand opacity
            var fadeAnim = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, duration) { EasingFunction = cubicEase };
            TrayExpandedContent.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

            // 2. Scale Tray up slightly from bottom-left corner
            var scaleXAnim = new System.Windows.Media.Animation.DoubleAnimation(0.92, 1.0, duration) { EasingFunction = backEase };
            var scaleYAnim = new System.Windows.Media.Animation.DoubleAnimation(0.92, 1.0, duration) { EasingFunction = backEase };
            TrayScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnim);
            TrayScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnim);

            // 3. Upward flyout translate
            var transYAnim = new System.Windows.Media.Animation.DoubleAnimation(6.0, 0.0, duration) { EasingFunction = cubicEase };
            TrayTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, transYAnim);
        }

        private void BottomActionTray_MouseLeave(object sender, MouseEventArgs e)
        {
            var duration = TimeSpan.FromMilliseconds(180);
            var cubicEase = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn };

            var fadeAnim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, duration) { EasingFunction = cubicEase };
            fadeAnim.Completed += (s, args) =>
            {
                if (!BottomActionTray.IsMouseOver)
                {
                    TrayExpandedContent.Visibility = Visibility.Collapsed;
                }
            };
            TrayExpandedContent.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

            var scaleXAnim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.96, duration) { EasingFunction = cubicEase };
            var scaleYAnim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.96, duration) { EasingFunction = cubicEase };
            TrayScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnim);
            TrayScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnim);
        }

        #endregion

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            if (IconMaximize == null || IconRestore == null) return;
            if (WindowState == WindowState.Maximized)
            {
                IconMaximize.Visibility = Visibility.Collapsed;
                IconRestore.Visibility = Visibility.Visible;
            }
            else
            {
                IconMaximize.Visibility = Visibility.Visible;
                IconRestore.Visibility = Visibility.Collapsed;
            }
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            StopVideo();
            SaveWindowPlacement();
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.SendMyActivityState("IDLE", true);
                _ = new StorageService().SaveSettingsAsync(vm.Settings);
            }
        }

        #endregion
    }
}


