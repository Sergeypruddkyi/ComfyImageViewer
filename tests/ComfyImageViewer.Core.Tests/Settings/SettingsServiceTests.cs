using System.IO;
using ComfyImageViewer.Core.Settings;
using Xunit;

namespace ComfyImageViewer.Core.Tests.Settings;

public class SettingsServiceTests
{
    /// <summary>Кейс 5: set → файл существует → новый экземпляр с тем же baseDir читает значение.</summary>
    [Fact]
    public void Set_PersistsFile_AndNewInstanceReads()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "civ-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new SettingsService(baseDir);
            first.RootPath = @"C:\archive\comfy";

            Assert.True(Directory.Exists(baseDir));
            Assert.True(File.Exists(Path.Combine(baseDir, "settings.json")));

            var second = new SettingsService(baseDir);
            Assert.Equal(@"C:\archive\comfy", second.RootPath);
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }

    /// <summary>baseDirOverride работает: разные каталоги изолированы.</summary>
    [Fact]
    public void BaseDirOverride_IsolatesInstances()
    {
        var dir1 = Path.Combine(Path.GetTempPath(), "civ-tests-" + Guid.NewGuid().ToString("N"));
        var dir2 = Path.Combine(Path.GetTempPath(), "civ-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s1 = new SettingsService(dir1) { RootPath = @"C:\one" };
            var s2 = new SettingsService(dir2) { RootPath = @"C:\two" };

            Assert.Equal(@"C:\one", new SettingsService(dir1).RootPath);
            Assert.Equal(@"C:\two", new SettingsService(dir2).RootPath);

            Assert.False(File.Exists(Path.Combine(dir2, "settings.json")) && File.ReadAllText(Path.Combine(dir2, "settings.json")).Contains("one"));
        }
        finally
        {
            if (Directory.Exists(dir1))
            {
                Directory.Delete(dir1, recursive: true);
            }

            if (Directory.Exists(dir2))
            {
                Directory.Delete(dir2, recursive: true);
            }
        }
    }

    /// <summary>Ленивая загрузка: до обращения файл не читается/не создаётся; битый JSON не бросает.</summary>
    [Fact]
    public void CorruptJson_DoesNotThrow_ReturnsNull()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "civ-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(baseDir);
            File.WriteAllText(Path.Combine(baseDir, "settings.json"), "{ not valid json !");

            var service = new SettingsService(baseDir);
            Assert.Null(service.RootPath);
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }

    /// <summary>set(null) сохраняется и читается как null.</summary>
    [Fact]
    public void SetNull_ReadsBackAsNull()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "civ-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            new SettingsService(baseDir).RootPath = @"C:\temp";
            var s = new SettingsService(baseDir) { RootPath = null };
            Assert.Null(s.RootPath);
            Assert.Null(new SettingsService(baseDir).RootPath);
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, recursive: true);
            }
        }
    }
}
