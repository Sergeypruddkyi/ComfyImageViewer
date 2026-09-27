using System.IO;
using ComfyImageViewer.Core.Models;
using ComfyImageViewer.Core.Tests.Scanning;
using ComfyImageViewer.Core.Thumbnails;
using Xunit;

namespace ComfyImageViewer.Core.Tests.Thumbnails;

public class ThumbnailServiceTests
{
    private static (string BaseDir, string ArchiveDir) CreateDirs()
    {
        var baseDir = TestTree.CreateTempDir();
        var archiveDir = Path.Combine(baseDir, "archive");
        Directory.CreateDirectory(archiveDir);
        return (baseDir, archiveDir);
    }

    private static ImageEntry WriteEntry(string archiveDir, byte[] png, string name = "big.png")
    {
        var path = TestTree.WriteFile(archiveDir, name, png);
        var fi = new FileInfo(path);
        return new ImageEntry(fi.FullName, name, fi.Length, fi.CreationTimeUtc, fi.LastWriteTimeUtc);
    }

    /// <summary>Кейс 4: PNG 2000×100 → thumb создан, декодируем, меньше оригинала; оригинал не изменён.</summary>
    [Fact]
    public async Task GetThumbnailAsync_CreatesDecodableThumb_SmallerThanOriginal()
    {
        var (baseDir, archiveDir) = CreateDirs();
        try
        {
            var png = PngFactory.CreatePng(2000, 100);
            var entry = WriteEntry(archiveDir, png);

            var service = new ThumbnailService(baseDir);
            var thumbPath = await service.GetThumbnailAsync(entry, edgeSize: 48);

            Assert.NotNull(thumbPath);
            Assert.True(File.Exists(thumbPath));
            Assert.StartsWith(Path.Combine(baseDir, "thumbs"), thumbPath);
            Assert.EndsWith(".png", thumbPath, StringComparison.Ordinal);

            // Файл кеша декодируем и он меньше оригинала.
            var (thumbW, thumbH) = PngFactory.DecodeSize(thumbPath);
            Assert.Equal(48, Math.Max(thumbW, thumbH));
            Assert.True(thumbW < 2000);

            // Оригинал не изменён.
            Assert.Equal(png.Length, new FileInfo(entry.FullPath).Length);
            Assert.Equal(png, File.ReadAllBytes(entry.FullPath));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    /// <summary>Кейс 4: повторный вызов — тот же путь (из кеша); edgeSize влияет на ключ.</summary>
    [Fact]
    public async Task GetThumbnailAsync_Repeat_UsesSamePath_AndEdgeSizeAffectsKey()
    {
        var (baseDir, archiveDir) = CreateDirs();
        try
        {
            var entry = WriteEntry(archiveDir, PngFactory.CreatePng(2000, 100));
            var service = new ThumbnailService(baseDir);

            var path48 = await service.GetThumbnailAsync(entry, 48);
            var path48Again = await service.GetThumbnailAsync(entry, 48);
            var path96 = await service.GetThumbnailAsync(entry, 96);

            Assert.NotNull(path48);
            Assert.NotNull(path96);
            Assert.Equal(path48, path48Again);
            Assert.NotEqual(path48, path96);
            Assert.True(File.Exists(path96));

            var files = Directory.GetFiles(Path.Combine(baseDir, "thumbs"), "*.png");
            Assert.Equal(2, files.Length); // два разных edgeSize → два файла кеша
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    /// <summary>Кеш-ключ зависит от mtime/размера файла: перезапись → другой путь.</summary>
    [Fact]
    public async Task GetThumbnailAsync_ChangedFile_GetsNewKey()
    {
        var (baseDir, archiveDir) = CreateDirs();
        try
        {
            var entry = WriteEntry(archiveDir, PngFactory.CreatePng(2000, 100));
            var service = new ThumbnailService(baseDir);

            var first = await service.GetThumbnailAsync(entry, 48);
            Assert.NotNull(first);

            // Файл перезаписали → SizeBytes/mtime меняются → другой ключ.
            Thread.Sleep(50);
            var bigger = PngFactory.CreatePng(2000, 120);
            File.WriteAllBytes(entry.FullPath, bigger);
            var fi = new FileInfo(entry.FullPath);
            var updated = new ImageEntry(fi.FullName, entry.Name, fi.Length, fi.CreationTimeUtc, fi.LastWriteTimeUtc);

            var second = await service.GetThumbnailAsync(updated, 48);
            Assert.NotNull(second);
            Assert.NotEqual(first, second);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    /// <summary>Любой сбой декода — null, без исключений.</summary>
    [Fact]
    public async Task GetThumbnailAsync_CorruptFile_ReturnsNull()
    {
        var (baseDir, archiveDir) = CreateDirs();
        try
        {
            var entry = WriteEntry(archiveDir, [0xDE, 0xAD, 0xBE, 0xEF], "corrupt.png");
            var service = new ThumbnailService(baseDir);

            var result = await service.GetThumbnailAsync(entry, 48);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    /// <summary>Несуществующий файл — null.</summary>
    [Fact]
    public async Task GetThumbnailAsync_MissingFile_ReturnsNull()
    {
        var (baseDir, _) = CreateDirs();
        try
        {
            var entry = new ImageEntry(@"Z:\nonexistent\nope.png", "nope.png", 1, DateTime.UtcNow, DateTime.UtcNow);
            var service = new ThumbnailService(baseDir);

            Assert.Null(await service.GetThumbnailAsync(entry, 48));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    /// <summary>JPEG: thumb создан, декодируем (большая сторона = edgeSize); оригинал не изменён.</summary>
    [Fact]
    public async Task GetThumbnailAsync_Jpeg_CreatesDecodableThumb()
    {
        var (baseDir, archiveDir) = CreateDirs();
        try
        {
            var jpeg = JpegFactory.CreateJpeg(1600, 900);
            var entry = WriteEntry(archiveDir, jpeg, "photo.jpg");

            var service = new ThumbnailService(baseDir);
            var thumbPath = await service.GetThumbnailAsync(entry, edgeSize: 48);

            Assert.NotNull(thumbPath);
            Assert.True(File.Exists(thumbPath));

            // Кеш-thumb — валидный PNG (кеш всегда PNG), большая сторона = edgeSize.
            var (thumbW, thumbH) = PngFactory.DecodeSize(thumbPath);
            Assert.Equal(48, Math.Max(thumbW, thumbH));

            // Оригинал не изменён.
            Assert.Equal(jpeg.Length, new FileInfo(entry.FullPath).Length);
            Assert.Equal(jpeg, File.ReadAllBytes(entry.FullPath));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    /// <summary>
    /// WebP (минимальный валидный 1×1 VP8L): thumb создаётся средствами системного WIC-кодека.
    /// Если в системе нет WebP-кодека — тест молча завершается (skip), как junction-тест.
    /// </summary>
    [Fact]
    public async Task GetThumbnailAsync_WebP_CreatesThumb_WhenSystemCodecAvailable()
    {
        var (baseDir, archiveDir) = CreateDirs();
        try
        {
            // Канонический минимальный 1×1 lossless WebP.
            const string webpBase64 = "UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA==";
            var webp = Convert.FromBase64String(webpBase64);
            var entry = WriteEntry(archiveDir, webp, "tiny.webp");

            // Проверка доступности системного WebP-кодека: если декод невозможен — skip.
            var sourcePath = entry.FullPath;
            bool decodable;
            try
            {
                PngFactory.DecodeSize(sourcePath);
                decodable = true;
            }
            catch
            {
                decodable = false;
            }

            if (!decodable)
            {
                return; // кодека нет — поведение thumbnail'ов не проверяем
            }

            var service = new ThumbnailService(baseDir);
            var thumbPath = await service.GetThumbnailAsync(entry, edgeSize: 48);

            Assert.NotNull(thumbPath);
            Assert.True(File.Exists(thumbPath));
            var (thumbW, thumbH) = PngFactory.DecodeSize(thumbPath);
            Assert.Equal(48, Math.Max(thumbW, thumbH));

            // Оригинал не изменён.
            Assert.Equal(webp.Length, new FileInfo(entry.FullPath).Length);
            Assert.Equal(webp, File.ReadAllBytes(entry.FullPath));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }

    /// <summary>Битый JPEG и битый WebP: декод не удался — null, без исключений (список не ломается).</summary>
    [Fact]
    public async Task GetThumbnailAsync_CorruptJpegAndWebp_ReturnsNull()
    {
        var (baseDir, archiveDir) = CreateDirs();
        try
        {
            var jpgEntry = WriteEntry(archiveDir, [0xFF, 0xD8, 0xFF, 0x00, 0x13, 0x37], "broken.jpg");
            var webpEntry = WriteEntry(archiveDir, [0x52, 0x49, 0x46, 0x46, 0x00, 0xBA, 0xAD, 0x00], "broken.webp");
            var service = new ThumbnailService(baseDir);

            Assert.Null(await service.GetThumbnailAsync(jpgEntry, 48));
            Assert.Null(await service.GetThumbnailAsync(webpEntry, 48));
        }
        finally
        {
            Directory.Delete(baseDir, recursive: true);
        }
    }
}
