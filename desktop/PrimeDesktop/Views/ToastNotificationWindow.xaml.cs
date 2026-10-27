using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PrimeDesktop.Models;

namespace PrimeDesktop.Views
{
    public partial class ToastNotificationWindow : Window
    {
        private readonly ChatItem _chat;
        private readonly Action<ChatItem> _onClick;
        private readonly DispatcherTimer _autoDismissTimer;
        private bool _isClosing;

        public ToastNotificationWindow(ChatItem chat, string messageText, Action<ChatItem> onClick)
        {
            InitializeComponent();
            _chat = chat;
            _onClick = onClick;

            TxtSender.Text = chat.DisplayName;
            TxtMessage.Text = messageText;

            // Setup avatar
            if (!string.IsNullOrWhiteSpace(chat.AvatarPath) && File.Exists(chat.AvatarPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(Path.GetFullPath(chat.AvatarPath), UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    ImgAvatar.Source = bmp;
                    ImgAvatar.Visibility = Visibility.Visible;
                    AvatarFallback.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    TxtInitials.Text = chat.Initials;
                }
            }
            else
            {
                TxtInitials.Text = chat.Initials;
            }

            _autoDismissTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4.5)
            };
            _autoDismissTimer.Tick += (s, e) => Dismiss();

            MouseEnter += (s, e) => _autoDismissTimer.Stop();
            MouseLeave += (s, e) =>
            {
                if (!_isClosing) _autoDismissTimer.Start();
            };

            Loaded += ToastNotificationWindow_Loaded;
        }

        private void ToastNotificationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Position above taskbar in bottom-right corner
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 16;
            Top = workArea.Bottom - Height - 16;

            // Animate entrance: Slide from right + Fade in
            Opacity = 0.0;
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var slideIn = new DoubleAnimation(60.0, 0.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, fadeIn);
            RootTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);

            _autoDismissTimer.Start();
        }

        public void Dismiss()
        {
            if (_isClosing) return;
            _isClosing = true;
            _autoDismissTimer.Stop();

            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var slideOut = new DoubleAnimation(0.0, 50.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (s, e) =>
            {
                try { Close(); } catch { }
            };

            BeginAnimation(OpacityProperty, fadeOut);
            RootTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            _onClick?.Invoke(_chat);
            Dismiss();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Dismiss();
        }
    }
}
