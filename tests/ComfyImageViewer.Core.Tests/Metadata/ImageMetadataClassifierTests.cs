using System.IO;
using ComfyImageViewer.Core.Metadata;
using ComfyImageViewer.Core.Models;
using ComfyImageViewer.Core.Tests.Metadata;
using ComfyImageViewer.Core.Tests.Scanning;
using ComfyImageViewer.Core.Tests.Thumbnails;
using Xunit;

namespace ComfyImageViewer.Core.Tests.Metadata;

/// <summary>
/// Регрессионные тесты границы ComfyUI / ordinary image path
/// по реальным вариантам metadata corpus'а E:\ScreenShot.
/// </summary>
public class ImageMetadataClassifierTests
{
    private readonly ImageMetadataClassifier _classifier =
        new(new PngTextChunkReader(), new ComfyMetadataParser(), new ImageFileInfoReader());

    private (string Dir, string Path, ImageEntry Entry) CreateFile(string name, byte[] content)
    {
        var dir = TestTree.CreateTempDir();
        var path = TestTree.WriteFile(dir, name, content);
        var fi = new FileInfo(path);
        var entry = new ImageEntry(path, name, fi.Length, fi.CreationTimeUtc, fi.LastWriteTimeUtc);
        return (dir, path, entry);
    }

    private static byte[] PngWithText(params (string Key, string Value)[] entries)
    {
        var png = PngFactory.CreatePng(32, 32);
        return entries.Aggregate(png, (current, e) => PngFixtureHelper.InjectTextChunk(current, e.Key, e.Value));
    }

    private const string ComfyPromptJson = """{ "1": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "modelA.safetensors" } } }""";

    [Fact]
    public void Classify_ComfyUiPng_PromptAndWorkflow_GoesToComfyUiPath()
    {
        var (dir, path, _) = CreateFile("comfy.png", PngWithText(("prompt", ComfyPromptJson), ("workflow", "{}")));
        try
        {
            var result = _classifier.Classify(path);

            Assert.True(result.IsComfyMetadata);
            Assert.NotNull(result.ComfyMetadata);
            Assert.Equal(MetadataStatus.Present, result.ComfyMetadata!.Status);
            Assert.Equal(["modelA.safetensors"], result.ComfyMetadata.ModelNames);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Classify_PlainPng_GoesToOrdinaryPath()
    {
        var (dir, path, _) = CreateFile("plain.png", PngFactory.CreatePng(24, 24));
        try
        {
            var result = _classifier.Classify(path);

            Assert.False(result.IsComfyMetadata);
            Assert.Null(result.ComfyMetadata);
            Assert.NotNull(result.FileInfo);
            Assert.Equal(24, result.FileInfo!.PixelWidth);
            Assert.Empty(result.FileFields);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Classify_A1111ParametersPng_GoesToOrdinaryPath_NotComfyUi()
    {
        const string parameters = "prompt text here\nNegative prompt: bad\nSteps: 20, Sampler: Euler a, Model: someCheckpoint_v10.safetensors";
        var (dir, path, _) = CreateFile("a1111.png", PngWithText(("parameters", parameters)));
        try
        {
            var result = _classifier.Classify(path);

            // "parameters" содержит внутри "Model:" — но ComfyUI detector опирается на ключи prompt/workflow.
            Assert.False(result.IsComfyMetadata);
            Assert.Null(result.ComfyMetadata);
            var field = Assert.Single(result.FileFields);
            Assert.Equal("parameters", field.Label);
            Assert.Contains("someCheckpoint_v10.safetensors", field.Value);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Classify_AigcPng_GoesToOrdinaryPath_NotComfyUi()
    {
        const string aigc = """{"Label":"1","ContentProducer":"001191330106","ProduceID":"U-_CQZGDq6SEqLCiUIzOb5kw"}""";
        var (dir, path, _) = CreateFile("aigc.png", PngWithText(("AIGC", aigc)));
        try
        {
            var result = _classifier.Classify(path);

            Assert.False(result.IsComfyMetadata);
            var field = Assert.Single(result.FileFields);
            Assert.Equal("AIGC", field.Label);
            Assert.Contains("ContentProducer", field.Value);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Classify_PngWithPromptInsideValue_StillOrdinaryPath_WhenKeysAreOther()
    {
        // Слово "prompt" внутри значения произвольного ключа НЕ делает файл ComfyUI.
        var (dir, path, _) = CreateFile("tricky.png", PngWithText(("Comment", "my favorite prompt ever")));
        try
        {
            var result = _classifier.Classify(path);

            Assert.False(result.IsComfyMetadata);
            Assert.Single(result.FileFields);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Classify_Jpeg_GoesToOrdinaryPath()
    {
        var (dir, path, _) = CreateFile("photo.jpg", JpegFactory.CreateJpeg(320, 200));
        try
        {
            var result = _classifier.Classify(path);

            Assert.False(result.IsComfyMetadata);
            Assert.Null(result.ComfyMetadata);
            Assert.NotNull(result.FileInfo);
            Assert.Equal(320, result.FileInfo!.PixelWidth);
            Assert.Equal(200, result.FileInfo.PixelHeight);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Classify_WebP_GoesToOrdinaryPath()
    {
        // Минимальный валидный 1×1 lossless WebP.
        var (dir, path, _) = CreateFile("tiny.webp", Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA=="));
        try
        {
            var result = _classifier.Classify(path);

            Assert.False(result.IsComfyMetadata);
            Assert.NotNull(result.FileInfo);
            Assert.Equal(1, result.FileInfo!.PixelWidth);
            Assert.Equal(1, result.FileInfo.PixelHeight);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Classify_ComfyUiPngWithNan_StillComfyUiPath_ExistingParser()
    {
        // NaN/Infinity в prompt JSON (fix c1e6458) — по-прежнему ComfyUI path.
        const string nanJson = """{ "1": { "class_type": "UNETLoader", "inputs": { "unet_name": "unet.safetensors" } }, "2": { "class_type": "KSampler", "inputs": { "widgets_changed": [NaN] } } }""";
        var (dir, path, _) = CreateFile("nan.png", PngWithText(("prompt", nanJson), ("workflow", "{ \"nodes\": [ { \"type\": \"CLIPTextEncode\", \"widgets_values\": [\"cat\"] } ] }")));
        try
        {
            var result = _classifier.Classify(path);

            Assert.True(result.IsComfyMetadata);
            Assert.Equal(["unet.safetensors"], result.ComfyMetadata!.ModelNames);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Classify_NoMetadata_IsNotError()
    {
        var (dir, path, _) = CreateFile("clean.png", PngFactory.CreatePng(16, 16));
        try
        {
            var result = _classifier.Classify(path);

            Assert.False(result.IsComfyMetadata);
            Assert.NotNull(result.FileInfo);
            Assert.Empty(result.FileFields);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Classify_OrdinaryPngTextKeys_FormattedAsFields()
    {
        // «Открыть всё»: полный набор найденных ordinary metadata формируется по полям.
        var (dir, path, _) = CreateFile("meta.png", PngWithText(("Software", "CivViewer"), ("Description", "test image"), ("parameters", "steps blob")));
        try
        {
            var result = _classifier.Classify(path);

            Assert.False(result.IsComfyMetadata);
            Assert.Equal(3, result.FileFields.Count);
            Assert.Equal(["Software", "Description", "parameters"], [.. result.FileFields.Select(f => f.Label)]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
