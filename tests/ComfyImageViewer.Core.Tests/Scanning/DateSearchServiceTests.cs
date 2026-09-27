using System.IO;
using ComfyImageViewer.Core.Models;
using ComfyImageViewer.Core.Scanning;
using ComfyImageViewer.Core.Tests.Metadata;
using ComfyImageViewer.Core.Tests.Thumbnails;
using Xunit;

namespace ComfyImageViewer.Core.Tests.Scanning;

public class DateSearchServiceTests
{
    // ExifTestFactory.CreateTiff(): DateTimeOriginal = "2026:01:02 03:04:05".
    private static readonly DateOnly ExifDate = new(2026, 1, 2);

    [Fact]
    public async Task JpegWithExifDateTimeOriginal_UsesExifDate_NotFileDate()
    {
        using var _ = new TempDir(out var dir);
        var jpeg = InjectJpegExif(JpegFactory.CreateJpeg(16, 16), ExifTestFactory.CreateTiff());
        var path = TestTree.WriteFile(dir, "photo.jpg", jpeg);
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 5, 5, 12, 0, 0));

        var hits = await Query(dir, ExifDate, ExifDate);
        Assert.Single(hits);
        AssertSamePath(path, hits[0]);

        var byFileDate = await Query(dir, new DateOnly(2020, 5, 5), new DateOnly(2020, 5, 5));
        Assert.Empty(byFileDate);
    }

    [Fact]
    public async Task JpegWithoutDateTimeOriginal_FallsBackToFileDate_EvenWithDateTimeTag()
    {
        using var _ = new TempDir(out var dir);
        // Только 0x0132 (DateTime), без 0x9003: по спецификации поиска это «нет DateTimeOriginal».
        var jpeg = InjectJpegExif(JpegFactory.CreateJpeg(16, 16), CreateTiffWithDateTimeOnly());
        var path = TestTree.WriteFile(dir, "giza-like.jpg", jpeg);
        File.SetLastWriteTimeUtc(path, new DateTime(2010, 9, 26, 12, 0, 0));

        var hits = await Query(dir, new DateOnly(2010, 9, 26), new DateOnly(2010, 9, 26));
        Assert.Single(hits);
        AssertSamePath(path, hits[0]);
    }

    [Fact]
    public async Task PngWithoutExif_UsesFileDate()
    {
        using var _ = new TempDir(out var dir);
        var path = TestTree.WriteFile(dir, "plain.png", PngFactory.CreatePng(8, 8));
        File.SetLastWriteTimeUtc(path, new DateTime(2024, 3, 10, 12, 0, 0));

        var hits = await Query(dir, new DateOnly(2024, 3, 10), new DateOnly(2024, 3, 10));
        Assert.Single(hits);
        AssertSamePath(path, hits[0]);
    }

    [Fact]
    public async Task WebPWithExif_UsesExifDate()
    {
        using var _ = new TempDir(out var dir);
        var path = TestTree.WriteFile(dir, "photo.webp", WebpTestFactory.CreateWebPWithExif(ExifTestFactory.CreateTiff()));
        File.SetLastWriteTimeUtc(path, new DateTime(1999, 1, 1, 12, 0, 0));

        var hits = await Query(dir, ExifDate, ExifDate);
        Assert.Single(hits);
        AssertSamePath(path, hits[0]);
    }

    [Fact]
    public async Task Range_IsInclusiveOnBothEnds()
    {
        using var _ = new TempDir(out var dir);
        var path = TestTree.WriteFile(dir, "a.jpg", InjectJpegExif(JpegFactory.CreateJpeg(16, 16), ExifTestFactory.CreateTiff()));

        Assert.Single(await Query(dir, new DateOnly(2026, 1, 1), ExifDate));
        Assert.Single(await Query(dir, ExifDate, new DateOnly(2026, 1, 3)));
        Assert.Empty(await Query(dir, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));
        Assert.Empty(await Query(dir, new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 4)));
    }

    [Fact]
    public async Task Recursive_FlatResultWithRealPaths_IncludesNestedFolders()
    {
        using var _ = new TempDir(out var dir);
        var rootFile = TestTree.WriteFile(dir, "root.png", PngFactory.CreatePng(8, 8));
        var nested = TestTree.WriteFile(dir, Path.Combine("a", "b", "deep.jpg"),
            InjectJpegExif(JpegFactory.CreateJpeg(16, 16), ExifTestFactory.CreateTiff()));
        File.SetLastWriteTimeUtc(rootFile, new DateTime(2026, 1, 2, 12, 0, 0)); // та же дата, что EXIF вложенного

        var hits = await Query(dir, ExifDate, ExifDate);

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.FullPath == rootFile);
        Assert.Contains(hits, h => h.FullPath == nested);
        Assert.All(hits, h => Assert.False(Directory.Exists(h.FullPath))); // плоский список файлов
    }

    [Fact]
    public async Task UnsupportedAndBrokenFiles_AreSkippedOrFallBack()
    {
        using var _ = new TempDir(out var dir);
        TestTree.WriteFile(dir, "notes.txt", "hello"u8.ToArray());
        var broken = TestTree.WriteFile(dir, "broken.jpg", [0xFF, 0xD8, 0x00]); // битый/без EXIF → fallback на дату файла

        var fileDate = DateOnly.FromDateTime(File.GetLastWriteTime(broken));
        var hits = await Query(dir, fileDate, fileDate);

        // Ровно битый jpg (по дате файла); txt в поиск не попадает, падения нет.
        Assert.Single(hits);
        Assert.Equal("broken.jpg", hits[0].Name);
    }

    private static async Task<List<ImageEntry>> Query(string dir, DateOnly from, DateOnly to)
    {
        var service = new DateSearchService();
        return await service.FindAsync(dir, from, to).ToListAsync();
    }

    private static void AssertSamePath(string expected, ImageEntry entry) =>
        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(entry.FullPath));

    /// <summary>Вставляет APP1-"Exif"-сегмент сразу после SOI в JPEG, закодированный WPF.</summary>
    private static byte[] InjectJpegExif(byte[] jpeg, byte[] tiff)
    {
        var payload = new byte[6 + tiff.Length];
        "Exif\0\0"u8.CopyTo(payload);
        tiff.CopyTo(payload, 6);

        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        segment[2] = (byte)((payload.Length + 2) >> 8);
        segment[3] = (byte)((payload.Length + 2) & 0xFF);
        payload.CopyTo(segment, 4);

        var result = new byte[2 + segment.Length + jpeg.Length - 2];
        Array.Copy(jpeg, 0, result, 0, 2);
        Array.Copy(segment, 0, result, 2, segment.Length);
        Array.Copy(jpeg, 2, result, 2 + segment.Length, jpeg.Length - 2);
        return result;
    }

    /// <summary>TIFF с единственной записью IFD0: DateTime (0x0132), без ExifIFD/DateTimeOriginal.</summary>
    private static byte[] CreateTiffWithDateTimeOnly()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write((byte)0x49); w.Write((byte)0x49); // "II"
            w.Write((ushort)42);
            w.Write((uint)8); // IFD0 offset

            // IFD0: 1 запись; value area сразу после IFD0 (8 + 2 + 12 + 4 = 26).
            w.Write((ushort)1);
            w.Write((ushort)0x0132); w.Write((ushort)2); w.Write((uint)20); w.Write((uint)26);
            w.Write((uint)0); // next IFD = 0
            w.Write("2010:09:26 03:04:03\0"u8.ToArray());
        }

        return ms.ToArray();
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _dir;

        public TempDir(out string dir)
        {
            _dir = dir = TestTree.CreateTempDir();
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }
    }
}
