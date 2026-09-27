using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Interfaces;

/// <summary>
/// Читает размеры изображения и дополнительную (не-ComfyUI) metadata
/// из содержимого файла: EXIF для JPEG/WebP. Вызывается из фонового потока.
/// </summary>
public interface IImageFileInfoReader
{
    /// <summary>Возвращает null, если файл не декодируется или недоступен.</summary>
    ImageFileInfo? Read(string filePath);
}
