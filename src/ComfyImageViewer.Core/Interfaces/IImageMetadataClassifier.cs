using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Interfaces;

public readonly record struct ClassifiedImageMetadata(
    bool IsComfyMetadata,
    ImageMetadata? ComfyMetadata,
    ImageFileInfo? FileInfo,
    IReadOnlyList<MetadataField> FileFields);

/// <summary>
/// Разделяет metadata изображения на два пути по СОДЕРЖИМОМУ файла:
/// ComfyUI (PNG tEXt-ключи prompt/workflow) либо обычное изображение.
/// Не является признаком формата: обычный PNG и A1111 "parameters" уходят в ordinary path.
/// </summary>
public interface IImageMetadataClassifier
{
    ClassifiedImageMetadata Classify(string filePath);
}
