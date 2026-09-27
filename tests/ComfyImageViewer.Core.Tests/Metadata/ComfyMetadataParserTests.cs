using ComfyImageViewer.Core.Metadata;
using ComfyImageViewer.Core.Models;
using ComfyImageViewer.Core.Tests.Metadata;
using Xunit;

namespace ComfyImageViewer.Core.Tests.Metadata;

public class ComfyMetadataParserTests
{
    private readonly ComfyMetadataParser _parser = new();

    private static IReadOnlyDictionary<string, string> Chunks(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value);

    private const string PromptJson = """
        {
          "1": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "modelA.safetensors" } },
          "2": { "class_type": "CLIPTextEncode", "inputs": { "text": "a photo of a cat" } },
          "3": { "class_type": "UNETLoader", "inputs": { "unet_name": "flux.safetensors" } },
          "4": { "class_type": "KSampler", "inputs": { "seed": 1 } }
        }
        """;

    private const string WorkflowJson = """
        {
          "nodes": [
            { "type": "CheckpointLoaderSimple", "widgets_values": ["modelA.safetensors", "vpred"] },
            { "type": "CLIPTextEncode", "widgets_values": ["a photo of a cat"] },
            { "type": "UNETLoader", "widgets_values": ["flux.safetensors", "default"] }
          ]
        }
        """;

    [Fact]
    public void Parse_PromptAndWorkflow_ExtractsModelAndPrompt()
    {
        var metadata = _parser.Parse(Chunks(("prompt", PromptJson), ("workflow", WorkflowJson)));

        Assert.Equal(MetadataStatus.Present, metadata.Status);
        Assert.Equal(new[] { "modelA.safetensors", "flux.safetensors" }, metadata.ModelNames);
        Assert.Equal(new[] { "a photo of a cat" }, metadata.PromptTexts);
        Assert.Equal(PromptJson, metadata.RawPromptJson);
        Assert.Equal(WorkflowJson, metadata.RawWorkflowJson);
    }

    [Fact]
    public void Parse_EmptyChunks_ReturnsNone()
    {
        var metadata = _parser.Parse(Chunks());

        Assert.Equal(MetadataStatus.None, metadata.Status);
        Assert.Empty(metadata.ModelNames);
        Assert.Empty(metadata.PromptTexts);
        Assert.Equal("", metadata.RawPromptJson);
        Assert.Equal("", metadata.RawWorkflowJson);
    }

    [Fact]
    public void Parse_PromptWithoutLoaderNodes_PresentWithEmptyModel()
    {
        const string json = """{ "1": { "class_type": "CLIPTextEncode", "inputs": { "text": "hello" } } }""";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(MetadataStatus.Present, metadata.Status);
        Assert.Empty(metadata.ModelNames);
        Assert.Equal(new[] { "hello" }, metadata.PromptTexts);
        Assert.Equal(json, metadata.RawPromptJson);
    }

    [Fact]
    public void Parse_TwoClipTextEncodes_PreservesNodeOrder()
    {
        const string json = """
            {
              "10": { "class_type": "CLIPTextEncode", "inputs": { "text": "positive" } },
              "2": { "class_type": "CLIPTextEncode", "inputs": { "text": "negative" } },
              "20": { "class_type": "CLIPTextEncode", "inputs": { "text": "third" } }
            }
            """;

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        // Числовой порядок nodeId: 2, 10, 20 (не строковый).
        Assert.Equal(new[] { "negative", "positive", "third" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_DuplicateModel_CollapsesToSingleEntry()
    {
        const string json = """
            {
              "1": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "same.safetensors" } },
              "5": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "same.safetensors" } }
            }
            """;

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "same.safetensors" }, metadata.ModelNames);
    }

    // Реальные типы нод из корпуса: Qwen Image 2.1 (TextEncodeQwenImage21) и прочие
    // энкодеры/виджеты, где лежит текст генерации. До этого промпт у таких файлов был «недоступен».
    [Theory]
    [InlineData("TextEncodeQwenImage21", "prompt")]
    [InlineData("TextEncodeQwenImageEdit", "prompt")]
    [InlineData("TextEncodeQwenImageEditPlusAdvance_lrzjason", "prompt")]
    [InlineData("EditTextEncode_EditUtils", "prompt")]
    [InlineData("TextEncodeKrea2OstrisEdit", "prompt")]
    [InlineData("TextEncodeEditAdvanced", "prompt")]
    [InlineData("BNK_CLIPTextEncodeAdvanced", "text")]
    [InlineData("GoogleTranslateCLIPTextEncodeNode", "text")]
    [InlineData("GoogleTranslateTextNode", "text")]
    [InlineData("DeepTranslatorCLIPTextEncodeNode", "text")]
    [InlineData("CR Prompt Text", "prompt")]
    [InlineData("CR Text", "text")]
    [InlineData("Text Multiline", "text")]
    [InlineData("LayerUtility: TextBox", "text")]
    [InlineData("String Literal", "string")]
    [InlineData("PromptRelayEncode", "global_prompt")]
    [InlineData("LTXDirector", "global_prompt")]
    public void Parse_PromptNodes_FromRealCorpus_AreExtracted(string classType, string inputName)
    {
        var json = $"{{ \"1\": {{ \"class_type\": \"{classType}\", \"inputs\": {{ \"{inputName}\": \"real prompt text\" }} }} }}";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "real prompt text" }, metadata.PromptTexts);
    }

    // Ноды-показыватели и LLM/vision-ноды промптом не считаются: их текст — служебный
    // (отображение, инструкция для модели), а не промпт генерации.
    [Theory]
    [InlineData("easy showAnything", "text")]
    [InlineData("PixaromaShowText", "text")]
    [InlineData("QwenVisionParser", "prompt")]
    [InlineData("Qwen3_VQA", "text")]
    [InlineData("Google-Gemini", "prompt")]
    [InlineData("OllamaGenerateV2", "prompt")]
    [InlineData("StringReplace", "string")]
    [InlineData("StringListIndex", "text")]
    [InlineData("AddLabel", "text")]
    public void Parse_DisplayAndLlmNodes_AreNotPromptSources(string classType, string inputName)
    {
        var json = $"{{ \"1\": {{ \"class_type\": \"{classType}\", \"inputs\": {{ \"{inputName}\": \"shown text\" }} }} }}";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(MetadataStatus.Present, metadata.Status);
        Assert.Empty(metadata.PromptTexts);
    }

    [Fact]
    public void Parse_QwenImage21LikeRealFile_ExtractsModelAndPrompt()
    {
        // Форма реального файла: Qwen Image 2.1_00001_.png (UNETLoader + TextEncodeQwenImage21).
        const string json = """
            {
              "1": { "class_type": "UNETLoader", "inputs": { "unet_name": "Qwen2_1\\qwen_image_2.1_int8_convrot.safetensors" } },
              "4": { "class_type": "TextEncodeQwenImage21", "inputs": { "prompt": "An ultra-realistic full-body image: an American woman" } },
              "92": { "class_type": "VAEDecode", "inputs": {} }
            }
            """;

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "Qwen2_1\\qwen_image_2.1_int8_convrot.safetensors" }, metadata.ModelNames);
        Assert.Equal(new[] { "An ultra-realistic full-body image: an American woman" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_Workflow_QwenImage21Widget_IsExtracted()
    {
        const string json = """
            {
              "nodes": [
                { "type": "TextEncodeQwenImage21", "widgets_values": ["workflow prompt text"] }
              ]
            }
            """;

        var metadata = _parser.Parse(Chunks(("workflow", json)));

        Assert.Equal(MetadataStatus.Present, metadata.Status);
        Assert.Equal(new[] { "workflow prompt text" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_WorkflowOnly_ExtractsModelFromFirstWidgetValue()
    {
        var metadata = _parser.Parse(Chunks(("workflow", WorkflowJson)));

        Assert.Equal(MetadataStatus.Present, metadata.Status);
        Assert.Equal(new[] { "modelA.safetensors", "flux.safetensors" }, metadata.ModelNames);
        Assert.Equal(new[] { "a photo of a cat" }, metadata.PromptTexts);
        Assert.Equal("", metadata.RawPromptJson);
        Assert.Equal(WorkflowJson, metadata.RawWorkflowJson);
    }

    [Fact]
    public void Parse_WorkflowOnly_PreservesArrayOrder()
    {
        const string json = """
            {
              "nodes": [
                { "type": "CLIPTextEncode", "widgets_values": ["first"] },
                { "type": "CLIPTextEncode", "widgets_values": ["second"] }
              ]
            }
            """;

        var metadata = _parser.Parse(Chunks(("workflow", json)));

        Assert.Equal(new[] { "first", "second" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_UnetLoaderPrompt_ExtractsUnetName()
    {
        const string json = """{ "1": { "class_type": "UNETLoader", "inputs": { "unet_name": "unet.safetensors" } } }""";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "unet.safetensors" }, metadata.ModelNames);
    }

    [Fact]
    public void Parse_EmptyPromptAndWorkflowValues_ReturnsNone()
    {
        var metadata = _parser.Parse(Chunks(("prompt", ""), ("workflow", "")));

        Assert.Equal(MetadataStatus.None, metadata.Status);
        Assert.Equal("", metadata.RawPromptJson);
    }

    [Fact]
    public void Parse_InvalidJson_PresentWithoutExtraction()
    {
        var metadata = _parser.Parse(Chunks(("prompt", "{not json")));

        Assert.Equal(MetadataStatus.Present, metadata.Status);
        Assert.Empty(metadata.ModelNames);
        Assert.Empty(metadata.PromptTexts);
        Assert.Equal("{not json", metadata.RawPromptJson);
    }

    [Fact]
    public void Parse_NonStringTextInput_Ignored()
    {
        const string json = """{ "1": { "class_type": "CLIPTextEncode", "inputs": { "text": 42 } } }""";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Empty(metadata.PromptTexts);
    }

    [Fact]
    public void Parse_WorkflowLoaderWithoutStringWidget_NoModel()
    {
        const string json = """
            { "nodes": [ { "type": "CheckpointLoaderSimple", "widgets_values": [123] } ] }
            """;

        var metadata = _parser.Parse(Chunks(("workflow", json)));

        Assert.Empty(metadata.ModelNames);
    }

    // Реальный формат ComfyUI (Python json.dumps): голые NaN/Infinity в widget-значениях.
    private const string PromptJsonWithNaN = """
        {
          "9": { "class_type": "SaveImage", "inputs": { "filename_prefix": "z-image", "images": ["57:8", 0] } },
          "62": { "class_type": "Textbox", "inputs": { "text": "a cat on the roof" } },
          "57:28": { "class_type": "UNETLoader", "inputs": { "unet_name": "z_image_turbo.safetensors" } },
          "57:27": { "class_type": "CLIPTextEncode", "inputs": { "text": ["62", 0], "clip": ["57:30", 0] } },
          "57:3": { "class_type": "KSampler", "inputs": { "seed": 1, "widgets_changed": [NaN] } },
          "57:29": { "class_type": "VAELoader", "inputs": { "vae_name": "ae.safetensors", "strength": Infinity } }
        }
        """;

    [Fact]
    public void Parse_PromptJsonWithNonFiniteNumbers_ExtractsModelAndPrompt()
    {
        var metadata = _parser.Parse(Chunks(("prompt", PromptJsonWithNaN)));

        Assert.Equal(MetadataStatus.Present, metadata.Status);
        Assert.Equal(new[] { "z_image_turbo.safetensors" }, metadata.ModelNames);
        Assert.Equal(new[] { "a cat on the roof" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_WorkflowJsonWithNaN_ExtractsFromFirstWidgetValue()
    {
        const string json = """
            {
              "nodes": [
                { "type": "CheckpointLoaderSimple", "widgets_values": ["modelA.safetensors", "vpred"] },
                { "type": "CLIPTextEncode", "widgets_values": ["a photo of a cat"], "widgets_changed": [NaN] }
              ]
            }
            """;

        var metadata = _parser.Parse(Chunks(("workflow", json)));

        Assert.Equal(new[] { "modelA.safetensors" }, metadata.ModelNames);
        Assert.Equal(new[] { "a photo of a cat" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_NaNInsideStringValues_PreservedVerbatim()
    {
        const string json = """{ "1": { "class_type": "CLIPTextEncode", "inputs": { "text": "value NaN, other Infinity like -Infinity here" } } }""";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "value NaN, other Infinity like -Infinity here" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_DiffusionModelLoaderKj_ExtractsModelName()
    {
        const string json = """{ "255": { "class_type": "DiffusionModelLoaderKJ", "inputs": { "model_name": "Qwen_GGUF\\FireRed-Q5_K_M.gguf" } } }""";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "Qwen_GGUF\\FireRed-Q5_K_M.gguf" }, metadata.ModelNames);
    }

    [Fact]
    public void Parse_GgufLoaderKj_ExtractsModelName()
    {
        const string json = """{ "133": { "class_type": "GGUFLoaderKJ", "inputs": { "model_name": "Wan2_2_GGUF\\wan22_highQ4KM.gguf" } } }""";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "Wan2_2_GGUF\\wan22_highQ4KM.gguf" }, metadata.ModelNames);
    }

    [Fact]
    public void Parse_UnetLoaderGguf_CaseInsensitiveMarker_ExtractsUnetName()
    {
        const string json = """{ "1": { "class_type": "UnetLoaderGGUF", "inputs": { "unet_name": "flux1.gguf" } } }""";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "flux1.gguf" }, metadata.ModelNames);
    }

    [Fact]
    public void Parse_TextboxNode_ExtractsPromptText()
    {
        const string json = """
            {
              "62": { "class_type": "Textbox", "inputs": { "text": "prompt from primitive node" } },
              "57:27": { "class_type": "CLIPTextEncode", "inputs": { "text": ["62", 0] } }
            }
            """;

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "prompt from primitive node" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_DeepTranslatorTextNode_ExtractsPromptText()
    {
        const string json = """{ "337": { "class_type": "DeepTranslatorTextNode", "inputs": { "text": "translated prompt" } } }""";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "translated prompt" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_PrimitiveStringMultiline_ExtractsValueInput()
    {
        const string json = """{ "51": { "class_type": "PrimitiveStringMultiline", "inputs": { "value": "prompt from string primitive" } } }""";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "prompt from string primitive" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_PromptNamedInput_ExtractsFromAutoTranslateNode()
    {
        const string json = """{ "286": { "class_type": "Prompt Text (Auto Translate)", "inputs": { "prompt": "original user prompt" } } }""";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "original user prompt" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_DebugShowAnythingNode_NotTreatedAsPrompt()
    {
        // Отладочные display-ноды (числа, статусы) не являются промптом.
        const string json = """{ "5630": { "class_type": "easy showAnything", "inputs": { "text": "243" } } }""";

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Empty(metadata.PromptTexts);
    }

    [Fact]
    public void Parse_BlankTextInput_Skipped()
    {
        const string json = """
            {
              "307": { "class_type": "CLIPTextEncode", "inputs": { "text": "" } },
              "62": { "class_type": "Textbox", "inputs": { "text": "real prompt" } }
            }
            """;

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Equal(new[] { "real prompt" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_TextboxViaWorkflow_ExtractsFromFirstWidgetValue()
    {
        const string json = """
            { "nodes": [ { "type": "Textbox", "widgets_values": ["workflow prompt"] } ] }
            """;

        var metadata = _parser.Parse(Chunks(("workflow", json)));

        Assert.Equal(new[] { "workflow prompt" }, metadata.PromptTexts);
    }

    [Fact]
    public void Parse_UpscaleModelLoader_ModelNameNotTreatedAsDiffusionModel()
    {
        // Вспомогательные загрузчики (upscale/interp/VAE) не являются основной моделью.
        const string json = """
            {
              "1": { "class_type": "UpscaleModelLoader", "inputs": { "model_name": "4xESRGAN.pth" } },
              "2": { "class_type": "FrameInterpolationModelLoader", "inputs": { "model_name": "rife49.pth" } },
              "3": { "class_type": "VAELoader", "inputs": { "vae_name": "vae.safetensors" } }
            }
            """;

        var metadata = _parser.Parse(Chunks(("prompt", json)));

        Assert.Empty(metadata.ModelNames);
    }
}
