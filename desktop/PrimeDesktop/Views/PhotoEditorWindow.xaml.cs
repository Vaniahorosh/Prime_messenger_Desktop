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

    public PhotoEditorWindow(string imagePath, bool avatarMode = false)
    {
        InitializeComponent();
        _sourcePath = imagePath;
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Title = "Редактор фото — Prime Desktop";
            TxtEditorHeaderTitle.Text = "🎨 Фоторедактор Прайм";
            TxtEditorHeaderSubtitle.Text = "v0.1";
            BtnApply.Content = "✓ Применить и отправить";
            BtnSaveDisk.Visibility = Visibility.Visible;

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
            transformed.Freeze();
        }

        // 2. Color adjustments
        int b = (int)(SliderBrightness?.Value ?? 0);
        int c = (int)(SliderContrast?.Value ?? 0);
        int s = (int)(SliderSaturation?.Value ?? 0);

        if (b != 0 || c != 0 || s != 0)
        {
            transformed = AdjustImagePixels(transformed, b, c, s);
        }

        TargetImage.Source = transformed;

        // Align InkCanvas size to displayed image size
        CanvasContainer.Width = double.NaN;
        CanvasContainer.Height = double.NaN;
    }

    private BitmapSource AdjustImagePixels(BitmapSource source, int brightness, int contrast, int saturation)
    {
        try
        {
            var formatConverted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = formatConverted.PixelWidth;
            int height = formatConverted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            formatConverted.CopyPixels(pixels, stride, 0);

            float bOffset = brightness * 1.28f;
            float cFactor = (contrast >= 0) ? (1f + contrast / 50f) : (1f + contrast / 100f);
            float sFactor = 1f + saturation / 100f;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];

                float rf = (r - 128f) * cFactor + 128f + bOffset;
                float gf = (g - 128f) * cFactor + 128f + bOffset;
                float bf = (b - 128f) * cFactor + 128f + bOffset;

                if (saturation != 0)
                {
                    float gray = 0.299f * rf + 0.587f * gf + 0.114f * bf;
                    rf = gray + (rf - gray) * sFactor;
                    gf = gray + (gf - gray) * sFactor;
                    bf = gray + (bf - gray) * sFactor;
                }

                pixels[i] = (byte)Math.Clamp(bf, 0f, 255f);
                pixels[i + 1] = (byte)Math.Clamp(gf, 0f, 255f);
                pixels[i + 2] = (byte)Math.Clamp(rf, 0f, 255f);
            }

            var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        catch
        {
            return source;
        }
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
        double sz = Math.Round(e.NewValue);
        if (TxtBrushSize != null) TxtBrushSize.Text = $"{sz} px";
        DrawingCanvas.DefaultDrawingAttributes.Width = sz;
        DrawingCanvas.DefaultDrawingAttributes.Height = sz;
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            DrawingCanvas.DefaultDrawingAttributes.Color = c;
            RadioBrush.IsChecked = true;
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

    private BitmapSource RenderFinalImage()
    {
        var bmpSource = TargetImage.Source as BitmapSource ?? _originalBitmap!;
        int origW = bmpSource.PixelWidth;
        int origH = bmpSource.PixelHeight;

        int outW = origW;
        int outH = origH;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
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
