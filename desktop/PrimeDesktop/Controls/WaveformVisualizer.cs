using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PrimeDesktop.Controls
{
    public class WaveformVisualizer : FrameworkElement
    {
        public static readonly DependencyProperty WaveformProperty =
            DependencyProperty.Register(nameof(Waveform), typeof(IReadOnlyList<double>), typeof(WaveformVisualizer),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(nameof(Progress), typeof(double), typeof(WaveformVisualizer),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty PlayedBrushProperty =
            DependencyProperty.Register(nameof(PlayedBrush), typeof(Brush), typeof(WaveformVisualizer),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty UnplayedBrushProperty =
            DependencyProperty.Register(nameof(UnplayedBrush), typeof(Brush), typeof(WaveformVisualizer),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SeekCommandProperty =
            DependencyProperty.Register(nameof(SeekCommand), typeof(ICommand), typeof(WaveformVisualizer),
                new PropertyMetadata(null));

        public static readonly DependencyProperty MessageContextProperty =
            DependencyProperty.Register(nameof(MessageContext), typeof(object), typeof(WaveformVisualizer),
                new PropertyMetadata(null));

        public IReadOnlyList<double>? Waveform
        {
            get => (IReadOnlyList<double>?)GetValue(WaveformProperty);
            set => SetValue(WaveformProperty, value);
        }

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        public Brush? PlayedBrush
        {
            get => (Brush?)GetValue(PlayedBrushProperty);
            set => SetValue(PlayedBrushProperty, value);
        }

        public Brush? UnplayedBrush
        {
            get => (Brush?)GetValue(UnplayedBrushProperty);
            set => SetValue(UnplayedBrushProperty, value);
        }

        public ICommand? SeekCommand
        {
            get => (ICommand?)GetValue(SeekCommandProperty);
            set => SetValue(SeekCommandProperty, value);
        }

        public object? MessageContext
        {
            get => GetValue(MessageContextProperty);
            set => SetValue(MessageContextProperty, value);
        }

        public WaveformVisualizer()
        {
            Cursor = Cursors.Hand;
            ClipToBounds = true;
            MinHeight = 18;
            MinWidth = 80;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                CaptureMouse();
                HandleSeek(e.GetPosition(this).X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
            {
                HandleSeek(e.GetPosition(this).X);
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }

        private void HandleSeek(double mouseX)
        {
            if (ActualWidth <= 0) return;
            double ratio = Math.Clamp(mouseX / ActualWidth, 0.0, 1.0);
            if (SeekCommand != null)
            {
                if (MessageContext != null && SeekCommand.CanExecute((MessageContext, ratio)))
                {
                    SeekCommand.Execute((MessageContext, ratio));
                }
                else if (SeekCommand.CanExecute(ratio))
                {
                    SeekCommand.Execute(ratio);
                }
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var bars = Waveform;
            if (bars == null || bars.Count == 0) return;

            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0) return;

            int count = bars.Count;
            double barWidth = 2.4;
            double gap = Math.Max(1.2, (width - (count * barWidth)) / Math.Max(1, count - 1));

            if (gap < 1.0)
            {
                gap = 1.0;
                barWidth = Math.Max(1.4, (width - (count * gap)) / count);
            }

            double progressX = width * Math.Clamp(Progress, 0.0, 1.0);

            var played = PlayedBrush ?? Brushes.White;
            var unplayed = UnplayedBrush ?? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));

            for (int i = 0; i < count; i++)
            {
                double x = i * (barWidth + gap);
                double barHeight = Math.Max(3.5, bars[i] * height * 0.88);
                double y = (height - barHeight) / 2.0;

                Brush brush = (x + barWidth / 2.0 <= progressX) ? played : unplayed;
                dc.DrawRoundedRectangle(brush, null, new Rect(x, y, barWidth, barHeight), 1.2, 1.2);
            }
        }
    }
}
