using System.IO;
using System.Text.Json;
using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Config;

public sealed class ModelFamiliesProvider : IModelFamiliesProvider
{
    public static string DefaultPath { get; } = Path.Combine(AppContext.BaseDirectory, "ModelFamilies.json");

    public IReadOnlyList<ModelFamily> Families { get; }

    /// <param name="basePathOverride">Полный путь к ModelFamilies.json (для тестов); null — файл рядом с EXE.</param>
    public ModelFamiliesProvider(string? basePathOverride = null)
    {
        var path = basePathOverride ?? DefaultPath;
        Families = Load(path);
    }

    // Внешний редактируемый конфиг (hot-reload не нужен): ЛЮБАЯ ошибка — нет файла,
    // битый JSON — даёт пустой список: приложение работает, фильтр остаётся только «Модель».
    private static IReadOnlyList<ModelFamily> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var families = new List<ModelFamily>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("name", out JsonElement nameElement) ||
                    nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var name = nameElement.GetString()?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                families.Add(new ModelFamily(name, LoadPatterns(element)));
            }

            return families;
        }
        catch (Exception)
        {
            return [];
        }
    }

    // Санитизация: trim, пропуск пустых, dedupe (OrdinalIgnoreCase); порядок сохраняется.
    private static IReadOnlyList<string> LoadPatterns(JsonElement family)
    {
        var patterns = new List<string>();
        if (!family.TryGetProperty("patterns", out JsonElement patternsElement) ||
            patternsElement.ValueKind != JsonValueKind.Array)
        {
            return patterns;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var patternElement in patternsElement.EnumerateArray())
        {
            if (patternElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var pattern = patternElement.GetString()?.Trim();
            if (!string.IsNullOrEmpty(pattern) && seen.Add(pattern))
            {
                patterns.Add(pattern);
            }
        }

        return patterns;
    }
}
