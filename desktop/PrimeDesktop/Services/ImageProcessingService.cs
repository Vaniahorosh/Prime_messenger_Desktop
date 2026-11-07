using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrimeDesktop.Services;

public static class ImageProcessingService
{
    private const int DefaultMaxDimension = 2048;
    private const int DefaultQuality = 82;

    /// <summary>
    /// Optimizes an image for fast network delivery matching mobile client behavior.
    /// Resizes large images to maxDimension and compresses as JPEG.
    /// Returns the compressed byte array, or raw bytes if optimization isn't needed or fails.
    /// </summary>
    public static async Task<byte[]> OptimizeImageForTransferAsync(string filePath, int maxDimension = DefaultMaxDimension, int quality = DefaultQuality)
    {
        return await Task.Run(async () =>
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists) return Array.Empty<byte>();

                // If already small (< 350KB), send original
                if (fileInfo.Length < 350 * 1024)
                {
                    return await File.ReadAllBytesAsync(filePath);
                }

                using var inStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = BitmapDecoder.Create(inStream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                {
                    return await File.ReadAllBytesAsync(filePath);
                }

                var frame = decoder.Frames[0];
                int width = frame.PixelWidth;
                int height = frame.PixelHeight;

                BitmapSource source = frame;
                if (width > maxDimension || height > maxDimension)
                {
                    double scale = Math.Min((double)maxDimension / width, (double)maxDimension / height);
                    var transformed = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                    transformed.Freeze();
                    source = transformed;
                }

                using var outStream = new MemoryStream();
                var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(outStream);
                byte[] optimized = outStream.ToArray();

                if (optimized.Length > 0 && optimized.Length < fileInfo.Length)
                {
                    return optimized;
                }
            }
            catch
            {
                // Fallback to original bytes
            }

            return await File.ReadAllBytesAsync(filePath);
        });
    }
}
