using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Interfaces;

public interface IModelFamilySearchService
{
    // Поиск всегда рекурсивный: CurrentPath + все вложенные папки.
    IAsyncEnumerable<ImageEntry> FindAsync(string rootPath, ModelFamily family, CancellationToken ct = default);
}
