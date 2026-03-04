namespace Trowser.Views;

public sealed partial class SettingsWindow : WinUIEx.WindowEx
{
    public SettingsWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/trowser.ico"));
    }
}
