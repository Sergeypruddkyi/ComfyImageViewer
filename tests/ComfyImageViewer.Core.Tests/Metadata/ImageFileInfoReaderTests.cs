using System.IO;
using ComfyImageViewer.Core.Metadata;
using ComfyImageViewer.Core.Models;
using ComfyImageViewer.Core.Tests.Metadata;
using ComfyImageViewer.Core.Tests.Scanning;
using ComfyImageViewer.Core.Tests.Thumbnails;
using Xunit;

namespace ComfyImageViewer.Core.Tests.Metadata;

public class ImageFileInfoReaderTests
{
    private readonly ImageFileInfoReader _reader = new();

    [Fact]
    public void Read_Jpeg_ReturnsDimensions_AndNoFields_WithoutExif()
    {
        var dir = TestTree.CreateTempDir();
        try
        {
            var path = TestTree.WriteFile(dir, "photo.jpg", JpegFactory.CreateJpeg(320, 200));

            var info = _reader.Read(path);

            Assert.NotNull(info);
            Assert.Equal(320, info!.PixelWidth);
            Assert.Equal(200, info.PixelHeight);
            Assert.Empty(info.Fields); // JpegBitmapEncoder не пишет EXIF
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_JpegWithCraftedExif_ReturnsCameraSoftwareAndDateFields()
    {
        var dir = TestTree.CreateTempDir();
        try
        {
            var path = TestTree.WriteFile(dir, "exif.jpg", InjectJpegExif(JpegFactory.CreateJpeg(64, 48), ExifTestFactory.CreateTiff()));

            var info = _reader.Read(path);

            Assert.NotNull(info);
            Assert.Equal(64, info!.PixelWidth);
            Assert.Equal(48, info.PixelHeight);

            Assert.Collection(
                info.Fields,
                f => { Assert.Equal("Camera", f.Label); Assert.Equal("CivCam X100", f.Value); Assert.False(f.HiddenByDefault); },
                f =>
                {
                    // Software в JPEG скрыт из краткой панели, но остаётся в полном наборе («Открыть всё»).
                    Assert.Equal("Software", f.Label);
                    Assert.Equal("UnitTest", f.Value);
                    Assert.True(f.HiddenByDefault);
                },
                f => { Assert.Equal("Дата съёмки", f.Label); Assert.Equal("2026:01:02 03:04:05", f.Value); Assert.False(f.HiddenByDefault); });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_JpegWithCraftedExif_ExposesRawDateTimeOriginal()
    {
        var dir = TestTree.CreateTempDir();
        try
        {
            var path = TestTree.WriteFile(dir, "exif.jpg", InjectJpegExif(JpegFactory.CreateJpeg(64, 48), ExifTestFactory.CreateTiff()));

            var info = _reader.Read(path);

            Assert.NotNull(info);
            Assert.Equal("2026:01:02 03:04:05", info!.DateTimeOriginal); // строго 0x9003 — для панели деталей
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_JpegWithoutExif_DateTimeOriginalIsNull()
    {
        var dir = TestTree.CreateTempDir();
        try
        {
            var path = TestTree.WriteFile(dir, "photo.jpg", JpegFactory.CreateJpeg(320, 200));

            var info = _reader.Read(path);

            Assert.NotNull(info);
            Assert.Null(info!.DateTimeOriginal); // нет 0x9003 → панель использует fallback CreatedUtc
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_WebPWithExif_SoftwareIsHiddenLikeJpeg()
    {
        var dir = TestTree.CreateTempDir();
        try
        {
            var path = TestTree.WriteFile(dir, "withexif.webp", WebpTestFactory.CreateWebPWithExif(ExifTestFactory.CreateTiff()));

            var info = _reader.Read(path);

            Assert.NotNull(info);
            var software = Assert.Single(info!.Fields, f => f.Label == "Software");
            Assert.Equal("UnitTest", software.Value);
            Assert.True(software.HiddenByDefault); // WebP ведёт себя так же, как JPEG
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_WebP_ReturnsDimensions()
    {
        var dir = TestTree.CreateTempDir();
        try
        {
            var path = TestTree.WriteFile(dir, "tiny.webp", Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA=="));

            var info = _reader.Read(path);

            Assert.NotNull(info);
            Assert.Equal(1, info!.PixelWidth);
            Assert.Equal(1, info.PixelHeight);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_CorruptOrMissingFile_ReturnsNull()
    {
        var dir = TestTree.CreateTempDir();
        try
        {
            var corrupt = TestTree.WriteFile(dir, "broken.jpg", [0xFF, 0xD8, 0x00, 0xBA, 0xAD]);

            Assert.Null(_reader.Read(corrupt));
            Assert.Null(_reader.Read(Path.Combine(dir, "missing.png")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // --- GPS/Location (ExifGps через ридер) ---

    [Fact]
    public void Read_JpegWithGps_NorthEast_ReturnsSignedDecimalDegrees()
    {
        // 55°45'20.88" N, 37°37'02.28" E → 55.7558, 37.6173 (в т. ч. дробные секунды, знаменатель 100)
        var info = ReadJpegWithExif(ExifTestFactory.CreateTiffWithGps(
            [R(55, 1), R(45, 1), R(2088, 100)],
            [R(37, 1), R(37, 1), R(228, 100)],
            "N", "E"));

        Assert.NotNull(info);
        Assert.NotNull(info!.Gps);
        Assert.Equal(55.7558, info.Gps!.Latitude, 6); // DMS→decimal, точность до 6 знаков
        Assert.Equal(37.6173, info.Gps.Longitude, 6);
        Assert.True(info.Gps.Latitude > 0 && info.Gps.Longitude > 0);
    }

    [Fact]
    public void Read_JpegWithGps_SouthWest_AppliesNegativeSign()
    {
        var info = ReadJpegWithExif(ExifTestFactory.CreateTiffWithGps(
            [R(33, 1), R(51, 1), R(5436, 100)],
            [R(151, 1), R(12, 1), R(3564, 100)],
            "S", "W"));

        Assert.NotNull(info?.Gps);
        Assert.Equal(-33.8651, info!.Gps!.Latitude, 6);
        Assert.Equal(-151.2099, info.Gps.Longitude, 6);
    }

    [Fact]
    public void Read_JpegEmptyGpsIfd_CountZero_ReturnsNoLocation()
    {
        // Заглушка privacy-stripping из реальных фото (E:\Foto_Test\Camera\IMG_*):
        // указатель 0x8825 есть, GPS IFD пуст (count=0) — это НЕ позиция.
        var info = ReadJpegWithExif(ExifTestFactory.CreateTiffWithGps(
            [R(55, 1), R(45, 1), R(0, 1)], [R(37, 1), R(37, 1), R(0, 1)], "N", "E",
            emptyGpsIfd: true));

        Assert.NotNull(info);
        Assert.Null(info!.Gps);
    }

    [Fact]
    public void Read_JpegWithoutGpsPointer_ReturnsNoLocation()
    {
        var info = ReadJpegWithExif(ExifTestFactory.CreateTiffWithGps(null, null, null, null, includeGpsPointer: false));

        Assert.NotNull(info);
        Assert.Null(info!.Gps);
    }

    [Fact]
    public void Read_JpegGpsZeroDenominator_ReturnsNoLocation()
    {
        var info = ReadJpegWithExif(ExifTestFactory.CreateTiffWithGps(
            [R(55, 1), R(45, 1), R(0, 0)], // нулевой знаменатель — невалидно
            [R(37, 1), R(37, 1), R(0, 1)],
            "N", "E"));

        Assert.NotNull(info);
        Assert.Null(info!.Gps);
    }

    [Fact]
    public void Read_JpegGpsOutOfRange_ReturnsNoLocation()
    {
        var info = ReadJpegWithExif(ExifTestFactory.CreateTiffWithGps(
            [R(91, 1), R(0, 1), R(0, 1)], // deg > 90 для широты
            [R(37, 1), R(37, 1), R(0, 1)],
            "N", "E"));

        Assert.NotNull(info);
        Assert.Null(info!.Gps);
    }

    [Fact]
    public void Read_JpegGpsInvalidReference_ReturnsNoLocation()
    {
        var info = ReadJpegWithExif(ExifTestFactory.CreateTiffWithGps(
            [R(55, 1), R(45, 1), R(0, 1)],
            [R(37, 1), R(37, 1), R(0, 1)],
            "X", "E")); // REF вне N/S/E/W

        Assert.NotNull(info);
        Assert.Null(info!.Gps);
    }

    [Fact]
    public void Read_JpegGpsMissingReference_ReturnsNoLocation()
    {
        var info = ReadJpegWithExif(ExifTestFactory.CreateTiffWithGps(
            [R(55, 1), R(45, 1), R(0, 1)],
            [R(37, 1), R(37, 1), R(0, 1)],
            latRef: null, lonRef: "E")); // DMS есть, REF отсутствует

        Assert.NotNull(info);
        Assert.Null(info!.Gps);
    }

    [Fact]
    public void Read_JpegGpsBigEndian_ReturnsLocation()
    {
        // Реальные телефонные JPEG — TIFF "MM" (big-endian); порядок байт не должен ломать GPS.
        var info = ReadJpegWithExif(ExifTestFactory.CreateTiffWithGps(
            [R(55, 1), R(45, 1), R(2088, 100)],
            [R(37, 1), R(37, 1), R(228, 100)],
            "N", "E", bigEndian: true));

        Assert.NotNull(info?.Gps);
        Assert.Equal(55.7558, info!.Gps!.Latitude, 6);
        Assert.Equal(37.6173, info.Gps.Longitude, 6);
    }

    [Fact]
    public void Read_WebPWithGps_ReturnsLocation()
    {
        var dir = TestTree.CreateTempDir();
        try
        {
            var tiff = ExifTestFactory.CreateTiffWithGps(
                [R(55, 1), R(45, 1), R(2088, 100)],
                [R(37, 1), R(37, 1), R(228, 100)],
                "N", "E");
            var path = TestTree.WriteFile(dir, "gps.webp", WebpTestFactory.CreateWebPWithExif(tiff));

            var info = _reader.Read(path);

            Assert.NotNull(info?.Gps);
            Assert.Equal(55.7558, info!.Gps!.Latitude, 6);
            Assert.Equal(37.6173, info.Gps.Longitude, 6);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_JpegWithGpsAndDateTimeOriginal_BothSurvive()
    {
        // GPS не должен мешать принятой DateTimeOriginal-логике (тот же тифф, IFD0 с двумя указателями).
        var info = ReadJpegWithExif(ExifTestFactory.CreateTiffWithGps(
            [R(55, 1), R(45, 1), R(2088, 100)],
            [R(37, 1), R(37, 1), R(228, 100)],
            "N", "E", withDateTimeOriginal: true));

        Assert.NotNull(info);
        Assert.Equal("2026:01:02 03:04:05", info!.DateTimeOriginal);
        Assert.Contains(info.Fields, f => f.Label == "Дата съёмки");
        Assert.Equal(55.7558, info.Gps!.Latitude, 6);
    }

    private ImageFileInfo? ReadJpegWithExif(byte[] tiff)
    {
        var dir = TestTree.CreateTempDir();
        try
        {
            var path = TestTree.WriteFile(dir, "gps.jpg", InjectJpegExif(JpegFactory.CreateJpeg(64, 48), tiff));
            return _reader.Read(path);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static ExifRational R(uint numerator, uint denominator) => new(numerator, denominator);

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

        var result = new byte[2 + segment.Length + (jpeg.Length - 2)];
        result[0] = jpeg[0]; // SOI
        result[1] = jpeg[1];
        segment.CopyTo(result, 2);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + segment.Length));
        return result;
    }
}
