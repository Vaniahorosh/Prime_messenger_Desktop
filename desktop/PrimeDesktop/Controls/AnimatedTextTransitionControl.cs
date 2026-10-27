using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PrimeDesktop.Controls
{
    /// <summary>
    /// Smooth slide-down text transition control:
    /// - New text appears from top (-offset) to center (0) with fade in (0 -> 1)
    /// - Old text exits from center (0) to down (+offset) with fade out (1 -> 0)
    /// - Seamlessly repeats on every subsequent value/number change.
    /// </summary>
    public class AnimatedTextTransitionControl : ContentControl
    {
        private readonly Grid _container;
        private TextBlock _currentTextBlock;
        private TextBlock _incomingTextBlock;
        private TranslateTransform _currentTransform;
        private TranslateTransform _incomingTransform;
        private bool _isInitialized = false;

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(AnimatedTextTransitionControl),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, OnTextChanged));

        public static readonly DependencyProperty TextTrimmingProperty =
            DependencyProperty.Register(nameof(TextTrimming), typeof(TextTrimming), typeof(AnimatedTextTransitionControl),
                new PropertyMetadata(TextTrimming.None, (d, e) => ((AnimatedTextTransitionControl)d).UpdateTextProperties()));

        public static readonly DependencyProperty TextWrappingProperty =
            DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(AnimatedTextTransitionControl),
                new PropertyMetadata(TextWrapping.NoWrap, (d, e) => ((AnimatedTextTransitionControl)d).UpdateTextProperties()));

        public static readonly DependencyProperty TextAlignmentProperty =
            DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(AnimatedTextTransitionControl),
                new PropertyMetadata(TextAlignment.Left, (d, e) => ((AnimatedTextTransitionControl)d).UpdateTextProperties()));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public TextTrimming TextTrimming
        {
            get => (TextTrimming)GetValue(TextTrimmingProperty);
            set => SetValue(TextTrimmingProperty, value);
        }

        public TextWrapping TextWrapping
        {
            get => (TextWrapping)GetValue(TextWrappingProperty);
            set => SetValue(TextWrappingProperty, value);
        }

        public TextAlignment TextAlignment
        {
            get => (TextAlignment)GetValue(TextAlignmentProperty);
            set => SetValue(TextAlignmentProperty, value);
        }

        public AnimatedTextTransitionControl()
        {
            ClipToBounds = true;
            Focusable = false;

            _currentTransform = new TranslateTransform(0, 0);
            _incomingTransform = new TranslateTransform(0, 0);

            _currentTextBlock = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = _currentTransform,
                Opacity = 1
            };

            _incomingTextBlock = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = _incomingTransform,
                Opacity = 0
            };

            _container = new Grid { ClipToBounds = true };
            _container.Children.Add(_currentTextBlock);
            _container.Children.Add(_incomingTextBlock);

            Content = _container;

            Loaded += (s, e) =>
            {
                UpdateTextProperties();
                if (!_isInitialized && !string.IsNullOrEmpty(Text))
                {
                    _currentTextBlock.Text = Text;
                    _isInitialized = true;
                }
            };
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == ForegroundProperty ||
                e.Property == FontSizeProperty ||
                e.Property == FontWeightProperty ||
                e.Property == FontFamilyProperty ||
                e.Property == FontStyleProperty ||
                e.Property == HorizontalContentAlignmentProperty ||
                e.Property == VerticalContentAlignmentProperty)
            {
                UpdateTextProperties();
            }
        }

        private void UpdateTextProperties()
        {
            ApplyStyle(_currentTextBlock);
            ApplyStyle(_incomingTextBlock);
        }

        private void ApplyStyle(TextBlock tb)
        {
            if (tb == null) return;
            tb.Foreground = Foreground;
            tb.FontSize = FontSize;
            tb.FontWeight = FontWeight;
            tb.FontFamily = FontFamily;
            tb.FontStyle = FontStyle;
            tb.TextTrimming = TextTrimming;
            tb.TextWrapping = TextWrapping;
            tb.TextAlignment = TextAlignment;
            tb.HorizontalAlignment = HorizontalContentAlignment;
            tb.VerticalAlignment = VerticalContentAlignment;
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AnimatedTextTransitionControl ctrl)
            {
                string oldVal = e.OldValue as string ?? string.Empty;
                string newVal = e.NewValue as string ?? string.Empty;
                ctrl.TransitionTo(newVal, oldVal);
            }
        }

        private void TransitionTo(string newText, string oldText)
        {
            if (string.Equals(_currentTextBlock.Text, newText, StringComparison.Ordinal))
            {
                return;
            }

            if (!_isInitialized || !IsLoaded)
            {
                _currentTextBlock.Text = newText;
                _currentTextBlock.Opacity = 1;
                _currentTransform.Y = 0;
                _incomingTextBlock.Opacity = 0;
                _isInitialized = true;
                return;
            }

            UpdateTextProperties();

            // Incoming starts at Top (-offset) and Opacity 0
            _incomingTextBlock.Text = newText;
            double offset = Math.Max(14, FontSize * 1.15);
            _incomingTransform.Y = -offset;
            _incomingTextBlock.Opacity = 0;

            var duration = TimeSpan.FromMilliseconds(240);
            var cubicEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            // 1. Outgoing animation: Center (0) -> Down (+offset) with fade out (1 -> 0)
            var currentTranslateAnim = new DoubleAnimation
            {
                From = _currentTransform.Y,
                To = offset,
                Duration = duration,
                EasingFunction = cubicEase
            };
            var currentOpacityAnim = new DoubleAnimation
            {
                From = _currentTextBlock.Opacity,
                To = 0,
                Duration = duration,
                EasingFunction = cubicEase
            };

            // 2. Incoming animation: Top (-offset) -> Center (0) with fade in (0 -> 1)
            var incomingTranslateAnim = new DoubleAnimation
            {
                From = -offset,
                To = 0,
                Duration = duration,
                EasingFunction = cubicEase
            };
            var incomingOpacityAnim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = duration,
                EasingFunction = cubicEase
            };

            incomingTranslateAnim.Completed += (s, e) =>
            {
                // Swap current and incoming
                var tempTb = _currentTextBlock;
                _currentTextBlock = _incomingTextBlock;
                _incomingTextBlock = tempTb;

                var tempTrans = _currentTransform;
                _currentTransform = _incomingTransform;
                _incomingTransform = tempTrans;

                _incomingTextBlock.Text = string.Empty;
                _incomingTextBlock.Opacity = 0;
                _incomingTransform.Y = 0;
            };

            _currentTransform.BeginAnimation(TranslateTransform.YProperty, currentTranslateAnim);
            _currentTextBlock.BeginAnimation(UIElement.OpacityProperty, currentOpacityAnim);

            _incomingTransform.BeginAnimation(TranslateTransform.YProperty, incomingTranslateAnim);
            _incomingTextBlock.BeginAnimation(UIElement.OpacityProperty, incomingOpacityAnim);
        }
    }
}
