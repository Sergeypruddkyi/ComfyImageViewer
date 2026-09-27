using System.IO;
using System.Text;
using ComfyImageViewer.Core.Config;
using ComfyImageViewer.Core.Tests.Scanning;
using Xunit;

namespace ComfyImageViewer.Core.Tests.Config;

public class ModelFamiliesProviderTests
{
    [Fact]
    public void ValidJson_ParsesFamiliesAndPatterns()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            const string json = """
                [
                  { "name": "SD 1.5", "patterns": ["sd15", "sd1.5", "sd_1.5"] },
                  { "name": "SDXL", "patterns": ["sdxl", "sd_xl"] }
                ]
                """;
            var path = TestTree.WriteFile(root, "ModelFamilies.json", Encoding.UTF8.GetBytes(json));

            var provider = new ModelFamiliesProvider(path);

            Assert.Equal(2, provider.Families.Count);
            Assert.Equal("SD 1.5", provider.Families[0].Name);
            Assert.Equal(["sd15", "sd1.5", "sd_1.5"], provider.Families[0].Patterns);
            Assert.Equal("SDXL", provider.Families[1].Name);
            Assert.Equal(["sdxl", "sd_xl"], provider.Families[1].Patterns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Sanitizes_TrimsSkipsEmptyDedupesPatterns()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            const string json = """
                [
                  { "name": "  Krea 2  ", "patterns": [" krea2 ", "KREA2", "", "krea_2", 42, "krea_2"] },
                  { "name": "", "patterns": ["x"] },
                  { "patterns": ["y"] },
                  { "name": "Wan", "patterns": [] }
                ]
                """;
            var path = TestTree.WriteFile(root, "ModelFamilies.json", Encoding.UTF8.GetBytes(json));

            var provider = new ModelFamiliesProvider(path);

            // Семейства с пустым/отсутствующим именем пропущены; порядок сохранён.
            Assert.Equal(2, provider.Families.Count);
            Assert.Equal("Krea 2", provider.Families[0].Name);
            // trim + пропуск пустых/не-строк + dedupe без учёта регистра, порядок сохранён.
            Assert.Equal(["krea2", "krea_2"], provider.Families[0].Patterns);
            Assert.Equal("Wan", provider.Families[1].Name);
            Assert.Empty(provider.Families[1].Patterns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BrokenJson_ReturnsEmptyList()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var broken = TestTree.WriteFile(root, "broken.json", Encoding.UTF8.GetBytes("{ not valid json ]"));
            var notArray = TestTree.WriteFile(root, "notarray.json", Encoding.UTF8.GetBytes("""{ "name": "X" }"""));

            Assert.Empty(new ModelFamiliesProvider(broken).Families);
            Assert.Empty(new ModelFamiliesProvider(notArray).Families);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingFile_ReturnsEmptyList()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var provider = new ModelFamiliesProvider(Path.Combine(root, "missing.json"));

            Assert.Empty(provider.Families);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
