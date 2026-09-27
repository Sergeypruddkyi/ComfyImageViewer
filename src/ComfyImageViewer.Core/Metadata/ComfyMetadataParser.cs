using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.Core.Metadata;

public sealed class ComfyMetadataParser : IComfyMetadataParser
{
    private const string CheckpointLoaderMarker = "CheckpointLoader";
    // Регистронезависимо: покрывает UNETLoader, UnetLoaderGGUF, UnetLoaderGGUFAdvanced.
    private const string UnetLoaderMarker = "unetloader";
    // "UNet loader with Name (Image Saver)" — вариант с пробелом внутри имени класса.
    private const string UnetLoaderSpacedMarker = "unet loader";
    // Загрузчики diffusion-моделей KJNodes: имя модели в input "model_name".
    private const string GgufLoaderKjType = "GGUFLoaderKJ";
    private const string DiffusionModelLoaderKjType = "DiffusionModelLoaderKJ";

    private const string ClipTextEncodeType = "CLIPTextEncode";
    // Новый фронтенд ComfyUI выносит текст в отдельные ноды; CLIPTextEncode получает его по link.
    private const string TextboxType = "Textbox";
    private const string DeepTranslatorTextNodeType = "DeepTranslatorTextNode";

    // Ноды, хранящие итоговый текст промпта (class_type -> имя текстового input).
    // Набор — фактические варианты, обнаруженные в реальных ComfyUI PNG.
    private static readonly (string ClassType, string TextInput)[] TextNodeTypes =
    [
        (ClipTextEncodeType, "text"),
        (TextboxType, "text"),
        ("PrimitiveStringMultiline", "value"),
        (DeepTranslatorTextNodeType, "text"),
        ("Prompt Text (Auto Translate)", "prompt"),
        ("Krea2EditGroundedEncode", "prompt"),
        ("TextEncodeQwenImageEditPlus", "prompt"),
        ("easy promptLine", "prompt"),
        ("MiniMaxH3ImageToVideo", "prompt"),
        ("PromptRelayEncodeTimeline", "global_prompt"),
        ("StringConstantMultiline", "string"),
        // Qwen Image 2.1 (и редакторные варианты): промпт в input "prompt".
        // Именно этого типа не хватало: у файлов Qwen 2.1 промпт был «недоступен».
        ("TextEncodeQwenImage21", "prompt"),
        ("TextEncodeQwenImageEdit", "prompt"),
        ("TextEncodeQwenImageEditPlusAdvance_lrzjason", "prompt"),
        ("EditTextEncode_EditUtils", "prompt"),
        ("TextEncodeKrea2OstrisEdit", "prompt"),
        ("TextEncodeEditAdvanced", "prompt"),
        ("BNK_CLIPTextEncodeAdvanced", "text"),
        // Переводчики промпта: DeepTranslator уже читался, Google — та же категория (текст генерации).
        ("GoogleTranslateCLIPTextEncodeNode", "text"),
        ("GoogleTranslateTextNode", "text"),
        ("DeepTranslatorCLIPTextEncodeNode", "text"),
        // Текстовые виджеты и промпт-ноды семейств CR/LTX плюс relay-энкодер.
        ("CR Prompt Text", "prompt"),
        ("CR Text", "text"),
        ("Text Multiline", "text"),
        ("LayerUtility: TextBox", "text"),
        ("String Literal", "string"),
        ("PromptRelayEncode", "global_prompt"),
        ("LTXDirector", "global_prompt"),
    ];

    private static readonly string[] NonFiniteTokens = { "NaN", "-Infinity", "Infinity" };

    public ImageMetadata Parse(IReadOnlyDictionary<string, string> textChunks)
    {
        textChunks.TryGetValue("prompt", out string? promptJson);
        textChunks.TryGetValue("workflow", out string? workflowJson);

        if (string.IsNullOrEmpty(promptJson) && string.IsNullOrEmpty(workflowJson))
            return new ImageMetadata(MetadataStatus.None, [], [], "", "");

        var models = new List<string>();
        var prompts = new List<string>();

        bool usedPrompt = false;
        if (!string.IsNullOrEmpty(promptJson))
            usedPrompt = TryExtractFromPromptJson(promptJson, models, prompts);

        if (!usedPrompt && !string.IsNullOrEmpty(workflowJson))
            TryExtractFromWorkflowJson(workflowJson, models, prompts);

        return new ImageMetadata(
            MetadataStatus.Present,
            models,
            prompts,
            promptJson ?? "",
            workflowJson ?? "");
    }

    private static bool TryExtractFromPromptJson(string json, List<string> models, List<string> prompts)
    {
        if (!TryParseJsonDocument(json, out JsonDocument? document))
            return false;

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            // Узлы в порядке числового возрастания nodeId; нечисловые ключи — после, в исходном порядке.
            var ordered = new List<(long Id, JsonElement Node)>();
            var unordered = new List<JsonElement>();

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (long.TryParse(property.Name, out long id))
                    ordered.Add((id, property.Value));
                else
                    unordered.Add(property.Value);
            }

            foreach (JsonElement node in ordered.OrderBy(e => e.Id).Select(e => e.Node).Concat(unordered))
                ProcessPromptNode(node, models, prompts);
        }

        return true;
    }

    private static bool TryParseJsonDocument(string json, [NotNullWhen(true)] out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            // Строгий JSON не принимает NaN/Infinity, которыми Python-писатель
            // ComfyUI заполняет widget-значения ("widgets_changed": [NaN]).
        }

        string? sanitized = QuoteNonFiniteNumbers(json);
        if (sanitized is null)
        {
            document = null;
            return false;
        }

        try
        {
            document = JsonDocument.Parse(sanitized);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    /// <summary>
    /// Закавычивает голые NaN/Infinity/-Infinity вне строк, делая JSON пригодным
    /// для строгого парсера. Возвращает null, если таких токенов нет.
    /// </summary>
    private static string? QuoteNonFiniteNumbers(string json)
    {
        bool inString = false;
        var builder = new StringBuilder(json.Length);
        bool modified = false;
        int i = 0;

        while (i < json.Length)
        {
            char c = json[i];

            if (inString)
            {
                builder.Append(c);
                if (c == '\\' && i + 1 < json.Length)
                {
                    builder.Append(json[i + 1]);
                    i += 2;
                    continue;
                }
                if (c == '"')
                    inString = false;
                i++;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                builder.Append(c);
                i++;
                continue;
            }

            if ((c == 'N' || c == 'I' || c == '-') && MatchNonFiniteToken(json, i, out int tokenLength))
            {
                builder.Append('"').Append(json, i, tokenLength).Append('"');
                modified = true;
                i += tokenLength;
                continue;
            }

            builder.Append(c);
            i++;
        }

        return modified ? builder.ToString() : null;
    }

    private static bool MatchNonFiniteToken(string json, int start, out int length)
    {
        foreach (string token in NonFiniteTokens)
        {
            if (json.AsSpan(start).StartsWith(token, StringComparison.Ordinal) &&
                !IsWordChar(At(json, start - 1)) &&
                !IsWordChar(At(json, start + token.Length)))
            {
                length = token.Length;
                return true;
            }
        }

        length = 0;
        return false;

        static char At(string s, int index) => index >= 0 && index < s.Length ? s[index] : ' ';
    }

    private static bool IsWordChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c == '_';

    private static bool IsDiffusionModelLoader(string classType, out string modelNameInput)
    {
        if (classType.Contains(CheckpointLoaderMarker, StringComparison.Ordinal))
        {
            modelNameInput = "ckpt_name";
            return true;
        }

        if (classType.Contains(UnetLoaderMarker, StringComparison.OrdinalIgnoreCase) ||
            classType.Contains(UnetLoaderSpacedMarker, StringComparison.OrdinalIgnoreCase))
        {
            modelNameInput = "unet_name";
            return true;
        }

        if (string.Equals(classType, GgufLoaderKjType, StringComparison.Ordinal) ||
            string.Equals(classType, DiffusionModelLoaderKjType, StringComparison.Ordinal))
        {
            modelNameInput = "model_name";
            return true;
        }

        modelNameInput = "";
        return false;
    }

    // Ноды, хранящие итоговый текст промпта.
    private static bool IsTextNodeType(string classType) =>
        Array.Exists(TextNodeTypes, t => string.Equals(t.ClassType, classType, StringComparison.Ordinal));

    private static void ProcessPromptNode(JsonElement node, List<string> models, List<string> prompts)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        string? classType = null;
        bool hasInputs = node.TryGetProperty("inputs", out JsonElement inputs) && inputs.ValueKind == JsonValueKind.Object;
        if (node.TryGetProperty("class_type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String)
            classType = typeElement.GetString();

        if (classType is null)
            return;

        if (hasInputs)
        {
            if (IsDiffusionModelLoader(classType, out string modelNameInput) &&
                inputs.TryGetProperty(modelNameInput, out JsonElement model) &&
                model.ValueKind == JsonValueKind.String)
            {
                AddUnique(models, model.GetString()!);
            }

            if (TryGetTextNodeValue(classType, inputs, out string? text))
                AddPrompt(prompts, text!);
        }
    }

    private static bool TryGetTextNodeValue(string classType, JsonElement inputs, [NotNullWhen(true)] out string? text)
    {
        foreach ((string nodeType, string inputName) in TextNodeTypes)
        {
            if (!string.Equals(nodeType, classType, StringComparison.Ordinal))
                continue;

            if (inputs.TryGetProperty(inputName, out JsonElement element) &&
                element.ValueKind == JsonValueKind.String)
            {
                text = element.GetString();
                return text is not null;
            }
        }

        text = null;
        return false;
    }

    private static bool TryExtractFromWorkflowJson(string json, List<string> models, List<string> prompts)
    {
        if (!TryParseJsonDocument(json, out JsonDocument? document))
            return false;

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("nodes", out JsonElement nodes) ||
                nodes.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement node in nodes.EnumerateArray())
                ProcessWorkflowNode(node, models, prompts);
        }

        return true;
    }

    private static void ProcessWorkflowNode(JsonElement node, List<string> models, List<string> prompts)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        if (!node.TryGetProperty("type", out JsonElement typeElement) || typeElement.ValueKind != JsonValueKind.String)
            return;
        string classType = typeElement.GetString()!;

        string? firstWidget = null;
        if (node.TryGetProperty("widgets_values", out JsonElement widgets) && widgets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement widget in widgets.EnumerateArray())
            {
                if (widget.ValueKind == JsonValueKind.String)
                {
                    firstWidget = widget.GetString();
                    break;
                }
            }
        }

        if (firstWidget is null)
            return;

        if (IsDiffusionModelLoader(classType, out _))
            AddUnique(models, firstWidget);

        if (IsTextNodeType(classType))
            AddPrompt(prompts, firstWidget);
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.Ordinal))
            values.Add(value);
    }

    // Пустые text-значения (незаполненные CLIPTextEncode) не должны затирать полезные промпты.
    private static void AddPrompt(List<string> prompts, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            prompts.Add(value);
    }
}
