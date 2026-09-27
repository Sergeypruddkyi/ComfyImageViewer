using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ComfyImageViewer.App;

public partial class ModelFamilyDialog : Window
{
    private bool _initialApplied;

    public ModelFamilyDialog(Window? owner)
    {
        InitializeComponent();
        if (owner is not null)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        Loaded += OnLoaded;
    }

    public string InitialName { get; set; } = string.Empty;

    public string InitialPatterns { get; set; } = string.Empty;

    /// <summary>Запись при нажатии «Сохранить»: null — успех (диалог закрывается), иначе текст ошибки.</summary>
    public Func<string, string, string?>? SaveDraft { get; set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialApplied)
        {
            return;
        }

        _initialApplied = true;
        NameBox.Text = InitialName;
        PatternsBox.Text = InitialPatterns;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var error = SaveDraft?.Invoke(NameBox.Text, PatternsBox.Text);
        if (error is null)
        {
            DialogResult = true;
            return;
        }

        ErrorText.Text = error;
        ErrorText.Visibility = Visibility.Visible;
    }

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
