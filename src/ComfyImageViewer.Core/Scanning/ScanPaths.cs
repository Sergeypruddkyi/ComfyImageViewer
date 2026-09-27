using System.IO;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Scanning;

/// <summary>Общие примитивы перечисления изображений (ArchiveScanner и ModelFamilySearchService).</summary>
internal static class ScanPaths
{
    private const int LongPathThreshold = 240;

    // Поддерживаемые форматы изображений: перечисление по расширению, без декодирования.
    internal static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
    };

    /// <summary>Префикс \\?\ для длинных путей (&gt; 240 символов).</summary>
    internal static string EnsureLongPath(string path) =>
        path.Length > LongPathThreshold && !path.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? @"\\?\" + path
            : path;

    /// <summary>Запись из FileInfo; файл исчез, недоступен или reparse point — null (не падаем).</summary>
    internal static ImageEntry? TryCreateEntry(string fullPath)
    {
        try
        {
            var fi = new FileInfo(EnsureLongPath(fullPath));
            if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            return new ImageEntry(fullPath, Path.GetFileName(fullPath), fi.Length, fi.CreationTimeUtc, fi.LastWriteTimeUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
