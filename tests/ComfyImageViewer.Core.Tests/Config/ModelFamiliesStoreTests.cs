using System.IO;
using System.Text;
using System.Text.Json;
using ComfyImageViewer.Core.Config;
using ComfyImageViewer.Core.Tests.Scanning;
using Xunit;

namespace ComfyImageViewer.Core.Tests.Config;

public class ModelFamiliesStoreTests
{
    private const string SampleJson = """
        [
          { "name": "SD 1.5", "patterns": ["sd15", "sd1.5", "sd_1.5", "v1-5", "sd 1.5"] },
          { "name": "SDXL", "patterns": ["sdxl", "sd_xl", "xl_base"] },
          { "name": "Flux 1", "patterns": ["flux1", "flux_1", "flux.1"] },
          { "name": "Flux 2 Klein", "patterns": ["klein", "flux2_klein", "flux2-klein"] },
          { "name": "Qwen", "patterns": ["qwen"] },
          { "name": "Krea 2", "patterns": ["krea2", "krea_2", "kreamania", "kreax2"] },
          { "name": "Wan", "patterns": ["wan"] },
          { "name": "LTX", "patterns": ["ltx"] },
          { "name": "MiniMax H3", "patterns": ["minimax"] }
        ]
        """;

    private static readonly string[] SampleNames =
        ["SD 1.5", "SDXL", "Flux 1", "Flux 2 Klein", "Qwen", "Krea 2", "Wan", "LTX", "MiniMax H3"];

    [Fact]
    public void Loads_ExistingSampleFamilies()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            Assert.Equal(SampleNames, store.Families.Select(f => f.Name));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Add_SavesAndReload_SeesNewEntry()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            store.Add("  Qwen Image 2.1  ", " qwen_image_2.1 , qwen2_1 ,, QWEN_IMAGE_2.1 ; qwen 2.1 ");

            var reloaded = new ModelFamiliesStore(path);
            Assert.Equal(10, reloaded.Families.Count);
            var added = reloaded.Families.Single(f => f.Name == "Qwen Image 2.1");
            // Название нового семейства автоматически становится первым шаблоном.
            Assert.Equal(["qwen image 2.1", "qwen_image_2.1", "qwen2_1", "qwen 2.1"], added.Patterns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Регрессия: «Qwen 2.1» + «qwen_image_2.1» → patterns обязаны содержать и название
    // («qwen 2.1»), и шаблон модели, иначе фильтр «По модели» не находит изображения.
    [Fact]
    public void Add_Qwen21_AddsLowercasedNameAsFirstPattern()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            store.Add("Qwen 2.1", "qwen_image_2.1");

            var reloaded = new ModelFamiliesStore(path);
            var added = Assert.Single(reloaded.Families, f => f.Name == "Qwen 2.1");
            Assert.Equal(["qwen 2.1", "qwen_image_2.1"], added.Patterns);
            Assert.Contains("\"name\": \"Qwen 2.1\", \"patterns\": [\"qwen 2.1\", \"qwen_image_2.1\"]", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Пустое поле «Поисковые шаблоны»: название всё равно становится шаблоном.
    [Fact]
    public void Add_EmptyPatterns_NameBecomesSolePattern()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            store.Add("Qwen 2.1", " , ");

            var reloaded = new ModelFamiliesStore(path);
            var added = Assert.Single(reloaded.Families, f => f.Name == "Qwen 2.1");
            Assert.Equal(["qwen 2.1"], added.Patterns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Название уже введено пользователем вручную — второй раз не добавляем.
    [Fact]
    public void Add_NameAlreadyAmongPatterns_NoDuplicate()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            store.Add("Qwen 2.1", "qwen_image_2.1, QWEN 2.1");

            var reloaded = new ModelFamiliesStore(path);
            var added = Assert.Single(reloaded.Families, f => f.Name == "Qwen 2.1");
            Assert.Equal(["qwen_image_2.1", "QWEN 2.1"], added.Patterns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Существующие записи (и их patterns) при добавлении нового семейства не меняются.
    [Fact]
    public void Add_KeepsExistingFamiliesPatternsUnchanged()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var before = new ModelFamiliesStore(path).Families
                .ToDictionary(f => f.Name, f => f.Patterns.ToArray(), StringComparer.Ordinal);
            var store = new ModelFamiliesStore(path);

            store.Add("Qwen 2.1", "qwen_image_2.1");

            var reloaded = new ModelFamiliesStore(path);
            foreach (var (name, patterns) in before)
            {
                var family = Assert.Single(reloaded.Families, f => f.Name == name);
                Assert.Equal(patterns, family.Patterns);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Add_KeepsExistingFamiliesAndValidJson()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            store.Add("New Family", "newf");

            var reloaded = new ModelFamiliesStore(path);
            Assert.Equal(SampleNames.Length + 1, reloaded.Families.Count);
            foreach (var name in SampleNames)
            {
                Assert.Contains(reloaded.Families, f => f.Name == name);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
            Assert.Equal(SampleNames.Length + 1, document.RootElement.GetArrayLength());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Add_EmptyName_ThrowsAndFileUntouched()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);
            var before = File.ReadAllBytes(path);

            Assert.Throws<ArgumentException>(() => store.Add("   ", "x"));
            Assert.Throws<ArgumentException>(() => store.Add(null!, "x"));

            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(SampleNames.Length, store.Families.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Add_DuplicateName_Throws()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            Assert.Throws<InvalidOperationException>(() => store.Add("sdxl", "y"));
            Assert.Equal(SampleNames.Length, store.Families.Count);
            Assert.Equal(SampleNames.Length, new ModelFamiliesStore(path).Families.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Add_EmptyPatterns_KeepsEntryWithNameAsSolePattern()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            store.Add("No Patterns", " , ");

            var reloaded = new ModelFamiliesStore(path);
            var added = Assert.Single(reloaded.Families, f => f.Name == "No Patterns");
            Assert.Equal(["no patterns"], added.Patterns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Add_WritesDocumentedFormat_Utf8NoBom()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            store.Add("New Family", "newf");

            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            var text = Encoding.UTF8.GetString(bytes);
            Assert.StartsWith("[\n  { \"name\": \"SD 1.5\"", text);
            Assert.EndsWith("]\n", text);
            Assert.DoesNotContain("\r", text);

            // Документированный формат: запятая в конце строки записи, как в исходном файле.
            // Название нового семейства автоматически попадает в patterns первым.
            var sample = SampleJson.Replace("\r\n", "\n");
            var idx = sample.LastIndexOf("\n]");
            var expected = sample[..idx]
                + ",\n  { \"name\": \"New Family\", \"patterns\": [\"new family\", \"newf\"] }"
                + sample[idx..]
                + "\n";
            Assert.Equal(expected, text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AddThenRemove_RestoresOriginalFileBytes()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var original = Encoding.UTF8.GetBytes(SampleJson + "\n");
            var path = TestTree.WriteFile(root, "ModelFamilies.json", original);
            var store = new ModelFamiliesStore(path);

            store.Add("New Family", "newf");
            store.Remove("New Family");

            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Update_ChangesNameAndPatterns()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            store.Update("Qwen", "Qwen Image 2.1", "qwen_image_2.1, qwen2_1");

            var reloaded = new ModelFamiliesStore(path);
            Assert.Equal(SampleNames.Length, reloaded.Families.Count);
            Assert.DoesNotContain(reloaded.Families, f => f.Name == "Qwen");
            var updated = reloaded.Families.Single(f => f.Name == "Qwen Image 2.1");
            Assert.Equal(["qwen_image_2.1", "qwen2_1"], updated.Patterns);
            foreach (var name in SampleNames.Where(n => n != "Qwen"))
            {
                Assert.Contains(reloaded.Families, f => f.Name == name);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Update_SameName_OnlyRewritesPatterns()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            store.Update("Wan", "Wan", "wan, wan2");

            var reloaded = new ModelFamiliesStore(path);
            Assert.Equal(SampleNames.Length, reloaded.Families.Count);
            var updated = reloaded.Families.Single(f => f.Name == "Wan");
            Assert.Equal(["wan", "wan2"], updated.Patterns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Update_UnknownOrDuplicateTarget_Throws()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            Assert.Throws<InvalidOperationException>(() => store.Update("Missing", "X", "x"));
            Assert.Throws<InvalidOperationException>(() => store.Update("Qwen", "SDXL", "y"));

            Assert.Equal(SampleNames.Length, new ModelFamiliesStore(path).Families.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Remove_DeletesOnlyTarget()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            store.Remove("Krea 2");

            var reloaded = new ModelFamiliesStore(path);
            Assert.Equal(SampleNames.Length - 1, reloaded.Families.Count);
            Assert.DoesNotContain(reloaded.Families, f => f.Name == "Krea 2");
            foreach (var name in SampleNames.Where(n => n != "Krea 2"))
            {
                Assert.Contains(reloaded.Families, f => f.Name == name);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(SampleNames.Length - 1, document.RootElement.GetArrayLength());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Remove_UnknownName_Throws()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            Assert.Throws<InvalidOperationException>(() => store.Remove("Missing"));
            Assert.Equal(SampleNames.Length, new ModelFamiliesStore(path).Families.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BrokenJson_AddThrowsAndFileNotDamaged()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var broken = TestTree.WriteFile(root, "ModelFamilies.json", Encoding.UTF8.GetBytes("{ not valid ]"));
            var before = File.ReadAllBytes(broken);
            var store = new ModelFamiliesStore(broken);

            Assert.Throws<InvalidOperationException>(() => store.Add("X", "x"));

            Assert.Equal(before, File.ReadAllBytes(broken));

            var notArray = TestTree.WriteFile(root, "notarray.json", Encoding.UTF8.GetBytes("""{ "name": "X" }"""));
            var store2 = new ModelFamiliesStore(notArray);
            Assert.Throws<InvalidOperationException>(() => store2.Add("X", "x"));
            Assert.Equal("""{ "name": "X" }""", File.ReadAllText(notArray));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingFile_AddCreatesFile()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = Path.Combine(root, "ModelFamilies.json");
            var store = new ModelFamiliesStore(path);
            Assert.Empty(store.Families);

            store.Add("SD 1.5", "sd15");

            Assert.True(File.Exists(path));
            var reloaded = new ModelFamiliesStore(path);
            var added = Assert.Single(reloaded.Families, f => f.Name == "SD 1.5");
            Assert.Equal(["sd 1.5", "sd15"], added.Patterns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Add_PreservesUnknownEntryShapes()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            const string json = """
                [
                  42,
                  { "name": "Keep", "patterns": ["k"], "note": "extra" },
                  "string-entry"
                ]
                """;
            var path = TestTree.WriteFile(root, "ModelFamilies.json", Encoding.UTF8.GetBytes(json));
            var store = new ModelFamiliesStore(path);

            store.Add("New Family", "newf");

            var text = File.ReadAllText(path);
            Assert.Contains("42", text);
            Assert.Contains("\"note\"", text);
            Assert.Contains("string-entry", text);
            using var document = JsonDocument.Parse(text);
            Assert.Equal(4, document.RootElement.GetArrayLength());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DraftNormalize_TrimsDropsEmptyDedupes()
    {
        var (name, patterns) = ModelFamilyDraft.Normalize("  Qwen Image 2.1  ", " a ,, b ,a, ");
        Assert.Equal("Qwen Image 2.1", name);
        Assert.Equal(["a", "b"], patterns);

        Assert.Empty(ModelFamilyDraft.ParsePatterns(" , "));
        Assert.Equal("a, b", ModelFamilyDraft.FormatPatterns(["a", "b"]));
        Assert.Throws<ArgumentException>(() => ModelFamilyDraft.Normalize("   ", "x"));
    }

    // Разные начертания одного имени — НЕ дубль: дедупликация только точная (Ordinal).
    [Fact]
    public void ParsePatterns_KeepsCaseVariantsOfSameName()
    {
        Assert.Equal(["sd 1.5", "SD 1.5"], ModelFamilyDraft.ParsePatterns("sd 1.5, SD 1.5"));
    }

    // Регрессия: введённые в диалоге шаблоны не должны теряться при Add.
    // «sd 1.5» и «SD 1.5» — оба обязаны попасть в JSON.
    [Fact]
    public void Add_Sd15_KeepsBothCaseVariantsInJson()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = TestTree.WriteFile(
                root,
                "ModelFamilies.json",
                Encoding.UTF8.GetBytes("""[{ "name": "SDXL", "patterns": ["sdxl"] }]"""));
            var store = new ModelFamiliesStore(path);

            store.Add("sd 1.5", "sd 1.5, SD 1.5");

            Assert.Contains(
                "{ \"name\": \"sd 1.5\", \"patterns\": [\"sd 1.5\", \"SD 1.5\"] }",
                File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Оба шаблона из поля «Поисковые шаблоны» сохраняются (плюс название семейства,
    // которое добавляется автоматически как первый шаблон).
    [Fact]
    public void Add_Qwen_2_1_KeepsBothPatternsInJson()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            var path = WriteSample(root);
            var store = new ModelFamiliesStore(path);

            store.Add("Qwen_2_1", "qwen2_1, qwen 2.1");

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var entry = document.RootElement
                .EnumerateArray()
                .Single(e => e.GetProperty("name").GetString() == "Qwen_2_1");
            var patterns = entry.GetProperty("patterns")
                .EnumerateArray()
                .Select(p => p.GetString())
                .ToArray();
            Assert.Contains("qwen2_1", patterns);
            Assert.Contains("qwen 2.1", patterns);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WithNamePattern_AddsNormalizedNameOnce()
    {
        Assert.Equal(["qwen 2.1", "qwen_image_2.1"], ModelFamilyDraft.WithNamePattern("  Qwen 2.1  ", ["qwen_image_2.1"]));
        Assert.Equal(["qwen 2.1"], ModelFamilyDraft.WithNamePattern("Qwen 2.1", []));
        Assert.Equal(["qwen_image_2.1", "QWEN 2.1"], ModelFamilyDraft.WithNamePattern("Qwen 2.1", ["qwen_image_2.1", "QWEN 2.1"]));
        Assert.Equal(["a", "b"], ModelFamilyDraft.WithNamePattern("   ", ["a", "b"]));
    }

    private static string WriteSample(string root) =>
        TestTree.WriteFile(root, "ModelFamilies.json", Encoding.UTF8.GetBytes(SampleJson));
}
