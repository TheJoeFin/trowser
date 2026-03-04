namespace Trowser.Views;

public sealed partial class BrowserWindow : WinUIEx.WindowEx
{
    public BrowserWindow(string url, string name)
    {
        InitializeComponent();

        Title = $"Trowser - {name}";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/trowser.ico"));
        BrowserWebView.Source = new Uri(url);
    }
}
