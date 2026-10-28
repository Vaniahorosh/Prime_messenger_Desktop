using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrimeSetup
{
    public partial class AvatarCropperDialog : Window
    {
        private readonly string _sourcePath;
        private BitmapSource? _originalBitmap;
        private int _rotationAngle = 0;

        public string? CroppedResultPath { get; private set; }

        public AvatarCropperDialog(string imagePath)
        {
            InitializeComponent();
            _sourcePath = imagePath;
            Loaded += OnLoaded;
            SizeChanged += (s, e) => UpdateMask();
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
                UpdateImageTransform();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось загрузить изображение: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private void UpdateImageTransform()
        {
            if (_originalBitmap == null) return;
            if (_rotationAngle != 0)
            {
                var tb = new TransformedBitmap(_originalBitmap, new RotateTransform(_rotationAngle));
                tb.Freeze();
                ImgPreview.Source = tb;
            }
            else
            {
                ImgPreview.Source = _originalBitmap;
            }

            Dispatcher.BeginInvoke(new Action(UpdateMask), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void UpdateMask()
        {
            if (ViewportGrid == null) return;
            double w = ViewportGrid.ActualWidth;
            double h = ViewportGrid.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double side = Math.Min(w, h) * 0.85;
            CropFullRect.Rect = new Rect(0, 0, w, h);
            CropHoleCircle.Center = new Point(w / 2.0, h / 2.0);
            CropHoleCircle.RadiusX = side / 2.0;
            CropHoleCircle.RadiusY = side / 2.0;

            CropBorder.Width = side;
            CropBorder.Height = side;
        }

        private void BtnRotateCCW_Click(object sender, RoutedEventArgs e)
        {
            _rotationAngle = (_rotationAngle - 90) % 360;
            UpdateImageTransform();
        }

        private void BtnRotateCW_Click(object sender, RoutedEventArgs e)
        {
            _rotationAngle = (_rotationAngle + 90) % 360;
            UpdateImageTransform();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var bmpSource = ImgPreview.Source as BitmapSource ?? _originalBitmap;
                if (bmpSource == null) return;

                int origW = bmpSource.PixelWidth;
                int origH = bmpSource.PixelHeight;
                int side = Math.Min(origW, origH);
                double offsetX = (origW - side) / 2.0;
                double offsetY = (origH - side) / 2.0;

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    var clipGeo = new EllipseGeometry(new Point(side / 2.0, side / 2.0), side / 2.0, side / 2.0);
                    dc.PushClip(clipGeo);
                    dc.DrawImage(bmpSource, new Rect(-offsetX, -offsetY, origW, origH));
                    dc.Pop();
                }

                var rtb = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                rtb.Freeze();

                string tempPath = Path.Combine(Path.GetTempPath(), $"prime_avatar_{Guid.NewGuid():N}.png");
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
                MessageBox.Show($"Ошибка обработки аватара: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
