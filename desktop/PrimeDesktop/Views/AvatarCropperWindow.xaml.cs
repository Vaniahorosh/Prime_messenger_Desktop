using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrimeDesktop.Services;

namespace PrimeDesktop.Views;

public partial class AvatarCropperWindow : Window
{
    private readonly string _sourcePath;
    private BitmapSource? _originalBitmap;
    private int _rotationAngle = 0;

    private double _zoom = 1.0;
    private double _baseZoom = 1.0;
    private double _panX = 0;
    private double _panY = 0;

    private bool _isDragging = false;
    private Point _dragStart;
    private double _initialPanX;
    private double _initialPanY;

    private const double CropSide = 280.0;
    private Rect _cropRect;

    public string? CroppedResultPath { get; private set; }

    public AvatarCropperWindow(string imagePath)
    {
        InitializeComponent();
        _sourcePath = imagePath;
        Loaded += OnLoaded;
        SizeChanged += (s, e) => UpdateLayoutGeometry();
        PrimeAnimationService.HookBrandTransitions(this, RootBorder, isDialog: true);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(_sourcePath);
            bmp.EndInit();
            bmp.Freeze();

            _originalBitmap = bmp;
            ImgPreview.Source = _originalBitmap;
            ImgPreview.Width = bmp.PixelWidth;
            ImgPreview.Height = bmp.PixelHeight;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateLayoutGeometry();
                ResetToDefault();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось загрузить изображение: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
            Close();
        }
    }

    private void UpdateLayoutGeometry()
    {
        if (ViewportGrid == null) return;
        double w = ViewportGrid.ActualWidth;
        double h = ViewportGrid.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double cropLeft = (w - CropSide) / 2.0;
        double cropTop = (h - CropSide) / 2.0;
        _cropRect = new Rect(cropLeft, cropTop, CropSide, CropSide);

        CropFullRect.Rect = new Rect(0, 0, w, h);
        CropHoleRect.Rect = _cropRect;
        double radius = CropSide * 0.15; // 15% corner radius
        CropHoleRect.RadiusX = radius;
        CropHoleRect.RadiusY = radius;

        CropBorder.Width = CropSide;
        CropBorder.Height = CropSide;
        CropBorder.CornerRadius = new CornerRadius(radius);
        CropBorder.Margin = new Thickness(cropLeft, cropTop, 0, 0);
    }

    private void ResetToDefault()
    {
        if (_originalBitmap == null || ViewportGrid == null) return;

        double w = ViewportGrid.ActualWidth;
        double h = ViewportGrid.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double bmpW = _originalBitmap.PixelWidth;
        double bmpH = _originalBitmap.PixelHeight;

        _rotationAngle = 0;
        _baseZoom = Math.Max(CropSide / bmpW, CropSide / bmpH);
        _zoom = _baseZoom;

        _panX = (w / 2.0) - (bmpW / 2.0) * _zoom;
        _panY = (h / 2.0) - (bmpH / 2.0) * _zoom;

        ApplyTransform();
    }

    private void ApplyTransform()
    {
        if (_originalBitmap == null) return;

        double bmpW = _originalBitmap.PixelWidth;
        double bmpH = _originalBitmap.PixelHeight;

        var group = new TransformGroup();
        group.Children.Add(new RotateTransform(_rotationAngle, bmpW / 2.0, bmpH / 2.0));
        group.Children.Add(new ScaleTransform(_zoom, _zoom));
        group.Children.Add(new TranslateTransform(_panX, _panY));

        ImgPreview.RenderTransform = group;
    }

    private void ZoomAround(Point center, double factor)
    {
        double minZoom = _baseZoom * 0.25;
        double maxZoom = _baseZoom * 10.0;
        double newZoom = Math.Clamp(_zoom * factor, minZoom, maxZoom);
        if (Math.Abs(newZoom - _zoom) < 0.0001) return;

        double actualFactor = newZoom / _zoom;
        _panX = center.X - (center.X - _panX) * actualFactor;
        _panY = center.Y - (center.Y - _panY) * actualFactor;
        _zoom = newZoom;

        ApplyTransform();
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(ViewportGrid);
            _initialPanX = _panX;
            _initialPanY = _panY;
            ViewportGrid.CaptureMouse();
            Cursor = Cursors.SizeAll;
        }
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            Point current = e.GetPosition(ViewportGrid);
            _panX = _initialPanX + (current.X - _dragStart.X);
            _panY = _initialPanY + (current.Y - _dragStart.Y);
            ApplyTransform();
        }
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ViewportGrid.ReleaseMouseCapture();
            Cursor = Cursors.Hand;
        }
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Smooth zoom supporting Ctrl + MouseWheel or direct MouseWheel over viewport
        Point mousePos = e.GetPosition(ViewportGrid);
        double factor = e.Delta > 0 ? 1.10 : 0.909;
        ZoomAround(mousePos, factor);
        e.Handled = true;
    }

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        Point center = new Point(ViewportGrid.ActualWidth / 2.0, ViewportGrid.ActualHeight / 2.0);
        ZoomAround(center, 1.15);
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        Point center = new Point(ViewportGrid.ActualWidth / 2.0, ViewportGrid.ActualHeight / 2.0);
        ZoomAround(center, 0.87);
    }

    private void BtnRotateCW_Click(object sender, RoutedEventArgs e)
    {
        _rotationAngle = (_rotationAngle + 90) % 360;
        ApplyTransform();
    }

    private void BtnRotateCCW_Click(object sender, RoutedEventArgs e)
    {
        _rotationAngle = (_rotationAngle - 90 + 360) % 360;
        ApplyTransform();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefault();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (_originalBitmap == null) return;

        try
        {
            const int targetSize = 512;
            double radius = targetSize * 0.15; // 15% corner radius

            double bmpW = _originalBitmap.PixelWidth;
            double bmpH = _originalBitmap.PixelHeight;
            double renderScale = (double)targetSize / CropSide;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var clipGeo = new RectangleGeometry(new Rect(0, 0, targetSize, targetSize), radius, radius);
                dc.PushClip(clipGeo);

                var group = new TransformGroup();
                group.Children.Add(new RotateTransform(_rotationAngle, bmpW / 2.0, bmpH / 2.0));
                group.Children.Add(new ScaleTransform(_zoom, _zoom));
                group.Children.Add(new TranslateTransform(_panX, _panY));
                group.Children.Add(new TranslateTransform(-_cropRect.X, -_cropRect.Y));
                group.Children.Add(new ScaleTransform(renderScale, renderScale));

                dc.PushTransform(group);
                dc.DrawImage(_originalBitmap, new Rect(0, 0, bmpW, bmpH));
                dc.Pop(); // transform
                dc.Pop(); // clip
            }

            var rtb = new RenderTargetBitmap(targetSize, targetSize, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            string tempDir = Path.Combine(Path.GetTempPath(), "PrimeDesktop");
            Directory.CreateDirectory(tempDir);
            string tempPath = Path.Combine(tempDir, $"avatar_crop_{Guid.NewGuid():N}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(tempPath))
            {
                encoder.Save(fs);
            }

            CroppedResultPath = tempPath;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка сохранения аватара: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
