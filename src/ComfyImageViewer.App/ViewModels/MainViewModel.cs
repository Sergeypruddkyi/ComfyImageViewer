using System.IO;
using System.Windows;
using ComfyImageViewer.Core.Config;
using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Models;
using Microsoft.Win32;

namespace ComfyImageViewer.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    // Нейтральное состояние поиска: не поиск, а признак «показать обычное содержимое».
    public static readonly ModelFamily NeutralSearch = new("Поиск", []);

    // Верхний уровень ComboBox (drill-down): выбор режима поиска. «Дата» — только пункт UI,
    // реализация поиска по датам — отдельный TASK.
    public static readonly ModelFamily ModelSearchMode = new("По модели", []);
    public static readonly ModelFamily DateSearchMode = new("По дате", []);

    // «← Поиск»: первый пункт списка семейств — возврат на верхний уровень режимов.
    public static readonly ModelFamily BackToSearch = new("← Поиск", []);

    // Сервисный пункт дочернего уровня даты: в dropdown отображается компактным
    // календарём (ItemContainerStyle → DateRangeCalendar), выбором не является.
    public static readonly ModelFamily DateCalendarEntry = new("📅", []);

    // Пункт дочернего уровня «По модели»: добавление семейства через диалог.
    public static readonly ModelFamily AddFamilyEntry = new("＋ Добавить семейство...", []);

    private static readonly IReadOnlyList<ModelFamily> ModeSearchOptions =
        [NeutralSearch, ModelSearchMode, DateSearchMode];

    private readonly ISettingsService _settingsService;
    private readonly IModelFamiliesStore _families;
    private IReadOnlyList<ModelFamily> _familySearchOptions;
    private readonly IReadOnlyList<ModelFamily> _dateLevelOptions;
    private string? _currentPath;
    private string? _rootPath;
    private string _currentDirectoryText = string.Empty;
    private ModelFamily _selectedFamily = NeutralSearch;
    private IReadOnlyList<ModelFamily> _searchOptions = ModeSearchOptions;

    public MainViewModel(
        IArchiveScanner scanner,
        IThumbnailService thumbnailService,
        IImageMetadataClassifier metadataClassifier,
        ISettingsService settingsService,
        IModelFamiliesStore modelFamiliesStore,
        IModelFamilySearchService modelSearchService,
        IDateSearchService dateSearchService)
    {
        _settingsService = settingsService;
        _families = modelFamiliesStore;

        ImageList = new ImageListViewModel(scanner, thumbnailService, modelSearchService, dateSearchService);
        ImageDetail = new ImageDetailViewModel(metadataClassifier);

        // Drill-down: пока не выбран режим «По модели», ComboBox показывает только режимы;
        // после его выбора тот же список заменяется семействами из ModelFamilies.json,
        // где «← Поиск» — возврат на уровень режимов, «＋ Добавить семейство...» —
        // добавление записи (после всех семейств), «По модели» — невидимая сервисная строка.
        // «По модели» обязан остаться в новом списке (невидимой строкой, см.
        // ItemContainerStyle в MainWindow.xaml): WPF Selector не позволяет SelectedItem
        // быть объектом вне ItemsSource и сбрасывает его в null — поле пустело.
        _familySearchOptions = BuildFamilyOptions();

        // Дочерний уровень «По дате»: первым «← Поиск» (тот же принцип возврата), затем
        // сервисный пункт с календарём; «По дате» — невидимая сервисная строка, чтобы
        // SelectedItem пережил смену ItemsSource (см. комментарий выше).
        _dateLevelOptions = [BackToSearch, DateCalendarEntry, DateSearchMode];

        SelectFolderCommand = new RelayCommand(SelectFolder);
        GoUpCommand = new RelayCommand(GoUp, _ => CanGoUp());
        OpenItemCommand = new RelayCommand(OpenItem, _ => CanOpenItem());

        // TASK-04 review fix: выбор изображения в списке должен обновлять панель деталей.
        ImageList.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ImageListViewModel.SelectedItem))
                OnImageSelected();
        };
    }

    public ImageListViewModel ImageList { get; }
    public ImageDetailViewModel ImageDetail { get; }

    /// <summary>Элементы ComboBox: режимы поиска либо семейства (после выбора «Модель»).</summary>
    public IReadOnlyList<ModelFamily> SearchOptions
    {
        get => _searchOptions;
        private set
        {
            if (!SetProperty(ref _searchOptions, value))
                return;
            OnPropertyChanged(nameof(ModelRowIsHiddenServiceEntry));
            OnPropertyChanged(nameof(DateModeRowIsHiddenServiceEntry));
        }
    }

    // На уровне семейств «По модели» — техническая строка (нужна, чтобы SelectedItem
    // пережил смену ItemsSource); ItemContainerStyle скрывает её из dropdown.
    // На верхнем уровне «По модели» — обычный видимый пункт.
    public bool ModelRowIsHiddenServiceEntry => !ReferenceEquals(_searchOptions, ModeSearchOptions);

    // Аналогично на уровне даты: «По дате» остаётся элементом списка (SelectedItem
    // не должен теряться при смене ItemsSource), но в dropdown скрыта.
    public bool DateModeRowIsHiddenServiceEntry => ReferenceEquals(_searchOptions, _dateLevelOptions);

    /// <summary>
    /// Якорь первого клика дня в раунде «По дате» (второй клик = диапазон min..max).
    /// Хранится в VM, а не в календаре: popup может закрыться (light dismiss/deactivate),
    /// контейнер календаря пересоздастся, а смысл раунда обязан пережить пересоздание,
    /// пока уровень «По дате» активен. Пишает/читает только DateRangeCalendar;
    /// сбрасывается при входе в уровень, выходе из него и смене папки.
    /// </summary>
    public DateTime? DateRoundAnchor { get; set; }

    public ModelFamily SelectedFamily
    {
        get => _selectedFamily;
        set
        {
            // WPF при смене ItemsSource сбрасывает выбор ComboBox в null, и TwoWay-биндинг
            // пишет эту «пустоту» в сеттер. Это не действие пользователя, а внутренний сброс:
            // игнорируем — сеттер сам утверждает корректное значение ПОСЛЕ смены списка
            // (OnPropertyChanged ниже), поэтому поле не становится пустым.
            if (value is null)
                return;

            // «По модели»: тот же ComboBox переключается на список семейств из JSON; в поле
            // остаётся «По модели», а dropdown НЕ закрывается — сразу виден список семейств:
            // коммит выбора перехвачен в OnSearchComboPreviewItemMouseUp (MainWindow).
            if (ReferenceEquals(value, ModelSearchMode))
            {
                // Порядок: сначала backing-поле, затем смена списка (её null-запись в биндинге
                // пройдёт в guard выше), затем утверждение SelectedFamily в поле.
                DateRoundAnchor = null;
                _selectedFamily = ModelSearchMode;
                SearchOptions = _familySearchOptions;
                OnPropertyChanged(nameof(SelectedFamily));
                return;
            }

            // «← Поиск»: возврат с уровня семейств на верхний уровень режимов; отмена поиска.
            if (ReferenceEquals(value, BackToSearch))
            {
                var searchWasActive = !ReferenceEquals(_selectedFamily, NeutralSearch);

                DateRoundAnchor = null;
                _selectedFamily = NeutralSearch;
                SearchOptions = ModeSearchOptions;
                OnPropertyChanged(nameof(SelectedFamily));

                if (searchWasActive)
                {
                    // Поиск отменяется (общий CTS внутри загрузки), восстанавливается
                    // обычное содержимое папки.
                    _ = ImageList.LoadImagesAsync(CurrentPath ?? string.Empty);
                    ImageDetail.Clear();
                }
                return;
            }

            // «По дате»: дочерний уровень того же ComboBox — «← Поиск» + календарь
                // (DateCalendarEntry). Выбор дат мышью → OnCalendarSelectionChanged → автопоиск.
            if (ReferenceEquals(value, DateSearchMode))
            {
                // Новый раунд «по дате»: якорь диапазона не наследуется из прошлого.
                DateRoundAnchor = null;
                _selectedFamily = DateSearchMode;
                SearchOptions = _dateLevelOptions;
                OnPropertyChanged(nameof(SelectedFamily));
                return;
            }

            // Сервисный пункт календаря выбором не является (мышью перехватывается в
            // MainWindow; защита для клавиатурного коммита): возвращаем текущее значение.
            if (ReferenceEquals(value, DateCalendarEntry))
            {
                OnPropertyChanged(nameof(SelectedFamily));
                return;
            }

            // «＋ Добавить семейство...» выбором не является (мышью перехватывается в
            // MainWindow): открывается диалог, значение поля возвращается как есть.
            // Диалог открывается через диспетчер — модальное окно не должно стартовать
            // внутри обработчика выбора (в т.ч. при программном выборе).
            if (ReferenceEquals(value, AddFamilyEntry))
            {
                Application.Current?.Dispatcher.InvokeAsync(AddFamily);
                OnPropertyChanged(nameof(SelectedFamily));
                return;
            }

            if (!SetProperty(ref _selectedFamily, value))
                return;

            DateRoundAnchor = null;

            if (ReferenceEquals(value, NeutralSearch))
            {
                // Нейтральное состояние: поиск отменяется (общий CTS), восстанавливается
                // обычное содержимое, ComboBox возвращается к выбору режима.
                SearchOptions = ModeSearchOptions;
                _ = ImageList.LoadImagesAsync(CurrentPath ?? string.Empty);
                ImageDetail.Clear();
                return;
            }

            // Семейство из ModelFamilies.json: существующий поиск, механизм не меняется.
            // Guard: папка ещё не выбрана — только запомнить выбор, не запускать.
            if (string.IsNullOrEmpty(CurrentPath))
                return;

            // Поиск всегда рекурсивный: CurrentPath и все вложенные папки; результат —
            // единый плоский список изображений.
            ImageDetail.Clear();
            _ = ImageList.ShowModelFilterResultsAsync(CurrentPath, value);
        }
    }

    /// <summary>
    /// Выбор дат мышью в календаре дочернего уровня «По дате» (SingleRange):
    /// одна дата → поиск за день; вторая дата расширяет диапазон; поиск запускается
    /// автоматически после каждого изменения. Вызывается только из DateRangeCalendar.
    /// </summary>
    public void OnCalendarSelectionChanged(IReadOnlyList<DateTime> selectedDates)
    {
        if (!ReferenceEquals(_searchOptions, _dateLevelOptions) || selectedDates.Count == 0)
            return;

        var from = DateOnly.FromDateTime(selectedDates.Min());
        var to = DateOnly.FromDateTime(selectedDates.Max());

        // Guard: папка ещё не выбрана — поиск не запускать (как в поиске по модели).
        if (string.IsNullOrEmpty(CurrentPath))
            return;

        ImageDetail.Clear();
        _ = ImageList.ShowDateFilterResultsAsync(CurrentPath, from, to);
    }

    public void AddFamily()
    {
        var dialog = new ModelFamilyDialog(Application.Current?.MainWindow)
        {
            Title = "Добавить семейство",
        };

        dialog.SaveDraft = (name, patternsRaw) =>
        {
            try
            {
                _families.Add(name, patternsRaw);
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        };

        if (dialog.ShowDialog() == true)
        {
            RefreshFamilyOptions();
        }
    }

    public void DeleteFamily(ModelFamily family)
    {
        // Собственный тёмный диалог подтверждения вместо стандартного Windows MessageBox
        // (у того системная жёлтая иконка и светлая рамка). Логика удаления та же.
        var confirm = new ConfirmDialog(Application.Current?.MainWindow)
        {
            Title = "Удаление семейства",
            Message = $"Удалить семейство «{family.Name}»?",
        };

        if (confirm.ShowDialog() != true)
            return;

        try
        {
            _families.Remove(family.Name);
            RefreshFamilyOptions();
        }
        catch (Exception e)
        {
            ShowMessageBox(e.Message, "Ошибка удаления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private IReadOnlyList<ModelFamily> BuildFamilyOptions() =>
        [BackToSearch, .. _families.Families, AddFamilyEntry, ModelSearchMode];

    // После добавления/изменения/удаления список семейств пересобирается на лету:
    // если открыт дочерний уровень «По модели», ItemsSource заменяется новым списком,
    // SelectedItem («По модели») переживает замену и dropdown обновляется без перезапуска.
    private void RefreshFamilyOptions()
    {
        var atFamilyLevel = ReferenceEquals(_searchOptions, _familySearchOptions);
        _familySearchOptions = BuildFamilyOptions();
        if (!atFamilyLevel)
        {
            return;
        }

        SearchOptions = _familySearchOptions;
        OnPropertyChanged(nameof(SelectedFamily));
    }

    private static MessageBoxResult ShowMessageBox(
        string text, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = Application.Current?.MainWindow;
        return owner is null
            ? MessageBox.Show(text, caption, buttons, image)
            : MessageBox.Show(owner, text, caption, buttons, image);
    }

    public string? CurrentPath
    {
        get => _currentPath;
        set
        {
            if (!SetProperty(ref _currentPath, value))
                return;

            // Навигация отменяет поиск: молча сбрасываем фильтр в нейтральный (обход сеттера —
            // ниже всё равно грузится обычное содержимое, двойной LoadImagesAsync не нужен).
            // Порядок: сначала список режимов, затем «Поиск» в поле — смена ItemsSource
            // опустошила бы ComboBox, если утверждать выбор до неё.
            if (!ReferenceEquals(_selectedFamily, NeutralSearch) ||
                !ReferenceEquals(_searchOptions, ModeSearchOptions))
            {
                DateRoundAnchor = null;
                SearchOptions = ModeSearchOptions;
                _selectedFamily = NeutralSearch;
                OnPropertyChanged(nameof(SelectedFamily));
            }

            CurrentDirectoryText = value ?? string.Empty;
            _ = ImageList.LoadImagesAsync(value ?? string.Empty);
            ImageDetail.Clear();
        }
    }

    public string CurrentDirectoryText
    {
        get => _currentDirectoryText;
        private set => SetProperty(ref _currentDirectoryText, value);
    }

    public RelayCommand SelectFolderCommand { get; }
    public RelayCommand GoUpCommand { get; }
    public RelayCommand OpenItemCommand { get; }

    public void Initialize()
    {
        var saved = _settingsService.RootPath;
        if (!string.IsNullOrEmpty(saved))
        {
            _rootPath = saved;
            CurrentPath = saved;
        }
    }

    private void SelectFolder(object? parameter)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите корневую папку архива",
        };

        if (!string.IsNullOrEmpty(CurrentPath))
            dialog.InitialDirectory = CurrentPath;

        if (dialog.ShowDialog() == true)
        {
            // Новый выбор root полностью переключает навигацию.
            _rootPath = dialog.FolderName;
            CurrentPath = dialog.FolderName;
            _settingsService.RootPath = dialog.FolderName;
        }
    }

    private void GoUp(object? parameter)
    {
        if (!CanGoUp())
            return;

        var parent = Path.GetDirectoryName(CurrentPath!);
        if (parent is not null)
            CurrentPath = parent;
    }

    private bool CanGoUp()
    {
        if (string.IsNullOrEmpty(CurrentPath) || string.IsNullOrEmpty(_rootPath))
            return false;

        // Граница root: «Назад» disabled в выбранной папке и никогда не поднимается выше неё.
        return !string.Equals(
            Path.TrimEndingDirectorySeparator(CurrentPath),
            Path.TrimEndingDirectorySeparator(_rootPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private void OpenItem(object? parameter)
    {
        if (ImageList.SelectedItem is not { IsFolder: true } folder)
            return;

        CurrentPath = folder.FullPath;
    }

    private bool CanOpenItem() => ImageList.SelectedItem is { IsFolder: true };

    private void OnImageSelected()
    {
        var selected = ImageList.SelectedItem;
// Выбор PNG обновляет панель деталей; выбор папки — нет.
        if (selected is { IsImage: true } item)
            ImageDetail.LoadImage(item.Entry!);
        else
            ImageDetail.Clear();
    }
}
