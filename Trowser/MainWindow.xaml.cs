namespace Trowser;

public sealed partial class MainWindow : WinUIEx.WindowEx
{
    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/trowser.ico"));
        Content = null;
        Title = "Trowser";
    }
}
