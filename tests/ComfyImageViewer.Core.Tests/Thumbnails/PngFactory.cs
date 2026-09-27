using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ComfyImageViewer.Core.Tests.Thumbnails;

/// <summary>WPF-код (RenderTargetBitmap/BitmapImage) требует STA — гоняем через выделенный поток.</summary>
internal static class StaRunner
{
    public static Task<T> RunAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}

/// <summary>Генерация fixture-PNG средствами WPF (PngBitmapEncoder), как предусмотрено планом.</summary>
internal static class PngFactory
{
    /// <summary>Создаёт PNG width×height с залитым прямоугольником (WriteableBitmap + PngBitmapEncoder).</summary>
    public static byte[] CreatePng(int width, int height)
    {
        return StaRunner.RunAsync(() =>
        {
            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
            var stride = width * 4;
            var pixels = new byte[stride * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var o = (y * stride) + (x * 4);
                    pixels[o + 0] = 0x90; // B
                    pixels[o + 1] = 0x80; // G
                    pixels[o + 2] = 0x46; // R
                    pixels[o + 3] = 0xFF; // A
                }
            }

            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }).GetAwaiter().GetResult();
    }

    /// <summary>Декодирует PNG-файл и возвращает (pixelWidth, pixelHeight) — для проверки thumb кеша.</summary>
    public static (int Width, int Height) DecodeSize(string pngPath)
    {
        return StaRunner.RunAsync(() =>
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(pngPath);
            bmp.EndInit();
            return (bmp.PixelWidth, bmp.PixelHeight);
        }).GetAwaiter().GetResult();
    }
}
