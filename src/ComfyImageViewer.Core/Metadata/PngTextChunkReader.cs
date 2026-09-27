using System.Buffers.Binary;
using System.IO;
using System.Text;
using ComfyImageViewer.Core.Interfaces;

namespace ComfyImageViewer.Core.Metadata;

public sealed class PngTextChunkReader : IPngMetadataReader
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private const uint TypeText = 0x74455874u; // "tEXt"
    private const uint TypeIend = 0x49454E44u; // "IEND"

    public IReadOnlyDictionary<string, string> ReadTextChunks(string pngPath)
    {
        using FileStream stream = File.OpenRead(pngPath); // FileShare.Read

        Span<byte> signature = stackalloc byte[8];
        ReadExactly(stream, signature);
        if (!signature.SequenceEqual(PngSignature))
            throw new InvalidDataException("File does not have a valid PNG signature.");

        var chunks = new Dictionary<string, string>(StringComparer.Ordinal);
        Span<byte> header = stackalloc byte[8];
        Span<byte> crc = stackalloc byte[4];

        while (true)
        {
            ReadExactly(stream, header);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
            uint type = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);

            long remaining = stream.Length - stream.Position;
            if (length > (ulong)remaining)
                throw new InvalidDataException("PNG chunk length exceeds remaining file size.");

            byte[] data = new byte[length];
            ReadExactly(stream, data);

            ReadExactly(stream, crc);

            if (type == TypeText && ExtractTextChunk(data) is { } entry)
                chunks[entry.Key] = entry.Value;

            if (type == TypeIend)
                return chunks;
        }
    }

    private static KeyValuePair<string, string>? ExtractTextChunk(byte[] data)
    {
        int separator = Array.IndexOf(data, (byte)0);
        if (separator < 0)
            return null;

        string keyword = Encoding.Latin1.GetString(data, 0, separator);
        string text = Encoding.Latin1.GetString(data, separator + 1, data.Length - separator - 1);
        return new KeyValuePair<string, string>(keyword, text);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of PNG file.");
            total += read;
        }
    }
}
