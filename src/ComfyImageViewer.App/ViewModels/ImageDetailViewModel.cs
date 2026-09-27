using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.App.ViewModels;

public sealed class ImageDetailViewModel : ViewModelBase
{
    // Preview предназначен для просмотра, а не для pixel-perfect редактирования:
    // длинная сторона декодируемого bitmap ограничена этим значением.
    private const int PreviewMaxEdge = 1920;

    // «Открыть всё»: показывается, когда длинного контента достаточно много,
    // и разворачивает текст Prompt (ComfyUI) либо дополнительных metadata (ordinary).
    private const int ExpandThresholdChars = 160;

    private readonly IImageMetadataClassifier _classifier;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _loadCts;
    private BitmapImage? _previewImage;
    private string _previewErrorText = string.Empty;
    private string _dateText = string.Empty;
    private string _modelText = string.Empty;
    private string _promptText = string.Empty;
    private bool _isComfyMetadata;
    private string _fileNameText = string.Empty;
    private string _sizeText = string.Empty;
    private string _dimensionsText = string.Empty;
    private string _locationText = string.Empty;
    private string _additionalMetadataText = string.Empty;
    private string _additionalMetadataFullText = string.Empty;
    private bool _isMetadataExpanded;

    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    public string PreviewErrorText
    {
        get => _previewErrorText;
        private set => SetProperty(ref _previewErrorText, value);
    }

    // ComfyUI path: Date / Model / Prompt. Ordinary path: базовые сведения файла и изображения.
    public bool IsComfyMetadata
    {
        get => _isComfyMetadata;
        private set => SetProperty(ref _isComfyMetadata, value);
    }

    public string DateText
    {
        get => _dateText;
        private set => SetProperty(ref _dateText, value);
    }

    public string ModelText
    {
        get => _modelText;
        private set => SetProperty(ref _modelText, value);
    }

    public string PromptText
    {
        get => _promptText;
        private set => SetProperty(ref _promptText, value);
    }

    public string FileNameText
    {
        get => _fileNameText;
        private set => SetProperty(ref _fileNameText, value);
    }

    public string SizeText
    {
        get => _sizeText;
        private set => SetProperty(ref _sizeText, value);
    }

    public string DimensionsText
    {
        get => _dimensionsText;
        private set => SetProperty(ref _dimensionsText, value);
    }

    // Место съёмки из EXIF GPS (форматировано в VM); пусто, если валидного GPS нет — строка скрывается.
    public string LocationText
    {
        get => _locationText;
        private set => SetProperty(ref _locationText, value);
    }

    public bool HasLocation
    {
        get => _locationText.Length > 0;
    }

    public string AdditionalMetadataText
    {
        get => _additionalMetadataText;
        private set => SetProperty(ref _additionalMetadataText, value);
    }

    // Полный набор ordinary metadata (включая скрытые в краткой панели, например Software у JPEG).
    public string AdditionalMetadataFullText
    {
        get => _additionalMetadataFullText;
        private set => SetProperty(ref _additionalMetadataFullText, value);
    }

    public bool HasAdditionalMetadata
    {
        get => _additionalMetadataFullText.Length > 0;
    }

    // «Открыть всё»: разворачивает длинный текст (Prompt / дополнительные metadata).
    public bool IsMetadataExpanded
    {
        get => _isMetadataExpanded;
        set => SetProperty(ref _isMetadataExpanded, value);
    }

    public bool HasExpandableContent
    {
        // Скрытые в краткой панели поля (Software у JPEG) или длинный контент — повод показать кнопку.
        get => _isComfyMetadata
            ? PromptText.Length > ExpandThresholdChars
            : _additionalMetadataFullText.Length > ExpandThresholdChars ||
              !string.Equals(_additionalMetadataText, _additionalMetadataFullText, StringComparison.Ordinal);
    }

    public RelayCommand ToggleMetadataExpandedCommand { get; }

    public ImageDetailViewModel(IImageMetadataClassifier classifier, Dispatcher? dispatcher = null)
    {
        _classifier = classifier;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        ToggleMetadataExpandedCommand = new RelayCommand(_ => IsMetadataExpanded = !IsMetadataExpanded);
    }

    public void LoadImage(ImageEntry? entry)
    {
        // Быстрое переключение: предыдущая фоновая загрузка отменяется,
        // её результат не сможет перезаписать Preview нового выбора.
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        if (entry is null)
        {
            Clear();
            return;
        }

        // Декодирование preview и чтение metadata — вне UI thread;
        // результат применяется на UI thread только если выбор не сменился.
        _ = LoadAsync(entry, ct);
    }

    public void Clear()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        PreviewImage = null;
        PreviewErrorText = string.Empty;
        DateText = string.Empty;
        ModelText = string.Empty;
        PromptText = string.Empty;
        IsComfyMetadata = false;
        FileNameText = string.Empty;
        SizeText = string.Empty;
        DimensionsText = string.Empty;
        LocationText = string.Empty;
        AdditionalMetadataText = string.Empty;
        AdditionalMetadataFullText = string.Empty;
        IsMetadataExpanded = false;
    }

    private async Task LoadAsync(ImageEntry entry, CancellationToken ct)
    {
        try
        {
            var previewTask = Task.Run(() => DecodePreview(entry.FullPath), ct);
            var metadataTask = Task.Run(() => _classifier.Classify(entry.FullPath), ct);
            await Task.WhenAll(previewTask, metadataTask).ConfigureAwait(false);

            if (ct.IsCancellationRequested)
                return;

            _ = _dispatcher.BeginInvoke(() =>
            {
                if (ct.IsCancellationRequested)
                    return;

                // Замена ссылки: предыдущий bitmap становится доступным для GC.
                PreviewImage = previewTask.Result;
                PreviewErrorText = string.Empty;
                ApplyMetadata(entry, metadataTask.Result);
            });
        }
        catch (OperationCanceledException)
        {
            // Отмена при быстром переключении — штатный сценарий.
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
                return;

            _ = _dispatcher.BeginInvoke(() =>
            {
                if (ct.IsCancellationRequested)
                    return;

                PreviewImage = null;
                PreviewErrorText = "Ошибка загрузки: " + ex.Message;
            });
        }
    }

    private static BitmapImage DecodePreview(string fullPath)
    {
        var (pixelWidth, pixelHeight) = ReadPngPixelSize(fullPath);
        if (pixelWidth == 0 && pixelHeight == 0)
        {
            (pixelWidth, pixelHeight) = ReadWicPixelSize(fullPath);
        }

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;

        // Даунсэмплинг только при превышении лимита. Задаётся одна сторона —
        // вторая WIC вычисляет автоматически, пропорции сохраняются.
        // Изображения меньше лимита декодируются в исходном разрешении (без увеличения).
        if (pixelWidth > 0 && pixelHeight > 0 && Math.Max(pixelWidth, pixelHeight) > PreviewMaxEdge)
        {
            if (pixelWidth >= pixelHeight)
                bmp.DecodePixelWidth = PreviewMaxEdge;
            else
                bmp.DecodePixelHeight = PreviewMaxEdge;
        }

        bmp.UriSource = new Uri(fullPath);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static (int Width, int Height) ReadPngPixelSize(string fullPath)
    {
        // IHDR: 8 байт подписи PNG + 8 байт заголовка чанка;
        // width/height — big-endian uint32 на смещениях 16 и 20.
        var buffer = new byte[24];
        using (var stream = File.OpenRead(fullPath))
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                    return (0, 0);
                read += n;
            }
        }

        if (buffer[0] != 0x89 || buffer[1] != 0x50 || buffer[2] != 0x4E || buffer[3] != 0x47)
            return (0, 0);

        int width = (buffer[16] << 24) | (buffer[17] << 16) | (buffer[18] << 8) | buffer[19];
        int height = (buffer[20] << 24) | (buffer[21] << 16) | (buffer[22] << 8) | buffer[23];
        return (width, height);
    }

    private static (int Width, int Height) ReadWicPixelSize(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnDemand);
            var frame = decoder.Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch
        {
            return (0, 0);
        }
    }

    private void ApplyMetadata(ImageEntry entry, ClassifiedImageMetadata classified)
    {
        IsMetadataExpanded = false;

        // Приоритет основной даты — EXIF DateTimeOriginal (0x9003), та же дата съёмки, что
        // использует date-search; при отсутствии/невалидности — существующий fallback (CreatedUtc).
        // ComfyUI-path: FileInfo=null → всегда fallback (PNG без EXIF-дат — штатно).
        var exifTaken = TryParseExifDateTime(classified.FileInfo?.DateTimeOriginal);
        var dateText = exifTaken is not null
            ? exifTaken.Value.ToString("yyyy-MM-dd HH:mm:ss")
            : entry.CreatedUtc == DateTime.MinValue
                ? "недоступно"
                : entry.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss");
        DateText = dateText;

        if (classified.IsComfyMetadata)
        {
            var meta = classified.ComfyMetadata!;
            IsComfyMetadata = true;
            ModelText = meta.ModelNames.Count > 0 ? string.Join(", ", meta.ModelNames) : "недоступно";
            PromptText = meta.PromptTexts.Count > 0 ? string.Join(Environment.NewLine, meta.PromptTexts) : "недоступно";
            FileNameText = string.Empty;
            SizeText = string.Empty;
            DimensionsText = string.Empty;
            LocationText = string.Empty;
            AdditionalMetadataText = string.Empty;
            AdditionalMetadataFullText = string.Empty;
        }
        else
        {
            IsComfyMetadata = false;
            ModelText = string.Empty;
            PromptText = string.Empty;
            FileNameText = entry.Name;
            SizeText = FormatSize(entry.SizeBytes);
            DimensionsText = classified.FileInfo is { PixelWidth: > 0, PixelHeight: > 0 } info
                ? $"{info.PixelWidth} × {info.PixelHeight}"
                : "недоступно";

            // GPS EXIF — только локальные данные из файла; строка скрывается без валидного Location.
            LocationText = classified.FileInfo?.Gps is { } gps
                ? string.Format(CultureInfo.InvariantCulture, "{0:F6}, {1:F6}", gps.Latitude, gps.Longitude)
                : string.Empty;

            // Краткая панель: без полей, помеченных HiddenByDefault (Software у JPEG);
            // полный набор доступен через «Открыть всё».
            var summaryFields = classified.FileFields.Where(f => !f.HiddenByDefault).ToList();
            var summary = string.Join(Environment.NewLine, summaryFields.Select(f => $"{f.Label}: {f.Value}"));
            AdditionalMetadataText = summary.Length > 0
                ? summary
                : classified.FileFields.Count > 0 ? "Есть скрытые поля — нажмите «Открыть всё»." : string.Empty;
            AdditionalMetadataFullText = string.Join(
                Environment.NewLine,
                classified.FileFields.Select(f => $"{f.Label}: {f.Value}"));
        }

        OnPropertyChanged(nameof(HasAdditionalMetadata));
        OnPropertyChanged(nameof(HasExpandableContent));
        OnPropertyChanged(nameof(HasLocation));
    }

    /// <summary>EXIF ASCII "2022:01:30 07:35:51"; мусор/неполный формат → null (fallback).</summary>
    private static DateTime? TryParseExifDateTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return DateTime.TryParseExact(raw.Trim(),
                   ["yyyy:MM:dd HH:mm:ss", "yyyy:MM:ddTHH:mm:ss"],
                   CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) && parsed.Year > 1
            ? parsed
            : null;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Format(CultureInfo.CurrentCulture, "{0:N1} GB", (double)bytes / (1L << 30)),
        >= 1L << 20 => string.Format(CultureInfo.CurrentCulture, "{0:N1} MB", (double)bytes / (1L << 20)),
        >= 1L << 10 => string.Format(CultureInfo.CurrentCulture, "{0:N1} KB", (double)bytes / (1L << 10)),
        _ => string.Format(CultureInfo.CurrentCulture, "{0} B", bytes),
    };
}
