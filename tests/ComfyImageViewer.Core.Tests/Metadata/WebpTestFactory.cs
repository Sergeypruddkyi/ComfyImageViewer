using System.Buffers.Binary;

namespace ComfyImageViewer.Core.Tests.Metadata;

/// <summary>Собирает валидный WebP (RIFF/WEBP) с EXIF-чанком на базе 1×1 lossless VP8L.</summary>
internal static class WebpTestFactory
{
    private const string Minimal1x1Base64 = "UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA==";

    public static byte[] CreateWebPWithExif(byte[] tiff)
    {
        var baseWebp = Convert.FromBase64String(Minimal1x1Base64);

        // Дописываем EXIF-чанк в конец валидного WebP и обновляем RIFF size
        // (pad до чётного размера чанка — по спецификации RIFF).
        int oldRiffSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(baseWebp.AsSpan(4));
        int exifChunkLen = tiff.Length + (tiff.Length & 1);

        var result = new byte[baseWebp.Length + 8 + exifChunkLen];
        Array.Copy(baseWebp, result, baseWebp.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(oldRiffSize + 8 + exifChunkLen));

        int pos = baseWebp.Length;
        "EXIF"u8.CopyTo(result.AsSpan(pos));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(pos + 4), (uint)tiff.Length);
        tiff.CopyTo(result.AsSpan(pos + 8));
        return result;
    }
}
