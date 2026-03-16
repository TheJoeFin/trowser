namespace Trowser.Views;

public sealed partial class BrowserWindow : WinUIEx.WindowEx
{
    private BrowserPage? _browserPage;

    public BrowserWindow(string name)
    {
        InitializeComponent();

        Title = $"Trowser - {name}";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/trowser.ico"));
    }

    public void AttachBrowserPage(BrowserPage browserPage, string name)
    {
        Title = $"Trowser - {name}";
        browserPage.PrepareForWindow();

        if (!ReferenceEquals(_browserPage, browserPage))
        {
            BrowserHost.Content = null;
            _browserPage = browserPage;
            BrowserHost.Content = browserPage;
        }
    }

    public void DetachBrowserPage()
    {
        if (_browserPage == null)
        {
            return;
        }

        BrowserHost.Content = null;
        _browserPage.CloseWebView();
        _browserPage = null;
    }
}
