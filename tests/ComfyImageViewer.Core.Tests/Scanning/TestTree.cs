using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

namespace ComfyImageViewer.Core.Tests.Scanning;

/// <summary>Хелперы тестов: временные каталоги и снимки состояния дерева файлов.</summary>
internal static class TestTree
{
    public static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "civ-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string WriteFile(string dir, string fileName, byte[] content)
    {
        var path = Path.Combine(dir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Снимок: путь → (размер, mtimeUtc, sha256) для ВСЕХ файлов дерева.</summary>
    public static Dictionary<string, string> Snapshot(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var fi = new FileInfo(file);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            result[file] = $"{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{hash}";
        }

        return result;
    }
}
