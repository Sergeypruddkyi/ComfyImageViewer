using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Metadata;

/// <summary>
/// Размеры изображения — через WIC (PNG/JPEG/WebP), дополнительная metadata —
/// из EXIF JPEG (APP1) и EXIF-чанка WebP (RIFF). Никаких записей в файл.
/// </summary>
public sealed class ImageFileInfoReader : IImageFileInfoReader
{
    public ImageFileInfo? Read(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnDemand);
            var frame = decoder.Frames[0];

            var (fields, dateOriginal, gps) = ReadFields(stream);
            return new ImageFileInfo(frame.PixelWidth, frame.PixelHeight, fields, dateOriginal, gps);
        }
        catch
        {
            // Недекодируемый/недоступный файл — null, решение о отображении принимает вызывающий код.
            return null;
        }
    }

    private static (List<MetadataField> Fields, string? DateTimeOriginal, GpsLocationInfo? Gps) ReadFields(FileStream stream)
    {
        stream.Position = 0;
        Span<byte> magic = stackalloc byte[12];
        FillExactly(stream, magic);
        stream.Position = 0;

        var tags = new Dictionary<int, string>();
        GpsLocationInfo? gps = null;

        if (magic[0] == 0xFF && magic[1] == 0xD8)
        {
            var exif = ExifBlocks.FindJpegExif(stream);
            if (exif is { Length: > 8 })
            {
                tags = ExifTiff.Parse(exif);
                gps = ExifGps.TryParse(exif);
            }
        }
        else if (magic[0] == 0x52 && magic[1] == 0x49 && magic[8] == 0x57) // "RI....W"
        {
            var exif = ExifBlocks.FindWebPExif(stream);
            if (exif is { Length: > 8 })
            {
                tags = ExifTiff.Parse(exif);
                gps = ExifGps.TryParse(exif);
            }
        }
        else
        {
            return ([], null, null);
        }

        // Сырой DateTimeOriginal (строго 0x9003) для панели деталей; не подменяется 0x0132.
        var original = tags.GetValueOrDefault(ExifTiff.TagDateTimeOriginal);

        var fields = new List<MetadataField>();

        var make = tags.GetValueOrDefault(ExifTiff.TagMake);
        var model = tags.GetValueOrDefault(ExifTiff.TagModel);
        var camera = string.Join(" ", new[] { make, model }.Where(s => !string.IsNullOrEmpty(s))).Trim();
        if (camera.Length > 0)
        {
            fields.Add(new MetadataField("Camera", camera));
        }

        // Software (UUID-подобные значения генераторов, JPEG и WebP) не показывается
        // в краткой панели; доступен через «Открыть всё».
        var software = tags.GetValueOrDefault(ExifTiff.TagSoftware);
        if (!string.IsNullOrEmpty(software))
        {
            fields.Add(new MetadataField("Software", Truncate(software), HiddenByDefault: true));
        }

        var taken = tags.GetValueOrDefault(ExifTiff.TagDateTimeOriginal) ?? tags.GetValueOrDefault(ExifTiff.TagDateTime);
        if (!string.IsNullOrEmpty(taken))
        {
            fields.Add(new MetadataField("Дата съёмки", Truncate(taken)));
        }

        return (fields, original, gps);
    }

    private static string Truncate(string value)
    {
        value = value.Trim();
        return value.Length <= 300 ? value : value[..300] + "…";
    }

    private static void FillExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            total += read;
        }
    }
}

/// <summary>Минимальный TIFF/EXIF-парсер: IFD0 + SubIFD (0x8769), ASCII-значения, оба порядка байт.</summary>
public static class ExifTiff
{
    public const int TagMake = 0x010F;
    public const int TagModel = 0x0110;
    public const int TagSoftware = 0x0131;
    public const int TagDateTime = 0x0132;
    public const int TagDateTimeOriginal = 0x9003;
    private const int TagExifIfdPointer = 0x8769;

    public static Dictionary<int, string> Parse(byte[] tiff)
    {
        var result = new Dictionary<int, string>();
        try
        {
            if (tiff.Length < 8)
            {
                return result;
            }

            bool littleEndian = tiff[0] == 0x49 && tiff[1] == 0x49; // "II", иначе "MM"
            if (!littleEndian && !(tiff[0] == 0x4D && tiff[1] == 0x4D))
            {
                return result;
            }

            uint ifd0 = littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(4))
                : BinaryPrimitives.ReadUInt32BigEndian(tiff.AsSpan(4));

            ReadIfd(tiff, ifd0, littleEndian, result, depth: 0);
        }
        catch
        {
            // Битый EXIF — возвращаем то, что успели прочитать.
        }

        return result;
    }

    private static void ReadIfd(byte[] tiff, uint offset, bool littleEndian, Dictionary<int, string> result, int depth)
    {
        if (depth > 2 || offset == 0 || offset + 2 > tiff.Length)
        {
            return;
        }

        uint count = ReadU16(tiff, (int)offset, littleEndian);
        if (count > 512)
        {
            return;
        }

        for (uint i = 0; i < count; i++)
        {
            long entry = offset + 2 + 12L * i;
            if (entry + 12 > tiff.Length)
            {
                return;
            }

            int tag = (int)ReadU16(tiff, (int)entry, littleEndian);
            int type = (int)ReadU16(tiff, (int)entry + 2, littleEndian);
            uint valueCount = ReadU32(tiff, (int)entry + 4, littleEndian);
            uint valueOffset = ReadU32(tiff, (int)entry + 8, littleEndian);

            if (type == 2) // ASCII
            {
                int length = (int)Math.Min(valueCount, 4096u);
                int dataStart = length <= 4 ? (int)entry + 8 : (int)valueOffset;
                if (dataStart >= 0 && dataStart + length <= tiff.Length)
                {
                    var text = Encoding.ASCII.GetString(tiff, dataStart, length).Trim('\0').Trim();
                    if (text.Length > 0)
                    {
                        result[tag] = text;
                    }
                }
            }

            if (tag == TagExifIfdPointer && type == 4)
            {
                ReadIfd(tiff, valueOffset, littleEndian, result, depth + 1);
            }
        }
    }

    private static uint ReadU16(byte[] tiff, int offset, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(offset))
            : BinaryPrimitives.ReadUInt16BigEndian(tiff.AsSpan(offset));

    private static uint ReadU32(byte[] tiff, int offset, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(offset))
            : BinaryPrimitives.ReadUInt32BigEndian(tiff.AsSpan(offset));
}
