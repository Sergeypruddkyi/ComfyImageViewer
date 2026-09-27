using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Metadata;

public sealed class ImageMetadataClassifier : IImageMetadataClassifier
{
    private const int MaxTextFields = 8;
    private const int MaxFieldValueLength = 2000;

    private readonly IPngMetadataReader _pngReader;
    private readonly IComfyMetadataParser _parser;
    private readonly IImageFileInfoReader _fileInfoReader;

    public ImageMetadataClassifier(
        IPngMetadataReader pngReader,
        IComfyMetadataParser parser,
        IImageFileInfoReader fileInfoReader)
    {
        _pngReader = pngReader;
        _parser = parser;
        _fileInfoReader = fileInfoReader;
    }

    public ClassifiedImageMetadata Classify(string filePath)
    {
        try
        {
            var chunks = _pngReader.ReadTextChunks(filePath);
            var meta = _parser.Parse(chunks);

            if (meta.Status == MetadataStatus.Present)
            {
                // ComfyUI metadata: tEXt-ключи prompt/workflow, распознанные существующим parser'ом.
                // A1111 "parameters" и AIGC сюда не попадают: parser их не считает ComfyUI.
                return new ClassifiedImageMetadata(true, meta, null, []);
            }

            // Обычный PNG: tEXt-ключи кроме ComfyUI — как дополнительные поля (raw key/value).
            var fields = chunks
                .Where(kv => !kv.Key.Equals("prompt", StringComparison.OrdinalIgnoreCase) &&
                             !kv.Key.Equals("workflow", StringComparison.OrdinalIgnoreCase))
                .Take(MaxTextFields)
                .Select(kv => new MetadataField(kv.Key, Truncate(kv.Value)))
                .ToList();

            var info = _fileInfoReader.Read(filePath);
            return new ClassifiedImageMetadata(false, null, info, fields);
        }
        catch
        {
            // Не-PNG (JPEG/WebP) или нечитаемый PNG: ordinary path, поля — из EXIF-ридера.
            var info = _fileInfoReader.Read(filePath);
            var fields = info is null ? [] : info.Fields;
            return new ClassifiedImageMetadata(false, null, info, fields);
        }
    }

    private static string Truncate(string value)
    {
        value = value.Trim();
        return value.Length <= MaxFieldValueLength ? value : value[..MaxFieldValueLength] + "…";
    }
}
