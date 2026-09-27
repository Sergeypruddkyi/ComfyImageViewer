using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ComfyImageViewer.App.ViewModels;
using ComfyImageViewer.Core.Models;

namespace ComfyImageViewer.App;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainWindow()
    {
        InitializeComponent();
    }

    // Windows-like навигация: двойной клик по папке открывает её; PNG выбирается одиночным кликом.
    private void OnListMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        // Клик по пустому месту/скроллбару не должен навигировать.
        var item = ItemsControl.ContainerFromElement((ListBox)sender, source) as ListBoxItem;
        if (item is null)
            return;

        if (DataContext is MainViewModel vm && vm.OpenItemCommand.CanExecute(null))
            vm.OpenItemCommand.Execute(null);
    }

    // Drill-down «По модели» и «По дате»: WPF выполняет коммит выбора пункта закрытием
    // popup — а повторно открыть dropdown из DropDownClosed нельзя (Popup бросает
    // InvalidOperationException «нельзя переоткрыть popup в закрытии», проверено вживую).
    // Поэтому коммит перехватывается ровно на этих пунктах: mouse-up не даётся ComboBox-у
    // (e.Handled), выбор переключается программно — popup НЕ закрывается, ItemsSource уже
    // заменён на дочерний список (MainViewModel.SelectedFamily), поле показывает выбранный
    // режим. Второй клик по полю не нужен; таймеров и задержек нет. Сервисная строка
    // календаря (DateCalendarEntry) выбором не является — её клики блокируются, КРОМЕ
    // кнопок внутри календаря (CalendarDayButton, стрелки заголовка): это tunneling-хэндлер
    // Preview*, он отрабатывает ДО ButtonBase, и e.Handled здесь «съел» бы сам клик по дню.
    // Все прочие пункты (семейства, «← Поиск», «Поиск») коммутируются и закрываются штатно.
    private void OnSearchComboPreviewItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBox combo)
            return;

        if (e.OriginalSource is not DependencyObject source)
            return;

        if (ItemsControl.ContainerFromElement(combo, source) is not ComboBoxItem item)
            return;

        if (ReferenceEquals(item.DataContext, MainViewModel.DateCalendarEntry))
        {
            if (!IsCalendarButtonClick(source, item))
                e.Handled = true;
            return;
        }

        if (ReferenceEquals(item.DataContext, MainViewModel.AddFamilyEntry))
        {
            // Диалог открывается после завершения обработки клика (InvokeAsync),
            // чтобы не показывать модальное окно внутри mouse-up коммита ComboBox.
            e.Handled = true;
            if (DataContext is MainViewModel viewModel)
                Dispatcher.InvokeAsync(viewModel.AddFamily);
            return;
        }

        if (ReferenceEquals(item.DataContext, MainViewModel.ModelSearchMode) ||
            ReferenceEquals(item.DataContext, MainViewModel.DateSearchMode))
        {
            e.Handled = true;
            combo.SelectedItem = item.DataContext;
        }
    }


    // Корзина в строке семейства dropdown: подтверждение, затем удаление одной записи
    // через ModelFamiliesStore. Клик не коммитирует выбор пункта: ButtonBase съедает
    // mouse-up на бабблинге до ComboBoxItem (коммит — событие mouse-up), popup
    // остаётся открытым до модального подтверждения.
    private void OnFamilyTrashClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is FrameworkElement { DataContext: ModelFamily family } &&
            DataContext is MainViewModel viewModel)
        {
            viewModel.DeleteFamily(family);
        }
    }

    // Источник клика — кнопка внутри строки календаря или корзина семейства (любой
    // ButtonBase до строки-item)? Коммит строки при таком клике недопустим, а сам
    // клик должен дойти до ButtonBase (она съест bubbling mouse-up, и строка
    // не закоммитится).
    private static bool IsCalendarButtonClick(DependencyObject source, ComboBoxItem item)
    {
        if (source is not Visual)
            return false;

        for (DependencyObject? o = source; o is not null && !ReferenceEquals(o, item); o = VisualTreeHelper.GetParent(o))
            if (o is ButtonBase)
                return true;

        return false;
    }

    // TASK-04C-R2: тёмный нативный заголовок окна (DWMWA_USE_IMMERSIVE_DARK_MODE, fallback 19)
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int enabled = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
        }
    }
}
