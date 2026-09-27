using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.App.ViewModels;

public sealed class ImageListViewModel : ViewModelBase
{
    private const int MaxConcurrentThumbnails = 4;

    private readonly IArchiveScanner _scanner;
    private readonly IThumbnailService _thumbnailService;
    private readonly IModelFamilySearchService _modelSearchService;
    private readonly IDateSearchService _dateSearchService;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _thumbnailGate = new(MaxConcurrentThumbnails, MaxConcurrentThumbnails);
    private CancellationTokenSource? _cts;
    private NavigationItem? _selectedItem;

    public ImageListViewModel(
        IArchiveScanner scanner,
        IThumbnailService thumbnailService,
        IModelFamilySearchService modelSearchService,
        IDateSearchService dateSearchService,
        Dispatcher? dispatcher = null)
    {
        _scanner = scanner;
        _thumbnailService = thumbnailService;
        _modelSearchService = modelSearchService;
        _dateSearchService = dateSearchService;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        Images = [];
    }

    public ObservableCollection<NavigationItem> Images { get; }

    public NavigationItem? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public async Task LoadImagesAsync(string path)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Images.Clear();
        SelectedItem = null;

        if (string.IsNullOrEmpty(path))
            return;

        // TASK-05 rework: перечисление архива полностью вне UI-потока;
        // синхронные секции итератора сканера не должны выполняться на UI.
        await Task.Run(async () =>
        {
            try
            {
                // Windows-like навигация: единый список — сначала папки, затем PNG текущей директории.
                await foreach (var folder in _scanner.ListSubfoldersAsync(path, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    var folderItem = new NavigationItem(folder);
                    // CollectionChanged должен приходить из UI-потока; отменённая загрузка
                    // не должна доливать устаревшие элементы в список новой операции.
                    _ = _dispatcher.BeginInvoke(() =>
                    {
                        if (!ct.IsCancellationRequested)
                            Images.Add(folderItem);
                    });
                }

                await foreach (var entry in _scanner.ListImagesAsync(path, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    var item = new NavigationItem(entry);
                    _ = LoadThumbnailAsync(item, ct);
                    _ = _dispatcher.BeginInvoke(() =>
                    {
                        if (!ct.IsCancellationRequested)
                            Images.Add(item);
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                // Silently handle scan errors — UI remains stable
            }
        }, ct);
    }

    /// <summary>
    /// Результаты поиска по модельному семейству: единый плоский список изображений
    /// (поиск всегда рекурсивный — CurrentPath и все вложенные папки; без папок
    /// и структуры каталогов), тот же thumbnail-пайплайн и выбор.
    /// Отмена — через общий <see cref="_cts"/>, поэтому навигация или новый поиск
    /// отменяют предыдущую выборку.
    /// </summary>
    public async Task ShowModelFilterResultsAsync(string rootPath, ModelFamily family)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Images.Clear();
        SelectedItem = null;

        if (string.IsNullOrEmpty(rootPath))
            return;

        await Task.Run(async () =>
        {
            try
            {
                await foreach (var entry in _modelSearchService.FindAsync(rootPath, family, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    var item = new NavigationItem(entry);
                    _ = LoadThumbnailAsync(item, ct);
                    // CollectionChanged должен приходить из UI-потока; отменённый поиск
                    // не должен доливать устаревшие элементы в список новой операции.
                    _ = _dispatcher.BeginInvoke(() =>
                    {
                        if (!ct.IsCancellationRequested)
                            Images.Add(item);
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                // Silently handle search errors — UI remains stable
            }
        }, ct);
    }

    /// <summary>
    /// Результаты поиска по дате: единый плоский список изображений (поиск всегда
    /// рекурсивный — CurrentPath и все вложенные папки; без папок и структуры каталогов),
    /// тот же thumbnail-пайплайн и выбор, что и у поиска по модели.
    /// Отмена — через общий <see cref="_cts"/>: навигация или новый поиск отменяют
    /// предыдущую выборку.
    /// </summary>
    public async Task ShowDateFilterResultsAsync(string rootPath, DateOnly from, DateOnly to)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Images.Clear();
        SelectedItem = null;

        if (string.IsNullOrEmpty(rootPath))
            return;

        await Task.Run(async () =>
        {
            try
            {
                await foreach (var entry in _dateSearchService.FindAsync(rootPath, from, to, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    var item = new NavigationItem(entry);
                    _ = LoadThumbnailAsync(item, ct);
                    // CollectionChanged должен приходить из UI-потока; отменённый поиск
                    // не должен доливать устаревшие элементы в список новой операции.
                    _ = _dispatcher.BeginInvoke(() =>
                    {
                        if (!ct.IsCancellationRequested)
                            Images.Add(item);
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                // Silently handle search errors — UI remains stable
            }
        }, ct);
    }

    private async Task LoadThumbnailAsync(NavigationItem item, CancellationToken ct)
    {
        if (item.Entry is null)
            return;

        try
        {
            await _thumbnailGate.WaitAsync(ct);

            try
            {
                var path = await _thumbnailService.GetThumbnailAsync(item.Entry, 40, ct);
                if (ct.IsCancellationRequested)
                    return;

                // Декод миниатюры вне UI-потока (EndInit синхронно открывает и декодирует файл).
                var bmp = await Task.Run(() =>
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(path ?? string.Empty);
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }, ct);

                item.Thumbnail = bmp;
            }
            finally
            {
                _thumbnailGate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            item.Thumbnail = null; // grey placeholder
        }
    }
}

public sealed class NavigationItem : ViewModelBase
{
    private BitmapImage? _thumbnail;

    public NavigationItem(FolderNode folder)
    {
        Folder = folder;
        Name = folder.Name;
    }

    public NavigationItem(ImageEntry entry)
    {
        Entry = entry;
        Name = entry.Name;
    }

    public FolderNode? Folder { get; }

    public ImageEntry? Entry { get; }

    public string Name { get; }

    public bool IsFolder => Folder is not null;

    public bool IsImage => Entry is not null;

    public string FullPath => Folder?.FullPath ?? Entry!.FullPath;

    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }
}
