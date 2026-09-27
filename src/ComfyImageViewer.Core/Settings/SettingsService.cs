using System.IO;
using System.Text.Json;
using ComfyImageViewer.Core.Interfaces;

namespace ComfyImageViewer.Core.Settings;

public sealed class SettingsService : ISettingsService
{
    private sealed class SettingsDto
    {
        public string? RootPath { get; set; }
    }

    private readonly string _baseDir;
    private readonly string _filePath;
    private string? _rootPath;
    private bool _loaded;

    /// <param name="baseDirOverride">Каталог настроек (для тестов); null — <c>%LOCALAPPDATA%\ComfyImageViewer</c>.</param>
    public SettingsService(string? baseDirOverride = null)
    {
        _baseDir = baseDirOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComfyImageViewer");
        _filePath = Path.Combine(_baseDir, "settings.json");
    }

    public string? RootPath
    {
        get
        {
            EnsureLoaded();
            return _rootPath;
        }

        set
        {
            EnsureLoaded();
            _rootPath = value;
            Persist();
        }
    }

    /// <summary>Ленивая загрузка JSON; ошибки файловой системы не бросаются наружу.</summary>
    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            if (File.Exists(_filePath))
            {
                _rootPath = JsonSerializer.Deserialize<SettingsDto>(File.ReadAllText(_filePath))?.RootPath;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _rootPath = null;
        }
    }

    /// <summary>Сохранение при set; ошибки файловой системы не бросаются наружу.</summary>
    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(_baseDir);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(new SettingsDto { RootPath = _rootPath }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Настройки не критичны: не бросаем наружу.
        }
    }
}

