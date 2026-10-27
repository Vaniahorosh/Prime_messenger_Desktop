using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PrimeDesktop.Services;

namespace PrimeDesktop.Views;

public partial class PhotoViewerWindow : Window
{
    private string? _currentPath;
    private BitmapImage? _bitmap;
    private Point _lastDragPoint;
    private bool _isDragging = false;
    private double _currentZoom = 1.0;

    public PhotoViewerWindow(string imagePath)
    {
        InitializeComponent();

        StateChanged += Window_StateChanged;
        Loaded += Window_Loaded;
        PreviewKeyDown += Window_PreviewKeyDown;

        LoadPhoto(imagePath);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.ApplyWindowDwm(this);
        WindowUtils.EnableWindowMinMaxHook(this);
    }

    public void LoadPhoto(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try
        {
            _currentPath = path;
            TxtFileName.Text = Path.GetFileName(path);

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();

            _bitmap = bi;
            TargetImage.Source = bi;

            long sizeKb = new FileInfo(path).Length / 1024;
            TxtPhotoDetails.Text = $"• {bi.PixelWidth} × {bi.PixelHeight} px ({sizeKb} КБ)";

            ResetZoomAndPosition();
        }
        catch (Exception ex)
        {
            App.Log($"[PhotoViewer ERROR] Failed to load image: {ex.Message}");
        }
    }

    private void ResetZoomAndPosition()
    {
        _currentZoom = 1.0;
        ImageScale.ScaleX = 1.0;
        ImageScale.ScaleY = 1.0;
        ImageTranslate.X = 0;
        ImageTranslate.Y = 0;
        BtnZoomReset.Content = "100%";
    }

    private void ApplyZoom(double newZoom, Point? center = null)
    {
        newZoom = Math.Clamp(newZoom, 0.1, 10.0);
        _currentZoom = newZoom;

        ImageScale.ScaleX = newZoom;
        ImageScale.ScaleY = newZoom;
        BtnZoomReset.Content = $"{Math.Round(newZoom * 100)}%";

        if (newZoom <= 1.0)
        {
            ImageTranslate.X = 0;
            ImageTranslate.Y = 0;
        }
    }

    #region Window TitleBar & Chrome Actions

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                BtnMaximize_Click(sender, e);
                return;
            }

            if (WindowState == WindowState.Maximized)
            {
                var mousePos = PointToScreen(e.GetPosition(this));
                var ratio = e.GetPosition(this).X / Math.Max(ActualWidth, 1);
                WindowState = WindowState.Normal;
                Left = mousePos.X - (ActualWidth * ratio);
                Top = mousePos.Y - 18;
            }

            DragMove();
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

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (IconMaximize == null || IconRestore == null || BtnMaximize == null) return;

        if (WindowState == WindowState.Maximized)
        {
            IconMaximize.Visibility = Visibility.Collapsed;
            IconRestore.Visibility = Visibility.Visible;
            BtnMaximize.ToolTip = "Восстановить";
        }
        else
        {
            IconMaximize.Visibility = Visibility.Visible;
            IconRestore.Visibility = Visibility.Collapsed;
            BtnMaximize.ToolTip = "Развернуть";
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
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
        else if (e.Key == Key.OemPlus || e.Key == Key.Add)
        {
            BtnZoomIn_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
        {
            BtnZoomOut_Click(sender, e);
            e.Handled = true;
        }
    }

    #endregion

    #region Zoom & Pan Viewport

    private void ScrollViewer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.15 : 0.85;
        ApplyZoom(_currentZoom * factor);
        e.Handled = true;
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

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ApplyZoom(_currentZoom * 1.25);
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ApplyZoom(_currentZoom * 0.8);
    }

    private void BtnZoom100_Click(object sender, RoutedEventArgs e)
    {
        ResetZoomAndPosition();
    }

    private void BtnZoomFit_Click(object sender, RoutedEventArgs e)
    {
        ResetZoomAndPosition();
    }

    #endregion

    #region Action Buttons

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_bitmap == null) return;
        try
        {
            Clipboard.SetImage(_bitmap);
        }
        catch (Exception ex)
        {
            App.Log($"[PhotoViewer ERROR] Clipboard copy failed: {ex.Message}");
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentPath) || !File.Exists(_currentPath)) return;

        try
        {
            string ext = Path.GetExtension(_currentPath).ToLowerInvariant();
            var dlg = new SaveFileDialog
            {
                Title = "Сохранить изображение",
                FileName = Path.GetFileName(_currentPath),
                Filter = ext switch
                {
                    ".png" => "Изображение PNG (*.png)|*.png|Все файлы (*.*)|*.*",
                    ".gif" => "GIF-анимация (*.gif)|*.gif|Все файлы (*.*)|*.*",
                    _ => "Изображение JPEG (*.jpg)|*.jpg|Все файлы (*.*)|*.*"
                }
            };

            if (dlg.ShowDialog(this) == true)
            {
                File.Copy(_currentPath, dlg.FileName, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[PhotoViewer ERROR] Save failed: {ex.Message}");
        }
    }

    #endregion
}
