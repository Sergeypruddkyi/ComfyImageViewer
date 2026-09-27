using System.IO;
using System.Text;
using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Scanning;

public sealed class ModelFamilySearchService : IModelFamilySearchService
{
    private readonly IPngMetadataReader _pngReader;
    private readonly IComfyMetadataParser _parser;

    public ModelFamilySearchService(IPngMetadataReader pngReader, IComfyMetadataParser parser)
    {
        _pngReader = pngReader;
        _parser = parser;
    }

    public async IAsyncEnumerable<ImageEntry> FindAsync(
        string rootPath,
        ModelFamily family,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(family);

        // Шаблоны сравниваются без разделителей и в нижнем регистре: имя семейства вводит
        // человек («sd 1.5», «Qwen 2.1»), а loader хранит значение модели файловым
        // написанием («SD15\realisticVisionV60B1_v51HyperVAE.safetensors»,
        // «Qwen2_1\qwen_image_2.1_int8_convrot.safetensors»). «Как есть» такое имя внутри
        // значений модели не встречается ни разу (проверено на реальном корпусе PNG),
        // поэтому поиск был пустым. Нормализация идёт один раз на весь поиск.
        var patterns = NormalizePatterns(family.Patterns);
        if (patterns.Length == 0)
        {
            yield break;
        }

        // Поиск всегда рекурсивный: CurrentPath + все вложенные папки; результат —
        // единый плоский список изображений (папки не возвращаются). Reparse point
        // пропускается (циклы по junction невозможны — skip и в каталогах, и в файлах),
        // недоступные каталоги игнорируются.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var path in Directory.EnumerateFiles(ScanPaths.EnsureLongPath(rootPath), "*", options))
        {
            ct.ThrowIfCancellationRequested();

            // ImageEntry.FullPath — реальный путь файла, без служебного префикса \\?\.
            var fullPath = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;

            // Расширения как в ArchiveScanner; ComfyUI metadata pipeline читает только PNG tEXt.
            if (!ScanPaths.SupportedImageExtensions.Contains(Path.GetExtension(fullPath)) ||
                !string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entry = ScanPaths.TryCreateEntry(fullPath);
            if (entry is not null && MatchesFamily(fullPath, patterns))
            {
                yield return entry;
            }
        }
    }

    // Сопоставление только через patterns выбранного семейства по ModelNames из prompt-JSON.
    // Сравниваются нормализованные строки (см. Normalize): «sd 1.5» ↔ «SD15\realisticVision...»,
    // «Qwen 2.1» ↔ «Qwen2_1\qwen_image_2.1_int8_convrot.safetensors».
    // Любой per-file сбой (исчез/недоступен/битый PNG) → файл пропускается, поиск продолжается.
    private bool MatchesFamily(string fullPath, string[] patterns)
    {
        try
        {
            var chunks = _pngReader.ReadTextChunks(ScanPaths.EnsureLongPath(fullPath));
            var metadata = _parser.Parse(chunks);
            if (metadata.Status != MetadataStatus.Present)
            {
                return false;
            }

            foreach (var modelName in metadata.ModelNames)
            {
                var normalizedName = Normalize(modelName);
                foreach (var pattern in patterns)
                {
                    if (normalizedName.Contains(pattern, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }
    }

    // Нормализация «человеческого» имени/шаблона и файлового значения модели для сравнения:
    // остаются только буквы и цифры в нижнем регистре, разделители (пробел, «_», «-», «.», «\»)
    // отбрасываются. Сравнение — подстрока нормализованного значения: «sd 1.5» → «sd15»,
    // «SD15\realisticVision...» → «sd15realisticvision...», «Qwen 2.1» → «qwen21»,
    // «Qwen2_1\qwen_image_2.1_...» → «qwen21qwenimage21...».
    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    // Шаблоны, состоящие только из разделителей, нормализуются в пустую строку и отбрасываются:
    // иначе пустой pattern совпал бы с любым файлом.
    private static string[] NormalizePatterns(IReadOnlyList<string> patterns)
    {
        var normalized = new List<string>(patterns.Count);
        foreach (var pattern in patterns)
        {
            var value = Normalize(pattern);
            if (value.Length > 0)
            {
                normalized.Add(value);
            }
        }

        return [.. normalized];
    }
}
