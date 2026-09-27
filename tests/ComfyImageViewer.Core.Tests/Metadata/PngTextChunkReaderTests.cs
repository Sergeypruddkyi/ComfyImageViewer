using System.IO;
using ComfyImageViewer.Core.Metadata;
using ComfyImageViewer.Core.Tests.Metadata;
using Xunit;

namespace ComfyImageViewer.Core.Tests;

public class PngTextChunkReaderTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "civ-task02-" + Guid.NewGuid().ToString("N"));

    public PngTextChunkReaderTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private string WriteFile(string fileName, byte[] bytes)
    {
        string path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ReadTextChunks_WithPromptAndWorkflow_ReturnsBothEntries()
    {
        byte[] png = PngFixtureHelper.CreatePng();
        png = PngFixtureHelper.InjectTextChunk(png, "prompt", "{}");
        png = PngFixtureHelper.InjectTextChunk(png, "workflow", "{}");
        string path = WriteFile("text.png", png);

        var chunks = new PngTextChunkReader().ReadTextChunks(path);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("{}", chunks["prompt"]);
        Assert.Equal("{}", chunks["workflow"]);
    }

    [Fact]
    public void ReadTextChunks_WithoutTextChunks_ReturnsEmptyDictionary()
    {
        string path = WriteFile("clean.png", PngFixtureHelper.CreatePng());

        var chunks = new PngTextChunkReader().ReadTextChunks(path);

        Assert.Empty(chunks);
    }

    [Fact]
    public void ReadTextChunks_TruncatedFile_Throws()
    {
        byte[] png = PngFixtureHelper.CreatePng();
        png = PngFixtureHelper.InjectTextChunk(png, "prompt", "{}");
        string path = WriteFile("truncated.png", PngFixtureHelper.Truncate(png, png.Length - 6));

        Assert.ThrowsAny<Exception>(() => new PngTextChunkReader().ReadTextChunks(path));
    }

    [Fact]
    public void ReadTextChunks_TruncatedMidChunk_Throws()
    {
        byte[] png = PngFixtureHelper.CreatePng();
        png = PngFixtureHelper.InjectTextChunk(png, "prompt", "{}");
        // Обрезаем так, что длина tEXt-чанка выходит за конец файла.
        string path = WriteFile("truncated-mid.png", PngFixtureHelper.Truncate(png, png.Length - 10));

        Assert.ThrowsAny<Exception>(() => new PngTextChunkReader().ReadTextChunks(path));
    }

    [Fact]
    public void ReadTextChunks_GarbageFile_Throws()
    {
        string path = WriteFile("garbage.png", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

        Assert.ThrowsAny<Exception>(() => new PngTextChunkReader().ReadTextChunks(path));
    }

    [Fact]
    public void ReadTextChunks_MissingFile_Throws()
    {
        Assert.ThrowsAny<Exception>(() => new PngTextChunkReader().ReadTextChunks(Path.Combine(_tempDirectory, "missing.png")));
    }

    [Fact]
    public void ReadTextChunks_Latin1Text_PreservedByteWise()
    {
        byte[] png = PngFixtureHelper.CreatePng();
        string text = "café \u00FF end";
        png = PngFixtureHelper.InjectTextChunk(png, "prompt", text);
        string path = WriteFile("latin1.png", png);

        var chunks = new PngTextChunkReader().ReadTextChunks(path);

        Assert.Equal(text, chunks["prompt"]);
    }
}
