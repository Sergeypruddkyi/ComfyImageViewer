using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Thumbnails;

public sealed class ThumbnailService : IThumbnailService
{
    private const int LongPathThreshold = 240;

    private readonly string _thumbsDir;

    /// <param name="baseDirOverride">
    /// Базовый каталог кеша (для тестов); null — <c>%LOCALAPPDATA%\ComfyImageViewer</c>.
    /// </param>
    public ThumbnailService(string? baseDirOverride = null)
    {
        var baseDir = baseDirOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComfyImageViewer");
        _thumbsDir = Path.Combine(baseDir, "thumbs");
    }

    public async Task<string?> GetThumbnailAsync(ImageEntry entry, int edgeSize, CancellationToken ct = default)
    {
        try
        {
            if (edgeSize <= 0 || entry.FullPath is null || !File.Exists(EnsureLongPath(entry.FullPath)))
            {
                return null;
            }

            var target = Path.Combine(_thumbsDir, ComputeKey(entry, edgeSize) + ".png");
            if (File.Exists(EnsureLongPath(target)))
            {
                // Повторный вызов — из кеша, без декода.
                return target;
            }

            var png = await RunOnSta(() => EncodeThumbnail(entry, edgeSize)).ConfigureAwait(false);
            if (png is null)
            {
                return null;
            }

            Directory.CreateDirectory(_thumbsDir);

            // Атомарная запись: temp + File.Move (overwrite) — двойная запись не ломает файл.
            var temp = target + ".tmp";
            await File.WriteAllBytesAsync(EnsureLongPath(temp), png, ct).ConfigureAwait(false);
            File.Move(EnsureLongPath(temp), EnsureLongPath(target), overwrite: true);
            return target;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Любой сбой (декод, кеш, файловая система) — null, не бросаем.
            return null;
        }
    }

    private static byte[]? EncodeThumbnail(ImageEntry entry, int edgeSize)
    {
        try
        {
            using var stream = new FileStream(EnsureLongPath(entry.FullPath), FileMode.Open, FileAccess.Read, FileShare.Read);
            var (ihdrWidth, ihdrHeight) = ReadIhdrSize(stream);
            stream.Position = 0;

            // Ориентацию берём из IHDR (первые 24 байта PNG) и ограничиваем бо́льшую сторону;
            // fallback (IHDR недоступен) — DecodePixelWidth = edgeSize.
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = stream;
            if (ihdrWidth > 0 && ihdrHeight > 0)
            {
                if (ihdrWidth >= ihdrHeight)
                {
                    bmp.DecodePixelWidth = edgeSize;
                }
                else
                {
                    bmp.DecodePixelHeight = edgeSize;
                }
            }
            else
            {
                bmp.DecodePixelWidth = edgeSize;
            }

            bmp.EndInit();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Читает размер из IHDR-заголовка PNG (первые 24 байта); при любом сбое — (0, 0).</summary>
    private static (int Width, int Height) ReadIhdrSize(Stream stream)
    {
        var buffer = new byte[24];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0)
            {
                return (0, 0);
            }

            read += n;
        }

        if (buffer[0] != 0x89 || buffer[1] != 0x50 || buffer[2] != 0x4E || buffer[3] != 0x47)
        {
            return (0, 0);
        }

        int width = (buffer[16] << 24) | (buffer[17] << 16) | (buffer[18] << 8) | buffer[19];
        int height = (buffer[20] << 24) | (buffer[21] << 16) | (buffer[22] << 8) | buffer[23];
        return (width, height);
    }

    private static string ComputeKey(ImageEntry entry, int edgeSize)
    {
        var payload = $"{entry.FullPath}|{entry.SizeBytes}|{entry.ModifiedUtc.Ticks}|{edgeSize}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Выполняет WPF-работу (BitmapImage/PngBitmapEncoder) на STA-потоке, как требует WPF.
    /// </summary>
    private static Task<T?> RunOnSta<T>(Func<T?> func)
    {
        var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
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

    /// <summary>Префикс \\?\ для длинных путей (&gt; 240 символов).</summary>
    private static string EnsureLongPath(string path) =>
        path.Length > LongPathThreshold && !path.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? @"\\?\" + path
            : path;
}

