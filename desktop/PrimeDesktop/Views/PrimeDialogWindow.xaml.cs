using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PrimeDesktop.Services;

namespace PrimeDesktop.Views
{
    public partial class PrimeDialogWindow : Window
    {
        public bool? DialogResultValue { get; private set; }

        public PrimeDialogWindow()
        {
            InitializeComponent();
            ThemeManager.ApplyWindowDwm(this);

            Loaded += PrimeDialogWindow_Loaded;
        }

        private void PrimeDialogWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Center the transform point based on actual rendered card size
            CardScale.CenterX = DialogCard.ActualWidth / 2.0;
            CardScale.CenterY = DialogCard.ActualHeight / 2.0;

            PlayEntranceAnimation();
        }

        private void PlayEntranceAnimation()
        {
            // Dynamic Spring/Overshoot Animation matching Android PrimeBlurDialog
            var duration = TimeSpan.FromMilliseconds(320);
            var overshootEase = new BackEase
            {
                EasingMode = EasingMode.EaseOut,
                Amplitude = 0.35 // Overshoot bounce factor matching Android OvershootInterpolator(1.2f)
            };
            var cubicEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            // 1. Fade In Opacity
            var fadeAnim = new DoubleAnimation(0.0, 1.0, duration)
            {
                EasingFunction = cubicEase
            };

            // 2. Scale Up from 0.82 to 1.0 with overshoot
            var scaleXAnim = new DoubleAnimation(0.82, 1.0, duration)
            {
                EasingFunction = overshootEase
            };
            var scaleYAnim = new DoubleAnimation(0.82, 1.0, duration)
            {
                EasingFunction = overshootEase
            };

            // 3. Translation from Y=40 down to Y=0
            var translateAnim = new DoubleAnimation(40.0, 0.0, duration)
            {
                EasingFunction = cubicEase
            };

            DialogCard.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            CardTranslate.BeginAnimation(TranslateTransform.YProperty, translateAnim);
        }

        private void BtnNegative_Click(object sender, RoutedEventArgs e)
        {
            DialogResultValue = false;
            Close();
        }

        private void BtnPositive_Click(object sender, RoutedEventArgs e)
        {
            DialogResultValue = true;
            Close();
        }

        /// <summary>
        /// Displays a modern modal dialog styled 1:1 like Android's PrimeBlurDialog.
        /// </summary>
        public static bool? Show(
            string title,
            string? message = null,
            string positiveText = "OK",
            string? negativeText = "Отмена",
            bool isPositiveDanger = false,
            Geometry? iconGeometry = null,
            Brush? iconColor = null,
            Window? owner = null)
        {
            var targetOwner = owner ?? Application.Current?.MainWindow;
            var dlg = new PrimeDialogWindow();

            if (targetOwner != null && targetOwner.IsVisible)
            {
                dlg.Owner = targetOwner;
            }

            dlg.TxtTitle.Text = title;

            if (!string.IsNullOrWhiteSpace(message))
            {
                dlg.TxtMessage.Text = message;
                dlg.TxtMessage.Visibility = Visibility.Visible;
            }
            else
            {
                dlg.TxtMessage.Visibility = Visibility.Collapsed;
            }

            // Icon configuration
            if (iconGeometry != null)
            {
                dlg.IconPath.Data = iconGeometry;
                if (iconColor != null) dlg.IconPath.Fill = iconColor;
                if (isPositiveDanger)
                {
                    dlg.IconBadge.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xF8, 0x51, 0x49));
                    if (Application.Current?.TryFindResource("DangerBrush") is Brush dangerBrush)
                    {
                        dlg.IconPath.Fill = dangerBrush;
                    }
                }
                dlg.IconBadge.Visibility = Visibility.Visible;
            }
            else
            {
                dlg.IconBadge.Visibility = Visibility.Collapsed;
            }

            // Buttons setup
            dlg.BtnPositive.Content = positiveText;
            if (isPositiveDanger)
            {
                if (Application.Current?.TryFindResource("DangerButton") is Style dangerBtnStyle)
                {
                    dlg.BtnPositive.Style = dangerBtnStyle;
                }
            }

            if (string.IsNullOrEmpty(negativeText))
            {
                dlg.BtnNegative.Visibility = Visibility.Collapsed;
                dlg.ColNegative.Width = new GridLength(0);
                dlg.ColSpacer.Width = new GridLength(0);
            }
            else
            {
                dlg.BtnNegative.Content = negativeText;
                dlg.BtnNegative.Visibility = Visibility.Visible;
            }

            dlg.ShowDialog();
            return dlg.DialogResultValue;
        }

        /// <summary>
        /// Modern confirmation dialog helper (Yes/No).
        /// </summary>
        public static bool Confirm(
            string title,
            string message,
            string positiveText = "Да",
            string negativeText = "Отмена",
            bool isDanger = false,
            Geometry? icon = null,
            Window? owner = null)
        {
            var res = Show(
                title: title,
                message: message,
                positiveText: positiveText,
                negativeText: negativeText,
                isPositiveDanger: isDanger,
                iconGeometry: icon,
                owner: owner);

            return res == true;
        }

        /// <summary>
        /// Modern alert dialog helper (OK only).
        /// </summary>
        public static void Alert(
            string title,
            string message,
            string buttonText = "Понятно",
            Geometry? icon = null,
            Window? owner = null)
        {
            Show(
                title: title,
                message: message,
                positiveText: buttonText,
                negativeText: null,
                isPositiveDanger: false,
                iconGeometry: icon,
                owner: owner);
        }
    }
}
