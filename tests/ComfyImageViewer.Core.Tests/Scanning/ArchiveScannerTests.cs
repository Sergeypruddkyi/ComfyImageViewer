using System.IO;
using ComfyImageViewer.Core.Scanning;
using Xunit;

namespace ComfyImageViewer.Core.Tests.Scanning;

public class ArchiveScannerTests
{
    /// <summary>Дерево: 2 подпапки, внутри одной — вложенная папка; png/jpg/jpeg/webp/JPG/txt/mp4.</summary>
    private static string CreateTree()
    {
        var root = TestTree.CreateTempDir();
        Directory.CreateDirectory(Path.Combine(root, "2026-09-01"));
        Directory.CreateDirectory(Path.Combine(root, "2026-09-02"));
        Directory.CreateDirectory(Path.Combine(root, "2026-09-01", "nested"));

        TestTree.WriteFile(root, "a.png", [1, 2, 3]);
        TestTree.WriteFile(root, "b.PNG", [4, 5]);
        TestTree.WriteFile(root, "c.jpg", [6]);
        TestTree.WriteFile(root, "d.txt", [7]);
        TestTree.WriteFile(root, "e.mp4", [8]);
        TestTree.WriteFile(root, "f.jpeg", [9]);
        TestTree.WriteFile(root, "g.webp", [10]);
        TestTree.WriteFile(root, "h.JPG", [11]);
        TestTree.WriteFile(Path.Combine(root, "2026-09-01"), "inner.png", [12]);
        TestTree.WriteFile(Path.Combine(root, "2026-09-01", "nested"), "deep.png", [13]);

        return root;
    }

    /// <summary>Кейс 1: только поддерживаемые изображения (.png/.jpg/.jpeg/.webp) текущей папки; подпапки перечислены; рекурсии нет.</summary>
    [Fact]
    public async Task Scanner_ListsSupportedImages_OfCurrentFolder_AndSubfolders()
    {
        var root = CreateTree();
        try
        {
            var scanner = new ArchiveScanner();

            var images = await scanner.ListImagesAsync(root).ToListAsync();
            var subfolders = await scanner.ListSubfoldersAsync(root).ToListAsync();

            Assert.Equal(
                ["a.png", "b.PNG", "c.jpg", "f.jpeg", "g.webp", "h.JPG"],
                [.. images.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(i => i.Name)]);
            Assert.All(images, i => Path.GetFileName(i.FullPath));

            Assert.Equal(["2026-09-01", "2026-09-02"], [.. subfolders.Select(f => f.Name)]);

            // Неподдерживаемые форматы не попадают в список.
            Assert.DoesNotContain(images, i => i.Name == "d.txt");
            Assert.DoesNotContain(images, i => i.Name == "e.mp4");

            // Вложенность НЕ разворачивается.
            Assert.DoesNotContain(images, i => i.Name == "inner.png");
            Assert.DoesNotContain(images, i => i.Name == "deep.png");
            Assert.DoesNotContain(subfolders, f => f.Name == "nested");

            // Записи заполнены честными данными из FileInfo.
            var a = Assert.Single(images, i => i.Name == "a.png");
            Assert.Equal(3, a.SizeBytes);
            var fi = new FileInfo(Path.Combine(root, "a.png"));
            Assert.Equal(fi.CreationTimeUtc, a.CreatedUtc);
            Assert.Equal(fi.LastWriteTimeUtc, a.ModifiedUtc);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Кейс 2: junction (reparse point) пропускается. Недоступно — тест молча завершается (skip).</summary>
    [Fact]
    public async Task Scanner_SkipsJunction()
    {
        var root = CreateTree();
        string? junction = null;
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "realSub"));
            junction = Path.Combine(root, "linkSub");

            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{target.FullName}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return; // недоступно — skip
            }

            if (!process.WaitForExit(10000) || process.ExitCode != 0)
            {
                return; // недоступно — skip
            }

            Assert.True(Directory.Exists(junction)); // junction создан — проверка осмысленна

            var scanner = new ArchiveScanner();
            var subfolders = await scanner.ListSubfoldersAsync(root).ToListAsync();

            Assert.DoesNotContain(subfolders, f => string.Equals(f.Name, "linkSub", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            // Сначала убираем саму точку репарса (junction), иначе recursive-удаление дерева падает.
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

    /// <summary>Кейс 3 (READ-ONLY аудит): сканер не пишет и не модифицирует сканируемый каталог.</summary>
    [Fact]
    public async Task Scanner_IsReadOnly_NoChangesInScannedTree()
    {
        var root = CreateTree();
        try
        {
            var before = TestTree.Snapshot(root);
            Assert.NotEmpty(before);

            var scanner = new ArchiveScanner();
            _ = await scanner.ListSubfoldersAsync(root).ToListAsync();
            _ = await scanner.ListImagesAsync(root).ToListAsync();

            var after = TestTree.Snapshot(root);

            Assert.Equal(before, after);
            Assert.Equal(before.Count, after.Count); // 0 изменений, 0 новых файлов
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
