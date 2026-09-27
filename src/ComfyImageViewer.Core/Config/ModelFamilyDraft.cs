namespace ComfyImageViewer.Core.Config;

public static class ModelFamilyDraft
{
    private static readonly char[] PatternSeparators = [',', ';'];

    public static (string Name, IReadOnlyList<string> Patterns) Normalize(string? name, string? patternsRaw)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
        {
            throw new ArgumentException("Название семейства не может быть пустым.");
        }

        return (trimmedName, ParsePatterns(patternsRaw));
    }

    /// <summary>
    /// Разбор строки «Поисковые шаблоны» в список: trim, пропуск пустых, удаление
    /// только ТОЧНЫХ дублей (Ordinal). Сопоставление регистронезависимое было бы
    /// потерей введённого: «sd 1.5» и «SD 1.5» — разные начертания одного имени,
    /// пользователь вводит их осознанно, и в JSON должны попасть оба. Поиск
    /// регистронезависимый (ModelFamilySearchService), поэтому на нахождение файлов
    /// это не влияет.
    /// </summary>
    public static IReadOnlyList<string> ParsePatterns(string? patternsRaw)
    {
        if (string.IsNullOrWhiteSpace(patternsRaw))
        {
            return [];
        }

        var patterns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in patternsRaw.Split(PatternSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var pattern = part.Trim();
            if (pattern.Length > 0 && seen.Add(pattern))
            {
                patterns.Add(pattern);
            }
        }

        return patterns;
    }

    public static string FormatPatterns(IEnumerable<string> patterns) => string.Join(", ", patterns);

    /// <summary>
    /// Название нового семейства само становится первым поисковым шаблоном: иначе
    /// только что созданное семейство ничего не находит — пользователь мог оставить
    /// поле «Поисковые шаблоны» пустым или вписать не то имя модели.
    /// Нормализация та же, что у patterns: trim и нижний регистр (сопоставление в
    /// ModelFamilySearchService регистронезависимое, регистр на поиск не влияет).
    /// Если название уже есть среди patterns (OrdinalIgnoreCase) — дубликат не
    /// добавляется; patterns пользователя не меняются и не переупорядочиваются.
    /// </summary>
    public static IReadOnlyList<string> WithNamePattern(string? name, IReadOnlyList<string> patterns)
    {
        var namePattern = (name?.Trim() ?? string.Empty).ToLowerInvariant();
        if (namePattern.Length == 0 || patterns.Contains(namePattern, StringComparer.OrdinalIgnoreCase))
        {
            return patterns;
        }

        return [namePattern, .. patterns];
    }
}
