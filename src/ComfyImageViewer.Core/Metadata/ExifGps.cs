using System.Buffers.Binary;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Metadata;

/// <summary>
/// Извлечение GPS-позиции из TIFF/EXIF-блока: IFD0 → указатель GPS IFD (0x8825) →
/// DMS-rational (0x0002/0x0004) + ASCII reference (0x0001/0x0003), оба порядка байт.
/// Отдельный API рядом с <see cref="ExifTiff"/> (тот читает только ASCII и для date-search):
/// контракт <c>ExifTiff.Parse</c> не меняется. Любые отсутствующие/невалидные данные → null:
/// наличие указателя 0x8825 само по себе GPS не означает (пустой GPS IFD — заглушка
/// privacy-stripping из реальных фото). Только чтение, без записей и сети.
/// </summary>
public static class ExifGps
{
    private const int TagGpsIfdPointer = 0x8825;
    private const int TagGpsLatitudeRef = 0x0001;
    private const int TagGpsLatitude = 0x0002;
    private const int TagGpsLongitudeRef = 0x0003;
    private const int TagGpsLongitude = 0x0004;
    private const int TypeAscii = 2;
    private const int TypeRational = 5;
    private const int TypeLong = 4;
    private const int MaxIfdEntries = 512;

    /// <summary>Валидная GPS-позиция или null (нет IFD / пустой IFD / битые или недопустимые значения).</summary>
    public static GpsLocationInfo? TryParse(byte[] tiff)
    {
        try
        {
            return ParseCore(tiff);
        }
        catch
        {
            // Битый EXIF — отсутствие позиции, а не ошибка.
            return null;
        }
    }

    private static GpsLocationInfo? ParseCore(byte[] tiff)
    {
        if (tiff.Length < 8)
        {
            return null;
        }

        bool littleEndian = tiff[0] == 0x49 && tiff[1] == 0x49; // "II", иначе "MM"
        if (!littleEndian && !(tiff[0] == 0x4D && tiff[1] == 0x4D))
        {
            return null;
        }

        uint gpsIfdOffset = FindGpsIfdPointer(tiff, littleEndian);
        if (gpsIfdOffset == 0)
        {
            return null;
        }

        string? latRef = null;
        string? lonRef = null;
        uint[]? lat = null; // deg, min, sec — числитель и знаменатель раздельно
        uint[]? lon = null;

        ushort count = ReadU16(tiff, (int)gpsIfdOffset, littleEndian);
        if (count == 0 || count > MaxIfdEntries)
        {
            return null; // пустой GPS IFD — заглушка, не позиция
        }

        for (ushort i = 0; i < count; i++)
        {
            long entry = gpsIfdOffset + 2 + 12L * i;
            if (entry + 12 > tiff.Length)
            {
                return null;
            }

            int tag = (int)ReadU16(tiff, (int)entry, littleEndian);
            int type = (int)ReadU16(tiff, (int)entry + 2, littleEndian);
            uint valueCount = ReadU32(tiff, (int)entry + 4, littleEndian);
            uint valueOffset = ReadU32(tiff, (int)entry + 8, littleEndian);

            switch (tag)
            {
                case TagGpsLatitudeRef:
                    latRef = ReadAscii(tiff, entry, type, valueCount, valueOffset, littleEndian);
                    break;
                case TagGpsLatitude:
                    lat = ReadRationals(tiff, type, valueCount, valueOffset, littleEndian);
                    break;
                case TagGpsLongitudeRef:
                    lonRef = ReadAscii(tiff, entry, type, valueCount, valueOffset, littleEndian);
                    break;
                case TagGpsLongitude:
                    lon = ReadRationals(tiff, type, valueCount, valueOffset, littleEndian);
                    break;
            }
        }

        if (lat is not { Length: 6 } || lon is not { Length: 6 })
        {
            return null;
        }

        double? latitude = ToDegrees(lat, latRef, maxDegrees: 90);
        double? longitude = ToDegrees(lon, lonRef, maxDegrees: 180);
        if (latitude is null || longitude is null)
        {
            return null;
        }

        return new GpsLocationInfo(latitude.Value, longitude.Value);
    }

    /// <summary>Обход IFD0 до тега 0x8825 (LONG). 0 — указателя нет или он вне буфера.</summary>
    private static uint FindGpsIfdPointer(byte[] tiff, bool littleEndian)
    {
        uint ifd0 = ReadU32(tiff, 4, littleEndian);
        if (ifd0 == 0 || ifd0 + 2 > tiff.Length)
        {
            return 0;
        }

        ushort count = ReadU16(tiff, (int)ifd0, littleEndian);
        if (count > MaxIfdEntries)
        {
            return 0;
        }

        for (ushort i = 0; i < count; i++)
        {
            long entry = ifd0 + 2 + 12L * i;
            if (entry + 12 > tiff.Length)
            {
                return 0;
            }

            int tag = (int)ReadU16(tiff, (int)entry, littleEndian);
            int type = (int)ReadU16(tiff, (int)entry + 2, littleEndian);
            if (tag == TagGpsIfdPointer && type == TypeLong)
            {
                uint offset = ReadU32(tiff, (int)entry + 8, littleEndian);
                return offset is >= 8 and < uint.MaxValue && offset + 2 <= tiff.Length ? offset : 0;
            }
        }

        return 0;
    }

    private static string? ReadAscii(byte[] tiff, long entry, int type, uint valueCount, uint valueOffset, bool littleEndian)
    {
        if (type != TypeAscii || valueCount is 0 or > 16)
        {
            return null;
        }

        int length = (int)valueCount;
        int dataStart = length <= 4 ? (int)entry + 8 : (int)valueOffset;
        if (dataStart < 0 || dataStart + length > tiff.Length)
        {
            return null;
        }

        var text = System.Text.Encoding.ASCII.GetString(tiff, dataStart, length).Trim('\0').Trim();
        return text.Length == 1 ? text.ToUpperInvariant() : null;
    }

    /// <summary>3 RATIONAL (type 5) → 6 uint32 [num,den]×3; null при невалидном типе/размере/смещении.</summary>
    private static uint[]? ReadRationals(byte[] tiff, int type, uint valueCount, uint valueOffset, bool littleEndian)
    {
        if (type != TypeRational || valueCount != 3)
        {
            return null; // 3 рациональных = 24 байта, всегда по offset
        }

        int start = (int)valueOffset;
        if (start < 0 || start + 24 > tiff.Length)
        {
            return null;
        }

        var values = new uint[6];
        for (int i = 0; i < 6; i++)
        {
            values[i] = ReadU32(tiff, start + 4 * i, littleEndian);
        }

        return values;
    }

    /// <summary>deg + min/60 + sec/3600 со знаком по reference (S/W → минус); null при любой невалидности.</summary>
    private static double? ToDegrees(uint[] dms, string? reference, double maxDegrees)
    {
        if (reference is not ("N" or "S" or "E" or "W"))
        {
            return null;
        }

        if (maxDegrees == 90 && reference is "E" or "W" || maxDegrees == 180 && reference is "N" or "S")
        {
            return null;
        }

        var (deg, min, sec) = (Component(dms, 0), Component(dms, 1), Component(dms, 2));
        if (deg is null || min is null || sec is null)
        {
            return null;
        }

        if (deg > maxDegrees || min >= 60 || sec >= 60)
        {
            return null;
        }

        double value = deg.Value + min.Value / 60 + sec.Value / 3600;
        if (reference is "S" or "W")
        {
            value = -value;
        }

        return double.IsFinite(value) && Math.Abs(value) <= maxDegrees ? value : null;
    }

    private static double? Component(uint[] dms, int index)
    {
        uint numerator = dms[2 * index];
        uint denominator = dms[2 * index + 1];
        return denominator == 0 ? null : (double)numerator / denominator;
    }

    private static ushort ReadU16(byte[] tiff, int offset, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(offset))
            : BinaryPrimitives.ReadUInt16BigEndian(tiff.AsSpan(offset));

    private static uint ReadU32(byte[] tiff, int offset, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(offset))
            : BinaryPrimitives.ReadUInt32BigEndian(tiff.AsSpan(offset));
}
