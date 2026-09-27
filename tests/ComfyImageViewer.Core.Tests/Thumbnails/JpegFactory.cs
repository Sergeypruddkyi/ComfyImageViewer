using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ComfyImageViewer.Core.Tests.Thumbnails;

/// <summary>Генерация fixture-JPEG средствами WPF (JpegBitmapEncoder), через STA-раннер.</summary>
internal static class JpegFactory
{
    /// <summary>Создаёт JPEG width×height с залитым прямоугольником (WriteableBitmap + JpegBitmapEncoder).</summary>
    public static byte[] CreateJpeg(int width, int height)
    {
        return StaRunner.RunAsync(() =>
        {
            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            var stride = width * 4;
            var pixels = new byte[stride * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var o = (y * stride) + (x * 4);
                    pixels[o + 0] = 0x20; // B
                    pixels[o + 1] = 0xA0; // G
                    pixels[o + 2] = 0xD0; // R
                    pixels[o + 3] = 0xFF; // A
                }
            }

            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }).GetAwaiter().GetResult();
    }
}
