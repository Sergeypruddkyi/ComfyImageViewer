using System.IO;

namespace ComfyImageViewer.Core.Tests.Metadata;

/// <summary>Собирает минимальный TIFF (II, little-endian) с EXIF-тегами для unit-тестов ридера.</summary>
internal static class ExifTestFactory
{
    public static byte[] CreateTiff()
    {
        // IFD0: Make(271)="CivCam", Model(272)="X100", Software(305)="UnitTest",
        // ExifIFD pointer(34665) -> DateTimeOriginal(36867)="2026:01:02 03:04:05".
        // Структура: header(8) + IFD0 + value area + ExifIFD + value area.
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write((byte)0x49); w.Write((byte)0x49); // "II"
            w.Write((ushort)42);
            w.Write((uint)8); // IFD0 offset

            long ifd0Pos = ms.Position;
            w.Write((ushort)4); // 4 записи

            uint valueAreaOffset = (uint)(ifd0Pos + 2 + 4 * 12 + 4); // после IFD0 + next-IFD
            uint valuesPos = valueAreaOffset;

            // Make: ASCII, 7 байт (>4 — в value area)
            w.Write((ushort)0x010F); w.Write((ushort)2); w.Write((uint)7); w.Write(valuesPos);
            long makePos = valuesPos; valuesPos += 7;

            // Model: ASCII, 5 байт
            w.Write((ushort)0x0110); w.Write((ushort)2); w.Write((uint)5); w.Write(valuesPos);
            long modelPos = valuesPos; valuesPos += 5;

            // Software: ASCII, 9 байт
            w.Write((ushort)0x0131); w.Write((ushort)2); w.Write((uint)9); w.Write(valuesPos);
            long softwarePos = valuesPos; valuesPos += 9;

            // Exif IFD pointer: LONG, 1 значение (inline)
            w.Write((ushort)0x8769); w.Write((ushort)4); w.Write((uint)1);
            uint exifIfdOffset = valuesPos;
            w.Write(exifIfdOffset);

            w.Write((uint)0); // next IFD = 0

            // Value area IFD0
            w.Write("CivCam\0"u8.ToArray());
            w.Write("X100\0"u8.ToArray());
            w.Write("UnitTest\0"u8.ToArray());

            // ExifIFD: 1 запись
            AssertEquals(valuesPos, exifIfdOffset);
            w.Write((ushort)1); // 1 запись: DateTimeOriginal
            w.Write((ushort)0x9003); w.Write((ushort)2); w.Write((uint)20);
            long dtoValuePos = valuesPos + 2 + 12 + 4;
            w.Write((uint)dtoValuePos);
            w.Write((uint)0); // next IFD = 0
            w.Write("2026:01:02 03:04:05\0"u8.ToArray());
        }

        return ms.ToArray();
    }

    private static void AssertEquals(long actual, long expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"ExifIFD offset mismatch: {actual} != {expected}");
        }
    }

    /// <summary>
    /// TIFF с GPS IFD (0x8825) для тестов ExifGps/ImageFileInfoReader. lat/lon — DMS
    /// (deg, min, sec) рациональными числами; refs — "N"/"S"/"E"/"W" (null — запись-REF отсутствует).
    /// includeGpsPointer=false — IFD0 без 0x8825; emptyGpsIfd=true — GPS IFD с count=0
    /// (заглушка privacy-stripping из реальных Foto_Test-файлов); withDateTimeOriginal —
    /// параллельно добавляется ExifIFD с 0x9003 (проверка, что GPS не мешает дате).
    /// Параметры собираются как в реальных файлах; для «грязных» тестов можно передать
    /// нулевой знаменатель, REF-"X" или deg>90 — парсер обязан вернуть null.
    /// </summary>
    public static byte[] CreateTiffWithGps(
        ExifRational[]? lat,
        ExifRational[]? lon,
        string? latRef = "N",
        string? lonRef = "E",
        bool includeGpsPointer = true,
        bool emptyGpsIfd = false,
        bool withDateTimeOriginal = false,
        bool bigEndian = false)
    {
        if (lat is not (null or { Length: 3 }))
        {
            throw new ArgumentException("lat: ровно 3 DMS-рационала", nameof(lat));
        }

        if (lon is not (null or { Length: 3 }))
        {
            throw new ArgumentException("lon: ровно 3 DMS-рационала", nameof(lon));
        }

        if ((latRef is not (null or { Length: 1 })) || (lonRef is not (null or { Length: 1 })))
        {
            throw new ArgumentException("REF должен быть 1 символом или null", nameof(latRef));
        }

        // --- раскладка ---
        var ifd0Tags = new List<ushort>();
        if (includeGpsPointer)
        {
            ifd0Tags.Add(0x8825);
        }

        if (withDateTimeOriginal)
        {
            ifd0Tags.Add(0x8769);
        }

        var gpsTags = new List<ushort>();
        if (includeGpsPointer && !emptyGpsIfd)
        {
            if (latRef is not null) gpsTags.Add(0x0001);
            if (lat is not null) gpsTags.Add(0x0002);
            if (lonRef is not null) gpsTags.Add(0x0003);
            if (lon is not null) gpsTags.Add(0x0004);
        }

        uint ifd0Pos = 8;
        uint afterIfd0 = ifd0Pos + 2 + 12u * (uint)ifd0Tags.Count + 4;
        uint gpsIfdPos = includeGpsPointer ? afterIfd0 : 0;
        uint gpsValueArea = includeGpsPointer ? gpsIfdPos + 2 + 12u * (uint)gpsTags.Count + 4 : afterIfd0;
        uint valueCursor = gpsValueArea;
        uint latValuePos = 0, lonValuePos = 0;
        if (gpsTags.Contains((ushort)0x0002))
        {
            latValuePos = valueCursor;
            valueCursor += 24;
        }

        if (gpsTags.Contains((ushort)0x0004))
        {
            lonValuePos = valueCursor;
            valueCursor += 24;
        }

        uint afterGpsValues = valueCursor;
        uint exifIfdPos = withDateTimeOriginal ? afterGpsValues : 0;
        uint dateValuePos = exifIfdPos + 2 + 12 + 4;

        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write(bigEndian ? (byte)0x4D : (byte)0x49);
            w.Write(bigEndian ? (byte)0x4D : (byte)0x49);
            W16(w, 42, bigEndian);
            W32(w, 8, bigEndian); // IFD0

            W16(w, (ushort)ifd0Tags.Count, bigEndian);
            foreach (var tag in ifd0Tags)
            {
                W16(w, tag, bigEndian);
                W16(w, 4, bigEndian); // LONG
                W32(w, 1, bigEndian);
                W32(w, tag == 0x8825 ? gpsIfdPos : exifIfdPos, bigEndian);
            }

            W32(w, 0, bigEndian); // next IFD

            if (includeGpsPointer)
            {
                AssertEquals(ms.Position, gpsIfdPos);
                W16(w, (ushort)gpsTags.Count, bigEndian);
                foreach (var tag in gpsTags)
                {
                    W16(w, tag, bigEndian);
                    switch (tag)
                    {
                        case 0x0001:
                        case 0x0003:
                            W16(w, 2, bigEndian); // ASCII
                            W32(w, 2, bigEndian); // "N\0"
                            var ref0 = (tag == 0x0001 ? latRef : lonRef)![0];
                            w.Write((byte)ref0);
                            w.Write((byte)0);
                            w.Write((byte)0);
                            w.Write((byte)0);
                            break;
                        case 0x0002:
                        case 0x0004:
                            W16(w, 5, bigEndian); // RATIONAL ×3
                            W32(w, 3, bigEndian);
                            W32(w, tag == 0x0002 ? latValuePos : lonValuePos, bigEndian);
                            break;
                    }
                }

                W32(w, 0, bigEndian); // next IFD

                // value area GPS: сначала lat (24 B), затем lon (24 B) — порядок смещений выше
                if (gpsTags.Contains((ushort)0x0002))
                {
                    AssertEquals(ms.Position, latValuePos);
                    WriteRationals(w, lat!, bigEndian);
                }

                if (gpsTags.Contains((ushort)0x0004))
                {
                    AssertEquals(ms.Position, lonValuePos);
                    WriteRationals(w, lon!, bigEndian);
                }
            }

            if (withDateTimeOriginal)
            {
                AssertEquals(ms.Position, exifIfdPos);
                W16(w, 1, bigEndian); // ExifIFD: 1 запись — DateTimeOriginal
                W16(w, 0x9003, bigEndian);
                W16(w, 2, bigEndian);
                W32(w, 20, bigEndian);
                W32(w, dateValuePos, bigEndian);
                W32(w, 0, bigEndian);
                AssertEquals(ms.Position, dateValuePos);
                w.Write("2026:01:02 03:04:05\0"u8.ToArray());
            }
        }

        return ms.ToArray();
    }

    private static void WriteRationals(BinaryWriter w, ExifRational[] dms, bool bigEndian)
    {
        foreach (var r in dms)
        {
            W32(w, r.Numerator, bigEndian);
            W32(w, r.Denominator, bigEndian);
        }
    }

    private static void W16(BinaryWriter w, ushort value, bool bigEndian)
    {
        if (bigEndian)
        {
            w.Write((byte)(value >> 8));
            w.Write((byte)value);
        }
        else
        {
            w.Write(value);
        }
    }

    private static void W32(BinaryWriter w, uint value, bool bigEndian)
    {
        if (bigEndian)
        {
            w.Write((byte)(value >> 24));
            w.Write((byte)(value >> 16));
            w.Write((byte)(value >> 8));
            w.Write((byte)value);
        }
        else
        {
            w.Write(value);
        }
    }
}

/// <summary>RATIONAL (type 5) для синтетических GPS-DMS в тестах.</summary>
internal readonly record struct ExifRational(uint Numerator, uint Denominator);
