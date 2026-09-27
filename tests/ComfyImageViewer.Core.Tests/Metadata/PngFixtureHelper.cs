using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ComfyImageViewer.Core.Tests.Metadata;

internal static class PngFixtureHelper
{
    /// <summary>Создаёт минимальный валидный PNG (1x1) через WPF-энкодер.</summary>
    public static byte[] CreatePng()
    {
        byte[] pixels = { 0xFF, 0x80, 0x40, 0xFF };
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Внедряет tEXt-чанк перед IEND: [len BE]["tEXt"][keyword 0x00 text][CRC32(type+data)].</summary>
    public static byte[] InjectTextChunk(byte[] png, string keyword, string text)
    {
        byte[] type = "tEXt"u8.ToArray();
        byte[] data = Encoding.Latin1.GetBytes(keyword).Concat(new byte[] { 0x00 }).Concat(Encoding.Latin1.GetBytes(text)).ToArray();

        byte[] chunk = new byte[12 + data.Length];
        WriteUInt32BigEndian(chunk.AsSpan(0, 4), (uint)data.Length);
        type.CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4, 4), Crc32(chunk.AsSpan(4, 4 + data.Length)));

        // IEND-чанк — последние 12 байта валидного PNG.
        int insertAt = png.Length - 12;
        byte[] result = new byte[png.Length + chunk.Length];
        png.AsSpan(0, insertAt).CopyTo(result);
        chunk.CopyTo(result, insertAt);
        png.AsSpan(insertAt).CopyTo(result.AsSpan(insertAt + chunk.Length));
        return result;
    }

    public static byte[] Truncate(byte[] png, int keepBytes) => png.AsSpan(0, keepBytes).ToArray();

    private static void WriteUInt32BigEndian(Span<byte> span, uint value)
    {
        span[0] = (byte)(value >> 24);
        span[1] = (byte)(value >> 16);
        span[2] = (byte)(value >> 8);
        span[3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int bit = 0; bit < 8; bit++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
            crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
