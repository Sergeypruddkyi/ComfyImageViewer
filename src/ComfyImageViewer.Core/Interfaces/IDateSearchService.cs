using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Interfaces;

public interface IDateSearchService
{
    // Поиск всегда рекурсивный: rootPath + все вложенные папки; даты включительно [from..to].
    IAsyncEnumerable<ImageEntry> FindAsync(string rootPath, DateOnly from, DateOnly to, CancellationToken ct = default);
}
