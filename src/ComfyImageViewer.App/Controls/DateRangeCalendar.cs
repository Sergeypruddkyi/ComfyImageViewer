using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ComfyImageViewer.App.ViewModels;

namespace ComfyImageViewer.App.Controls;

/// <summary>
/// Компактный календарь в dropdown-уровне «По дате»: прокидывает выбор мышью
/// в MainViewModel. WPF Calendar на обычный второй клик ЗАМЕНЯЕТ выделение
/// (нативный диапазон — только drag/Shift), поэтому «вторая дата → диапазон
/// min..max» реализовано так: первый клик раунда запоминается якорем в VM
/// (DateRoundAnchor — он переживает пересоздание контейнера календаря при
/// переоткрытии popup), второй клик разворачивается программно в
/// min..max. Дочерний уровень — тот же ComboBox, «← Поиск» — первый пункт.
/// </summary>
public sealed class DateRangeCalendar : Calendar
{
    private bool _suppress;

    public DateRangeCalendar()
    {
        SelectionMode = CalendarSelectionMode.SingleRange;
    }

    protected override void OnSelectedDatesChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectedDatesChanged(e);

        ReleaseStaleCapture();

        if (_suppress)
            return;

        // VM прокинут в DataContext через binding в ContentTemplate (MainWindow.xaml):
        // визуальный подъём до ComboBox невозможен — контент popup живёт в отдельном
        // visual tree/HWND, и цепочка VisualTreeHelper.GetParent обрывается на popup root.
        var vm = DataContext as MainViewModel;

        if (SelectedDates.Count == 0)
            return;

        if (SelectedDates.Count > 1)
        {
            // Диапазон родом из самого Calendar (drag) — раунд завершён.
            if (vm is not null)
                vm.DateRoundAnchor = null;
            vm?.OnCalendarSelectionChanged(SelectedDates.ToList());
            return;
        }

        var day = SelectedDates[0];

        if (vm?.DateRoundAnchor is DateTime anchor && anchor.Date != day.Date)
        {
            // Второй клик раунда (календарь мог быть пересоздан между кликами):
            // разворачиваем {day} в диапазон anchor..day.
            vm.DateRoundAnchor = null;
            _suppress = true;
            try
            {
                SelectedDates.Clear();
                SelectedDates.AddRange(anchor <= day ? anchor : day, anchor >= day ? anchor : day);
            }
            finally
            {
                _suppress = false;
            }

            vm.OnCalendarSelectionChanged(SelectedDates.ToList());
            return;
        }

        // Первый клик раунда (или повтор клика по якорному дню).
        if (vm is not null)
            vm.DateRoundAnchor = day;
        vm?.OnCalendarSelectionChanged(SelectedDates.ToList());
    }

    // Дни — ButtonBase, они съедают bubbling mouse-up раньше штатного релиза
    // календаря; оставшийся capture на CalendarItem перенаправляет последующие
    // клики («← Поиск», строки dropdown) в CalendarItem. Снимаем асинхронно:
    // синхронный релиз внутри этого события сбивает обработку текущего клика.
    private void ReleaseStaleCapture()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (Mouse.Captured is CalendarItem)
                Mouse.Captured.ReleaseMouseCapture();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }
}
