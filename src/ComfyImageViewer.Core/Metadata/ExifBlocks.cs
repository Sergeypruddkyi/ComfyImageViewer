using System.IO;

namespace ComfyImageViewer.Core.Metadata;

/// <summary>
/// Поиск EXIF-блоков в контейнерах JPEG (APP1-"Exif") и WebP (RIFF-чанк "EXIF").
/// Общие для <see cref="ImageFileInfoReader"/> (отображение metadata) и поиска по дате.
/// Только чтение байтов, никаких записей в файл.
/// </summary>
internal static class ExifBlocks
{
    /// <summary>Ищет APP1-"Exif"-сегмент в JPEG; возвращает TIFF-блок без префикса "Exif\0\0".</summary>
    internal static byte[]? FindJpegExif(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return FindJpegExif(ms.ToArray());
    }

    /// <summary>
    /// Ищет APP1-"Exif"-сегмент в JPEG-байтах. Буфер может быть усечённым (поиск читает
    /// только начало файла): сегменты за пределами буфера считаются отсутствующими.
    /// </summary>
    internal static byte[]? FindJpegExif(byte[] data)
    {
        if (data.Length < 2 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return null;
        }

        int pos = 2;
        while (pos + 4 <= data.Length)
        {
            if (data[pos] != 0xFF)
            {
                return null;
            }

            int marker = data[pos + 1];
            if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7)
            {
                pos += 2;
                continue;
            }

            if (marker is 0xDA or 0xD9)
            {
                return null; // SOS/EOI — текстовые сегменты закончились
            }

            int segLen = (data[pos + 2] << 8) | data[pos + 3];
            if (segLen < 2 || pos + 2 + segLen > data.Length)
            {
                return null;
            }

            if (marker == 0xE1 && segLen > 10 &&
                data[pos + 4] == (byte)'E' && data[pos + 5] == (byte)'x' &&
                data[pos + 6] == (byte)'i' && data[pos + 7] == (byte)'f' &&
                data[pos + 8] == 0 && data[pos + 9] == 0)
            {
                var tiff = new byte[segLen - 2 - 6];
                Array.Copy(data, pos + 10, tiff, 0, tiff.Length);
                return tiff;
            }

            pos += 2 + segLen;
        }

        return null;
    }

    /// <summary>Ищет EXIF-чанк в RIFF-контейнере WebP.</summary>
    internal static byte[]? FindWebPExif(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return FindWebPExif(ms.ToArray());
    }

    /// <summary>Ищет EXIF-чанк в RIFF-байтах WebP (буфер может быть усечённым).</summary>
    internal static byte[]? FindWebPExif(byte[] data)
    {
        if (data.Length < 12)
        {
            return null;
        }

        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            int len = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            if (len < 0 || pos + 8 + len > data.Length)
            {
                return null;
            }

            if (data[pos] == (byte)'E' && data[pos + 1] == (byte)'X' && data[pos + 2] == (byte)'I' && data[pos + 3] == (byte)'F')
            {
                var tiff = new byte[len];
                Array.Copy(data, pos + 8, tiff, 0, len);
                return tiff;
            }

            pos += 8 + len + (len & 1);
        }

        return null;
    }
}
