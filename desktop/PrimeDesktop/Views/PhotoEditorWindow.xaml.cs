using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PrimeDesktop.Views;

public partial class PhotoEditorWindow : Window
{
    private readonly string _sourcePath;
    private BitmapSource? _originalBitmap;
    private int _rotationAngle = 0;
    private bool _flipH = false;
    private bool _flipV = false;

    public string? ResultFilePath { get; private set; }

    public bool IsAvatarMode { get; set; } = false;

    public PhotoEditorWindow(string imagePath, bool avatarMode = false)
    {
        InitializeComponent();
        _sourcePath = imagePath;
        IsAvatarMode = avatarMode;
        Loaded += OnWindowLoaded;
        SizeChanged += (s, e) => UpdateCropOverlay();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (IsAvatarMode)
            {
                ChkAvatarCrop.IsChecked = true;
            }
            var uri = new Uri(_sourcePath);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = uri;
            bmp.EndInit();
            bmp.Freeze();

            _originalBitmap = bmp;
            ApplyTransformsAndFilters();

            // Default brush
            DrawingCanvas.DefaultDrawingAttributes.Color = (Color)ColorConverter.ConvertFromString("#4D9FFF");
            DrawingCanvas.DefaultDrawingAttributes.Width = 4;
            DrawingCanvas.DefaultDrawingAttributes.Height = 4;
            DrawingCanvas.DefaultDrawingAttributes.FitToCurve = true;
        }
        catch (Exception ex)
        {
            PrimeDialogWindow.Alert("Ошибка", $"Не удалось загрузить изображение: {ex.Message}", "Понятно", owner: this);
            Close();
        }
    }

    private void ApplyTransformsAndFilters()
    {
        if (_originalBitmap == null) return;

        // 1. Geometric transforms
        var transformGroup = new TransformGroup();
        if (_rotationAngle != 0)
        {
            transformGroup.Children.Add(new RotateTransform(_rotationAngle));
        }
        if (_flipH || _flipV)
        {
            transformGroup.Children.Add(new ScaleTransform(_flipH ? -1 : 1, _flipV ? -1 : 1));
        }

        BitmapSource transformed = _originalBitmap;
        if (transformGroup.Children.Count > 0)
        {
            transformed = new TransformedBitmap(_originalBitmap, transformGroup);
        }

        // 2. Brightness / Contrast / Saturation pixel adjustment
        double brightness = SliderBrightness?.Value ?? 0;
        double contrast = SliderContrast?.Value ?? 0;
        double saturation = SliderSaturation?.Value ?? 0;

        if (brightness != 0 || contrast != 0 || saturation != 0)
        {
            transformed = AdjustPixels(transformed, brightness, contrast, saturation);
        }

        TargetImage.Source = transformed;
        CanvasContainer.Width = TargetImage.ActualWidth > 0 ? TargetImage.ActualWidth : double.NaN;
        CanvasContainer.Height = TargetImage.ActualHeight > 0 ? TargetImage.ActualHeight : double.NaN;
    }

    private BitmapSource AdjustPixels(BitmapSource source, double brightness, double contrast, double saturation)
    {
        // Convert to standard Bgr32
        var formatConverted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = formatConverted.PixelWidth;
        int height = formatConverted.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        formatConverted.CopyPixels(pixels, stride, 0);

        // Precompute factors
        double bFactor = brightness * 1.5; // -150 to +150
        double cFactor = (contrast + 100.0) / 100.0; // 0.0 to 2.0
        cFactor = cFactor * cFactor; // steeper curve
        double sFactor = (saturation + 100.0) / 100.0; // 0.0 to 2.0

        for (int i = 0; i < pixels.Length; i += 4)
        {
            double b = pixels[i];
            double g = pixels[i + 1];
            double r = pixels[i + 2];

            // 1. Brightness
            r += bFactor;
            g += bFactor;
            b += bFactor;

            // 2. Contrast around midpoint 128
            r = ((r - 128.0) * cFactor) + 128.0;
            g = ((g - 128.0) * cFactor) + 128.0;
            b = ((b - 128.0) * cFactor) + 128.0;

            // 3. Saturation (grayscale weighted)
            if (sFactor != 1.0)
            {
                double gray = (0.299 * r) + (0.587 * g) + (0.114 * b);
                r = gray + ((r - gray) * sFactor);
                g = gray + ((g - gray) * sFactor);
                b = gray + ((b - gray) * sFactor);
            }

            pixels[i] = (byte)Math.Clamp(b, 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(g, 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(r, 0, 255);
        }

        return BitmapSource.Create(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, pixels, stride);
    }

    private void BtnRotateCW_Click(object sender, RoutedEventArgs e)
    {
        _rotationAngle = (_rotationAngle + 90) % 360;
        ApplyTransformsAndFilters();
    }

    private void BtnRotateCCW_Click(object sender, RoutedEventArgs e)
    {
        _rotationAngle = (_rotationAngle - 90 + 360) % 360;
        ApplyTransformsAndFilters();
    }

    private void BtnFlipH_Click(object sender, RoutedEventArgs e)
    {
        _flipH = !_flipH;
        ApplyTransformsAndFilters();
    }

    private void BtnFlipV_Click(object sender, RoutedEventArgs e)
    {
        _flipV = !_flipV;
        ApplyTransformsAndFilters();
    }

    private void FilterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtBrightness != null && SliderBrightness != null)
            TxtBrightness.Text = $"{(int)SliderBrightness.Value}";
        if (TxtContrast != null && SliderContrast != null)
            TxtContrast.Text = $"{(int)SliderContrast.Value}";
        if (TxtSaturation != null && SliderSaturation != null)
            TxtSaturation.Text = $"{(int)SliderSaturation.Value}";

        ApplyTransformsAndFilters();
    }

    private void SliderBrushSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DrawingCanvas == null) return;
        double sz = SliderBrushSize.Value;
        if (TxtBrushSize != null) TxtBrushSize.Text = $"{(int)sz} px";
        DrawingCanvas.DefaultDrawingAttributes.Width = sz;
        DrawingCanvas.DefaultDrawingAttributes.Height = sz;
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            DrawingCanvas.DefaultDrawingAttributes.Color = c;
            if (RadioBrush != null) RadioBrush.IsChecked = true;
            DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;
        }
    }

    private void DrawingMode_Checked(object sender, RoutedEventArgs e)
    {
        if (DrawingCanvas == null) return;
        if (RadioEraser?.IsChecked == true)
        {
            DrawingCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
        }
        else
        {
            DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;
        }
    }

    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        if (DrawingCanvas.Strokes.Count > 0)
        {
            DrawingCanvas.Strokes.RemoveAt(DrawingCanvas.Strokes.Count - 1);
        }
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _rotationAngle = 0;
        _flipH = false;
        _flipV = false;
        if (SliderBrightness != null) SliderBrightness.Value = 0;
        if (SliderContrast != null) SliderContrast.Value = 0;
        if (SliderSaturation != null) SliderSaturation.Value = 0;
        DrawingCanvas.Strokes.Clear();
        ApplyTransformsAndFilters();
    }

    private void ChkAvatarCrop_Checked(object sender, RoutedEventArgs e)
    {
        CropOverlayGrid.Visibility = Visibility.Visible;
        UpdateCropOverlay();
    }

    private void ChkAvatarCrop_Unchecked(object sender, RoutedEventArgs e)
    {
        CropOverlayGrid.Visibility = Visibility.Collapsed;
    }

    private void UpdateCropOverlay()
    {
        if (CropOverlayGrid == null || CropOverlayGrid.Visibility != Visibility.Visible) return;

        double w = CanvasContainer.ActualWidth;
        double h = CanvasContainer.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            w = TargetImage.ActualWidth;
            h = TargetImage.ActualHeight;
        }
        if (w <= 0 || h <= 0) return;

        double size = Math.Min(w, h);
        CropFullRect.Rect = new Rect(0, 0, Math.Max(w, CropOverlayGrid.ActualWidth), Math.Max(h, CropOverlayGrid.ActualHeight));
        CropHoleCircle.Center = new Point(w / 2.0, h / 2.0);
        CropHoleCircle.RadiusX = size / 2.0;
        CropHoleCircle.RadiusY = size / 2.0;

        CropFrameBorder.Width = size;
        CropFrameBorder.Height = size;
    }

    private BitmapSource RenderFinalImage()
    {
        var bmpSource = TargetImage.Source as BitmapSource ?? _originalBitmap!;
        int origW = bmpSource.PixelWidth;
        int origH = bmpSource.PixelHeight;

        bool cropToAvatar = ChkAvatarCrop.IsChecked == true;
        int outW = origW;
        int outH = origH;

        if (cropToAvatar)
        {
            int side = Math.Min(origW, origH);
            outW = side;
            outH = side;
        }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (cropToAvatar)
            {
                int side = Math.Min(origW, origH);
                double offsetX = (origW - side) / 2.0;
                double offsetY = (origH - side) / 2.0;

                // Push avatar clip (circle)
                var clipGeo = new EllipseGeometry(new Point(side / 2.0, side / 2.0), side / 2.0, side / 2.0);
                dc.PushClip(clipGeo);

                // Draw source image shifted
                dc.DrawImage(bmpSource, new Rect(-offsetX, -offsetY, origW, origH));

                if (DrawingCanvas.Strokes.Count > 0)
                {
                    double scaleX = (double)origW / (TargetImage.ActualWidth > 0 ? TargetImage.ActualWidth : origW);
                    double scaleY = (double)origH / (TargetImage.ActualHeight > 0 ? TargetImage.ActualHeight : origH);
                    dc.PushTransform(new TransformGroup
                    {
                        Children =
                        {
                            new ScaleTransform(scaleX, scaleY),
                            new TranslateTransform(-offsetX / scaleX, -offsetY / scaleY)
                        }
                    });
                    DrawingCanvas.Strokes.Draw(dc);
                    dc.Pop();
                }

                dc.Pop(); // Pop clip
            }
            else
            {
                // Draw transformed photo
                dc.DrawImage(bmpSource, new Rect(0, 0, outW, outH));

                // Scale and draw ink strokes if any
                if (DrawingCanvas.Strokes.Count > 0)
                {
                    double scaleX = (double)outW / (TargetImage.ActualWidth > 0 ? TargetImage.ActualWidth : outW);
                    double scaleY = (double)outH / (TargetImage.ActualHeight > 0 ? TargetImage.ActualHeight : outH);
                    dc.PushTransform(new ScaleTransform(scaleX, scaleY));
                    DrawingCanvas.Strokes.Draw(dc);
                    dc.Pop();
                }
            }
        }

        var rtb = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private void SaveBitmapToFile(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void BtnSaveDisk_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Сохранить отредактированное фото",
            Filter = "Изображение PNG (*.png)|*.png|Изображение JPEG (*.jpg)|*.jpg",
            FileName = "prime_edited_photo.png"
        };
        if (dlg.ShowDialog() == true)
        {
            var final = RenderFinalImage();
            SaveBitmapToFile(final, dlg.FileName);
            PrimeDialogWindow.Alert("Прайм", "Изображение успешно сохранено!", "Отлично", owner: this);
        }
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "PrimeDesktop");
        Directory.CreateDirectory(tempDir);
        string outPath = Path.Combine(tempDir, $"edited_{Guid.NewGuid():N}.png");

        var final = RenderFinalImage();
        SaveBitmapToFile(final, outPath);

        ResultFilePath = outPath;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
