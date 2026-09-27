using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Config;

public sealed class ModelFamiliesStore : IModelFamiliesStore
{
    private readonly string _path;
    private ModelFamiliesProvider _provider;

    /// <param name="pathOverride">Полный путь к ModelFamilies.json (для тестов); null — файл рядом с EXE.</param>
    public ModelFamiliesStore(string? pathOverride = null)
    {
        _path = pathOverride ?? ModelFamiliesProvider.DefaultPath;
        _provider = new ModelFamiliesProvider(_path);
    }

    public IReadOnlyList<ModelFamily> Families => _provider.Families;

    public void Add(string name, string patternsRaw)
    {
        var (normalizedName, patterns) = ModelFamilyDraft.Normalize(name, patternsRaw);
        EnsureUnique(normalizedName, excludingName: null);

        // Название нового семейства автоматически становится поисковым шаблоном:
        // созданное семейство иначе ничего не находит (patterns пустые или не совпадают).
        var patternsWithName = ModelFamilyDraft.WithNamePattern(normalizedName, patterns);
        Mutate(families => families.Add(CreateEntry(normalizedName, patternsWithName)));
    }

    public void Update(string originalName, string name, string patternsRaw)
    {
        var original = originalName?.Trim();
        if (string.IsNullOrEmpty(original))
        {
            throw new ArgumentException("Семейство не выбрано.");
        }

        var (normalizedName, patterns) = ModelFamilyDraft.Normalize(name, patternsRaw);
        EnsureUnique(normalizedName, original);

        Mutate(families =>
        {
            var index = IndexOf(families, original);
            if (index < 0)
            {
                throw new InvalidOperationException($"Семейство «{original}» не найдено в ModelFamilies.json.");
            }

            families[index] = CreateEntry(normalizedName, patterns);
        });
    }

    public void Remove(string name)
    {
        var target = name?.Trim();
        if (string.IsNullOrEmpty(target))
        {
            throw new ArgumentException("Семейство не выбрано.");
        }

        Mutate(families =>
        {
            var index = IndexOf(families, target);
            if (index < 0)
            {
                throw new InvalidOperationException($"Семейство «{target}» не найдено в ModelFamilies.json.");
            }

            families.RemoveAt(index);
        });
    }

    private void EnsureUnique(string name, string? excludingName)
    {
        foreach (var family in Families)
        {
            if (!string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (excludingName is not null &&
                string.Equals(family.Name, excludingName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            throw new InvalidOperationException($"Семейство «{name}» уже существует.");
        }
    }

    private static JsonObject CreateEntry(string name, IReadOnlyList<string> patterns)
    {
        var patternNodes = patterns.Select(pattern => (JsonNode?)JsonValue.Create(pattern)).ToArray();
        return new JsonObject
        {
            ["name"] = name,
            ["patterns"] = new JsonArray(patternNodes),
        };
    }

    private static int IndexOf(JsonArray families, string name)
    {
        for (var i = 0; i < families.Count; i++)
        {
            if (families[i] is JsonObject entry &&
                entry["name"] is JsonValue nameValue &&
                nameValue.TryGetValue<string>(out var entryName) &&
                entryName is not null &&
                string.Equals(entryName.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void Mutate(Action<JsonArray> mutation)
    {
        var families = ReadForEdit();
        mutation(families);
        Save(families);
        _provider = new ModelFamiliesProvider(_path);
    }

    private JsonArray ReadForEdit()
    {
        if (!File.Exists(_path))
        {
            return new JsonArray();
        }

        string text;
        try
        {
            text = File.ReadAllText(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Не удалось прочитать ModelFamilies.json: {e.Message}", e);
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException(
                "ModelFamilies.json не является валидным JSON — изменения не сохранены.", e);
        }

        if (node is not JsonArray families)
        {
            throw new InvalidOperationException(
                "ModelFamilies.json: ожидается массив семейств — изменения не сохранены.");
        }

        return families;
    }

    private void Save(JsonArray families)
    {
        var text = Format(families);
        var tempPath = _path + ".tmp";

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(tempPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Не удалось сохранить ModelFamilies.json: {e.Message}", e);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    private static string Format(JsonArray families)
    {
        if (families.Count == 0)
        {
            return "[]\n";
        }

        var builder = new StringBuilder();
        builder.Append("[\n");
        for (var i = 0; i < families.Count; i++)
        {
            builder.Append("  ");
            builder.Append(FormatEntry(families[i]));
            builder.Append(i == families.Count - 1 ? "\n" : ",\n");
        }

        builder.Append("]\n");
        return builder.ToString();
    }

    // Формат совпадает с исходным файлом: объект в одну строку, LF, UTF-8 без BOM.
    // Записи с нестандартной структурой сериализуются как есть (данные не теряются).
    private static string FormatEntry(JsonNode? node)
    {
        if (node is JsonObject entry &&
            entry.Count == 2 &&
            entry["name"] is JsonValue nameValue &&
            nameValue.TryGetValue<string>(out var name) &&
            !string.IsNullOrEmpty(name) &&
            entry["patterns"] is JsonArray patterns &&
            patterns.All(IsStringValue))
        {
            var patternText = string.Join(", ", patterns.Select(pattern => pattern!.ToJsonString()));
            return $"{{ \"name\": {entry["name"]!.ToJsonString()}, \"patterns\": [{patternText}] }}";
        }

        return node?.ToJsonString() ?? "null";
    }

    private static bool IsStringValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out _);
}
