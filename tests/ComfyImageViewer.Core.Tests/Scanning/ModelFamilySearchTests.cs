using System.IO;
using ComfyImageViewer.Core.Metadata;
using ComfyImageViewer.Core.Models;
using ComfyImageViewer.Core.Scanning;
using ComfyImageViewer.Core.Tests.Metadata;
using Xunit;

namespace ComfyImageViewer.Core.Tests.Scanning;

public class ModelFamilySearchTests
{
    private static readonly ModelFamily Krea2 = new("Krea 2", ["krea2", "krea_2", "kreamania", "kreax2"]);
    private static readonly ModelFamily KreamaniaOnly = new("Krea 2", ["kreamania"]);
    private static readonly ModelFamily Ltx = new("LTX", ["ltx"]);
    private static readonly ModelFamily Wan = new("Wan", ["wan"]);

    // Семейство из ModelFamilies.json реального пользователя: имя с пробелом («sd 1.5»)
    // автоматически становится его единственным pattern (ModelFamilyDraft.WithNamePattern).
    private static readonly ModelFamily Sd15ByName = new("sd 1.5", ["sd 1.5"]);
    private static readonly ModelFamily Qwen21ByName = new("Qwen 2.1", ["qwen 2.1"]);

    // Как в реальном корпусе: имя модели из loader-нод prompt-JSON, а НЕ из имени файла.
    private const string KreaUnetPromptJson = """
        { "1": { "class_type": "UNETLoader", "inputs": { "unet_name": "krea2_turbo_int8_convrot.safetensors" } } }
        """;

    private const string KreamaniaPromptJson = """
        { "1": { "class_type": "DiffusionModelLoaderKJ", "inputs": { "model_name": "Krea_2\\kreamania_variant7.safetensors" } } }
        """;

    private const string FluxKleinPromptJson = """
        { "1": { "class_type": "UNETLoader", "inputs": { "unet_name": "flux2_klein_9b.safetensors" } } }
        """;

    private const string WanPromptJson = """
        { "1": { "class_type": "DiffusionModelLoaderKJ", "inputs": { "model_name": "Wan2.2\\Wan2.2_Remix_NSFW_i2v_14b_720p_v3.0.safetensors" } } }
        """;

    [Fact]
    public async Task Krea2_FindsPngByUnetName()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            WriteComfyPng(root, "gen.png", KreaUnetPromptJson);
            var service = CreateService();

            var results = await service.FindAsync(root, Krea2).ToListAsync();

            Assert.Single(results, e => e.Name == "gen.png");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Krea2_FindsPngByKreamaniaPattern()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            WriteComfyPng(root, "gen2.png", KreamaniaPromptJson);
            var service = CreateService();

            // Только pattern «kreamania» — матчится внутри "Krea_2\kreamania_variant7.safetensors".
            var results = await service.FindAsync(root, KreamaniaOnly).ToListAsync();

            Assert.Single(results, e => e.Name == "gen2.png");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Families_DoNotMix()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            WriteComfyPng(root, "flux.png", FluxKleinPromptJson);
            WriteComfyPng(root, "wan.png", WanPromptJson);
            var service = CreateService();

            // flux2_klein не матчится в Krea 2; Wan2.2 не матчится в LTX.
            Assert.Empty(await service.FindAsync(root, Krea2).ToListAsync());
            Assert.Empty(await service.FindAsync(root, Ltx).ToListAsync());

            // Позитивный контроль: metadata в файлах есть, Wan их находит.
            Assert.Single(await service.FindAsync(root, Wan).ToListAsync(), e => e.Name == "wan.png");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Регрессия: имя семейства вводит человек («sd 1.5»), а loader хранит значение модели
    // файловым написанием («SD15\...»). «Как есть» такое имя внутри значения модели не
    // встречается ни разу (проверено на реальном корпусе PNG) — сопоставление обязано идти
    // по нормализованной строке (разделители отброшены).
    [Fact]
    public async Task FamilyNameWithSpace_MatchesFilePathStyleModelValue()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            // Реальные значения моделей SD 1.5 из корпуса E:\output_test.
            WriteComfyPng(root, "sd15_a.png", """
                { "1": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "SD15\\realisticVisionV60B1_v51HyperVAE.safetensors" } } }
                """);
            WriteComfyPng(root, "sd15_b.png", """
                { "1": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "SD_1_5\\pornmaster_v7FP16VAE.safetensors" } } }
                """);
            var service = CreateService();

            var results = await service.FindAsync(root, Sd15ByName).ToListAsync();

            Assert.Equal(["sd15_a.png", "sd15_b.png"],
                [.. results.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal)]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Регрессия: «Qwen 2.1» (имя с пробелом и точкой) должен находить ветку Qwen Image 2.1
    // («Qwen2_1\qwen_image_2.1_...»), но не всю ветку Qwen.
    [Fact]
    public async Task QwenFamilyNameWithSpace_FindsQwenImage21Files()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            WriteComfyPng(root, "qwen21.png", """
                { "1": { "class_type": "UNETLoader", "inputs": { "unet_name": "Qwen2_1\\qwen_image_2.1_int8_convrot.safetensors" } } }
                """);
            WriteComfyPng(root, "qwen2512.png", """
                { "1": { "class_type": "UnetLoaderGGUF", "inputs": { "unet_name": "Qwen_GGUF\\qwen-image-2512-Q4_K_M.gguf" } } }
                """);
            var service = CreateService();

            var results = await service.FindAsync(root, Qwen21ByName).ToListAsync();

            Assert.Single(results, e => e.Name == "qwen21.png");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Нормализация не превращает сравнение в «совпадает со всем»: чужое имя по-прежнему
    // не находится, а pattern из одних разделителей отбрасывается (пустой pattern совпал бы любым).
    [Fact]
    public async Task FamilyPatterns_AreNotWildcards()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            WriteComfyPng(root, "sdxl.png", """
                { "1": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "sdxl_base_1.0.safetensors" } } }
                """);
            var service = CreateService();

            Assert.Empty(await service.FindAsync(root, Sd15ByName).ToListAsync());
            Assert.Empty(await service.FindAsync(root, Qwen21ByName).ToListAsync());
            Assert.Empty(await service.FindAsync(root, new ModelFamily("Разделители", ["-"])).ToListAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PngWithoutMetadata_AndKreaInNameOnly_AreSkipped()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            // PNG без ComfyUI metadata (вообще без tEXt).
            TestTree.WriteFile(root, "krea2_named.png", PngFixtureHelper.CreatePng());
            // tEXt есть, но без prompt/workflow → Status = None.
            var otherText = PngFixtureHelper.InjectTextChunk(PngFixtureHelper.CreatePng(), "Software", "Foo 1.0");
            TestTree.WriteFile(root, "other_text.png", otherText);
            // Битый PNG → per-file сбой пропускается, поиск не падает.
            TestTree.WriteFile(root, "corrupt.png", [0x12, 0x34, 0x56]);
            var service = CreateService();

            Assert.Empty(await service.FindAsync(root, Krea2).ToListAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NoMatches_ReturnsEmpty()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            const string sdxlPrompt = """
                { "1": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "sdxl_base_1.0.safetensors" } } }
                """;
            WriteComfyPng(root, "gen.png", sdxlPrompt);
            var service = CreateService();

            Assert.Empty(await service.FindAsync(root, Krea2).ToListAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Search_AlwaysRecursive_FindsRootAndSubfolders()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            WriteComfyPng(root, "krea.png", KreaUnetPromptJson);
            WriteComfyPng(Path.Combine(root, "2026-09-01"), "inner.png", KreaUnetPromptJson);
            WriteComfyPng(Path.Combine(root, "2026-09-01", "nested"), "deep.png", KreaUnetPromptJson);
            var service = CreateService();

            // Область всегда рекурсивная: файлы корня и всех вложенных папок в одном плоском списке.
            var results = await service.FindAsync(root, Krea2).ToListAsync();
            Assert.Equal(["deep.png", "inner.png", "krea.png"],
                [.. results.Select(e => e.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_BeforeEnumeration_Throws()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            WriteComfyPng(root, "gen.png", KreaUnetPromptJson);
            var service = CreateService();
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.FindAsync(root, Krea2, cts.Token).ToListAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_MidEnumeration_Stops()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            WriteComfyPng(root, "a.png", KreaUnetPromptJson);
            WriteComfyPng(root, "b.png", KreaUnetPromptJson);
            var service = CreateService();
            using var cts = new CancellationTokenSource();

            var seen = new List<ImageEntry>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var entry in service.FindAsync(root, Krea2, cts.Token))
                {
                    seen.Add(entry);
                    await cts.CancelAsync();
                }
            });

            Assert.Single(seen);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Search_SkipsJunction()
    {
        var root = TestTree.CreateTempDir();
        string? junction = null;
        try
        {
            WriteComfyPng(Path.Combine(root, "realSub"), "krea.png", KreaUnetPromptJson);
            junction = Path.Combine(root, "linkSub");

            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{Path.Combine(root, "realSub")}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null || !process.WaitForExit(10000) || process.ExitCode != 0)
            {
                return; // недоступно — тихий skip
            }

            Assert.True(Directory.Exists(junction)); // junction создан — проверка осмысленна

            var service = CreateService();
            var results = await service.FindAsync(root, Krea2).ToListAsync();

            // Junction не обходится: файл найден ровно один раз, по реальному пути.
            Assert.Single(results, e => e.Name == "krea.png");
            Assert.DoesNotContain(results, e => e.FullPath.Contains("linkSub", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (junction is not null)
            {
                try
                {
                    if (Directory.Exists(junction))
                    {
                        Directory.Delete(junction, recursive: false);
                    }
                }
                catch
                {
                    // очистка тестового артефакта — не важна для результата
                }
            }

            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Entries_HaveRealPathAndFileInfoStats()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            WriteComfyPng(root, "krea.png", KreaUnetPromptJson);
            var service = CreateService();

            var entry = Assert.Single(await service.FindAsync(root, Krea2).ToListAsync());

            var expectedPath = Path.Combine(root, "krea.png");
            Assert.Equal(expectedPath, entry.FullPath);
            Assert.DoesNotContain(@"\\?\", entry.FullPath, StringComparison.Ordinal);

            var fi = new FileInfo(expectedPath);
            Assert.Equal(fi.Length, entry.SizeBytes);
            Assert.Equal(fi.CreationTimeUtc, entry.CreatedUtc);
            Assert.Equal(fi.LastWriteTimeUtc, entry.ModifiedUtc);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NonImageFiles_AreNotReturned()
    {
        var root = TestTree.CreateTempDir();
        try
        {
            // Содержимое с «krea2» не важно: расширение не поддерживается либо не PNG.
            TestTree.WriteFile(root, "notes.txt", [0x6B, 0x72, 0x65, 0x61, 0x32]); // "krea2"
            TestTree.WriteFile(root, "clip.mp4", [0x00, 0x01, 0x6B, 0x72, 0x65, 0x61, 0x32]);
            TestTree.WriteFile(root, "photo.jpg", [0xFF, 0xD8, 0x6B, 0x72, 0x65, 0x61, 0x32]);
            var service = CreateService();

            Assert.Empty(await service.FindAsync(root, Krea2).ToListAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ModelFamilySearchService CreateService() =>
        new(new PngTextChunkReader(), new ComfyMetadataParser());

    private static void WriteComfyPng(string dir, string fileName, string promptJson)
    {
        var png = PngFixtureHelper.InjectTextChunk(PngFixtureHelper.CreatePng(), "prompt", promptJson);
        TestTree.WriteFile(dir, fileName, png);
    }
}
