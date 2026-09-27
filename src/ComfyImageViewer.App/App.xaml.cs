using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ComfyImageViewer.App.ViewModels;
using ComfyImageViewer.Core.Config;
using ComfyImageViewer.Core.Interfaces;
using ComfyImageViewer.Core.Metadata;
using ComfyImageViewer.Core.Scanning;
using ComfyImageViewer.Core.Settings;
using ComfyImageViewer.Core.Thumbnails;

namespace ComfyImageViewer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Системное отключение всплывающих подсказок (ToolTip) для ВСЕХ элементов:
        // ToolTipOpening — иницируемое FrameworkElement bubble-событие, которое
        // ToolTipService поднимает перед показом подсказки. Class-handler на
        // typeof(FrameworkElement) помечает его обработанным, поэтому WPF не
        // показывает ни одну подсказку — независимо от того, откуда задан ToolTip
        // (атрибут в XAML, binding, код, будущие элементы). Убирает белый
        // системный tooltip-стиль из тёмного интерфейса точечно, без удаления
        // атрибутов ToolTip по одному элементу. На dropdown ComboBox, меню и
        // прочие Popup-элементы не влияет — это другой механизм.
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent,
            new ToolTipEventHandler(static (_, args) => args.Handled = true));

        // TASK-05: replace fakes with real services
        IArchiveScanner scanner = new ArchiveScanner();
        IThumbnailService thumbnails = new ThumbnailService();
        IPngMetadataReader metadataReader = new PngTextChunkReader();
        IComfyMetadataParser metadataParser = new ComfyMetadataParser();
        IImageFileInfoReader fileInfoReader = new ImageFileInfoReader();
        IImageMetadataClassifier metadataClassifier = new ImageMetadataClassifier(metadataReader, metadataParser, fileInfoReader);
        IModelFamiliesStore modelFamilies = new ModelFamiliesStore();
        IModelFamilySearchService modelFamilySearch = new ModelFamilySearchService(metadataReader, metadataParser);
        IDateSearchService dateSearch = new DateSearchService();
        ISettingsService settings = new SettingsService();

        var vm = new MainViewModel(
            scanner: scanner,
            thumbnailService: thumbnails,
            metadataClassifier: metadataClassifier,
            settingsService: settings,
            modelFamiliesStore: modelFamilies,
            modelSearchService: modelFamilySearch,
            dateSearchService: dateSearch);

        var window = new MainWindow
        {
            DataContext = vm
        };

        vm.Initialize();
        window.Show();
    }
}
