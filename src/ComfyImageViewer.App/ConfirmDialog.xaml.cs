using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ComfyImageViewer.App;

/// <summary>
/// Компактное подтверждение опасного действия в стиле приложения: собственный тёмный
/// диалог вместо стандартного Windows MessageBox (у того системная иконка и светлая рамка).
/// ShowDialog() == true — подтверждено, всё остальное (Esc, X, «Отмена») — отмена.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(Window? owner)
    {
        InitializeComponent();
        if (owner is not null)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    /// <summary>Текст вопроса, например «Удалить семейство «Qwen 2.1»?».</summary>
    public string Message
    {
        get => MessageText.Text;
        set => MessageText.Text = value;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;

    // Тёмный нативный заголовок окна — как в MainWindow (DWMWA_USE_IMMERSIVE_DARK_MODE, fallback 19).
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
