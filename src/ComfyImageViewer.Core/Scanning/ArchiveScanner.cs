using System.IO;
using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Scanning;

public sealed class ArchiveScanner : IArchiveScanner
{
    public async IAsyncEnumerable<FolderNode> ListSubfoldersAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        foreach (var name in SafeNames(() => Directory.EnumerateDirectories(path)))
        {
            ct.ThrowIfCancellationRequested();

            var full = Path.Combine(path, name);
            if (ShouldSkip(full))
            {
                continue;
            }

            yield return new FolderNode(full, Path.GetFileName(full));
        }
    }

    public async IAsyncEnumerable<ImageEntry> ListImagesAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        foreach (var name in SafeNames(() => Directory.EnumerateFiles(path)))
        {
            ct.ThrowIfCancellationRequested();

            if (!ScanPaths.SupportedImageExtensions.Contains(Path.GetExtension(name)))
            {
                continue;
            }

            var full = Path.Combine(path, name);
            var entry = ScanPaths.TryCreateEntry(full);
            if (entry is not null)
            {
                yield return entry;
            }
        }
    }

    /// <summary>
    /// Перечисляет имена, изолируя ошибки самого перечисления (недоступный каталог и т.п.).
    /// </summary>
    private static List<string> SafeNames(Func<IEnumerable<string>> enumerate)
    {
        try
        {
            return enumerate().ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Каталог недоступен — считаем его пустым, не падаем.
            return [];
        }
    }

    /// <summary>Возвращает true, если узел должен быть пропущен (reparse point либо недоступен).</summary>
    private static bool ShouldSkip(string fullPath)
    {
        try
        {
            var attributes = File.GetAttributes(ScanPaths.EnsureLongPath(fullPath));
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}

