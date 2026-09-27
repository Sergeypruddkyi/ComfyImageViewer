using System.Globalization;
using System.IO;
using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Metadata;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Scanning;

/// <summary>
/// Рекурсивный поиск изображений по дате. Основная дата файла:
/// EXIF DateTimeOriginal (tag 0x9003) — приоритет; если её нет или она не читается —
/// дата изменения файла. ComfyUI metadata НЕ участвует: обычная фотография — обычная дата.
/// Поддерживаются JPEG/WebP (EXIF читается из заголовка без полного декодирования) и PNG
/// (EXIF-дат в тестовом множестве нет — работает fallback на дату файла).
/// </summary>
public sealed class DateSearchService : IDateSearchService
{
    // EXIF APP1/"EXIF"-чанк всегда в начале файла (до SOS); чтение всего файла ради даты
    // было бы расточительным на больших архивах. 1 МБ покрывает заголовки реальных фото
    // (включая вложенные миниатюры); сегменты дальше этого окна считаются отсутствующими.
    private const int ExifHeaderWindowBytes = 1024 * 1024;

    public async IAsyncEnumerable<ImageEntry> FindAsync(
        string rootPath,
        DateOnly from,
        DateOnly to,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (from > to)
        {
            (from, to) = (to, from);
        }

        // Та же стратегия обхода, что и у ModelFamilySearchService: рекурсия от root,
        // недоступные каталоги игнорируются, reparse point пропускается (без циклов),
        // результат — единый плоский список изображений (папки не возвращаются).
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var path in Directory.EnumerateFiles(ScanPaths.EnsureLongPath(rootPath), "*", options))
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;

            if (!ScanPaths.SupportedImageExtensions.Contains(Path.GetExtension(fullPath)))
            {
                continue;
            }

            var entry = ScanPaths.TryCreateEntry(fullPath);
            if (entry is null)
            {
                continue;
            }

            // Любой per-file сбой (исчез/недоступен/битый заголовок) — fallback на дату
            // файла; сам поиск продолжается (политика как в ModelFamilySearchService).
            var date = GetImageDate(fullPath, entry);
            if (date >= from && date <= to)
            {
                yield return entry;
            }
        }
    }

    internal static DateOnly GetImageDate(string fullPath, ImageEntry entry)
    {
        var exifDate = TryReadExifOriginalDate(fullPath);
        return exifDate ?? DateOnly.FromDateTime(entry.ModifiedUtc.ToLocalTime());
    }

    private static DateOnly? TryReadExifOriginalDate(string fullPath)
    {
        try
        {
            var extension = Path.GetExtension(fullPath);
            byte[]? tiff = null;

            if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                tiff = ExifBlocks.FindJpegExif(ReadHeader(fullPath));
            }
            else if (extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
            {
                tiff = ExifBlocks.FindWebPExif(ReadHeader(fullPath));
            }

            if (tiff is not { Length: > 8 })
            {
                return null;
            }

            var tags = ExifTiff.Parse(tiff);
            return TryParseExifDate(tags.GetValueOrDefault(ExifTiff.TagDateTimeOriginal));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[] ReadHeader(string fullPath)
    {
        using var stream = File.OpenRead(ScanPaths.EnsureLongPath(fullPath));
        var buffer = new byte[ExifHeaderWindowBytes];
        var read = stream.Read(buffer);
        if (read == buffer.Length)
        {
            return buffer;
        }

        Array.Resize(ref buffer, read);
        return buffer;
    }

    /// <summary>EXIF ASCII "2008:03:15 17:47:49"; мусор/нулевой год → null (fallback).</summary>
    internal static DateOnly? TryParseExifDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var datePart = value.Split([' ', 'T'])[0];
        return DateOnly.TryParseExact(datePart, "yyyy:MM:dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
