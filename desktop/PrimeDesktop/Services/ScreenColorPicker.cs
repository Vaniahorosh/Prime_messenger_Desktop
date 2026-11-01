using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PrimeDesktop.Services;

/// <summary>
/// Screen Eyedropper Service: Allows picking pixel colors directly from anywhere on the screen
/// with real-time cursor magnification, live hex readout, and DPI-aware overlay.
/// </summary>
public static class ScreenColorPicker
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

    /// <summary>
    /// Samples the exact color of the pixel currently under the hardware mouse cursor.
    /// Returns null if unable to access desktop DC.
    /// </summary>
    public static Color? SamplePixelAtCursor(out int screenX, out int screenY)
    {
        screenX = 0;
        screenY = 0;
        try
        {
            if (!GetCursorPos(out var pt)) return null;
            screenX = pt.x;
            screenY = pt.y;

            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return null;

            uint pixel = GetPixel(hdc, pt.x, pt.y);
            ReleaseDC(IntPtr.Zero, hdc);

            // Win32 COLORREF format: 0x00bbggrr
            byte r = (byte)(pixel & 0xFF);
            byte g = (byte)((pixel >> 8) & 0xFF);
            byte b = (byte)((pixel >> 16) & 0xFF);
            return Color.FromRgb(r, g, b);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Launches an interactive fullscreen eyedropper overlay window.
    /// When the user clicks on any pixel, <paramref name="onColorPicked"/> is called with "#RRGGBB".
    /// Pressing Escape or Right-Click cancels without changing color.
    /// </summary>
    public static void StartInteractivePick(Action<string> onColorPicked)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.Invoke(() =>
        {
            try
            {
                var overlay = new Window
                {
                    Title = "Prime Messenger Eyedropper",
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), // Minimal alpha for hit testing
                    Topmost = true,
                    ShowInTaskbar = false,
                    Left = SystemParameters.VirtualScreenLeft,
                    Top = SystemParameters.VirtualScreenTop,
                    Width = SystemParameters.VirtualScreenWidth,
                    Height = SystemParameters.VirtualScreenHeight,
                    Cursor = Cursors.Cross
                };

                var canvas = new Canvas();
                overlay.Content = canvas;

                // Floating Loupe / Info Badge
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromArgb(235, 18, 25, 36)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 6, 12, 6),
                    IsHitTestVisible = false,
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 18,
                        ShadowDepth = 4,
                        Direction = 270,
                        Opacity = 0.65,
                        Color = Colors.Black
                    }
                };

                var sp = new StackPanel { Orientation = Orientation.Horizontal };

                var swatch = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1.5),
                    Margin = new Thickness(0, 0, 8, 0)
                };

                var hexText = new TextBlock
                {
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = new FontFamily("Consolas, Segoe UI")
                };

                var hintText = new TextBlock
                {
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(140, 155, 170)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0),
                    Text = "ЛКМ: выбрать • Esc: отмена"
                };

                sp.Children.Add(swatch);
                sp.Children.Add(hexText);
                sp.Children.Add(hintText);
                badge.Child = sp;
                canvas.Children.Add(badge);

                void UpdateSample()
                {
                    var color = SamplePixelAtCursor(out _, out _);
                    if (color.HasValue)
                    {
                        swatch.Background = new SolidColorBrush(color.Value);
                        hexText.Text = $"#{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}";
                    }

                    if (GetCursorPos(out var pt))
                    {
                        var wpfPt = overlay.PointFromScreen(new Point(pt.x, pt.y));
                        double x = wpfPt.X + 22;
                        double y = wpfPt.Y + 22;

                        if (x + 250 > overlay.ActualWidth)
                        {
                            x = wpfPt.X - 260;
                        }
                        if (y + 50 > overlay.ActualHeight)
                        {
                            y = wpfPt.Y - 55;
                        }

                        Canvas.SetLeft(badge, Math.Max(0, x));
                        Canvas.SetTop(badge, Math.Max(0, y));
                    }
                }

                overlay.MouseMove += (s, e) => UpdateSample();

                overlay.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    var color = SamplePixelAtCursor(out _, out _);
                    overlay.Close();
                    if (color.HasValue)
                    {
                        string hex = $"#{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}";
                        onColorPicked?.Invoke(hex);
                    }
                    e.Handled = true;
                };

                overlay.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        overlay.Close();
                        e.Handled = true;
                    }
                };

                overlay.PreviewMouseRightButtonDown += (s, e) =>
                {
                    overlay.Close();
                    e.Handled = true;
                };

                overlay.Loaded += (s, e) =>
                {
                    overlay.Activate();
                    overlay.Focus();
                    UpdateSample();
                };

                overlay.Show();
            }
            catch { }
        });
    }
}
