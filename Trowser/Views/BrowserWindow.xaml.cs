using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Trowser.Views;

public sealed partial class BrowserWindow : WinUIEx.WindowEx
{
    public BrowserWindow(string url, string name)
    {
        InitializeComponent();

        Title = $"Trowser - {name}";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/trowser.ico"));
        BrowserWebView.CoreWebView2Initialized += OnCoreWebView2Initialized;
        BrowserWebView.NavigationCompleted += OnNavigationCompleted;
        _ = NavigateAsync(url);
    }

    private async Task NavigateAsync(string url)
    {
        var env = await App.GetSharedWebViewEnvironmentAsync();
        await BrowserWebView.EnsureCoreWebView2Async(env);
        BrowserWebView.CoreWebView2.Navigate(url);
    }

    private async void OnCoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        sender.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

        try
        {
            await sender.CoreWebView2.CallDevToolsProtocolMethodAsync(
                "Emulation.setDeviceMetricsOverride",
                """{"width":0,"height":0,"deviceScaleFactor":0,"mobile":true}""");
        }
        catch { }
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        sender.Navigate(args.Uri);
    }
    private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        BackButton.IsEnabled = sender.CanGoBack;
        ForwardButton.IsEnabled = sender.CanGoForward;
    }

    private void BackButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => BrowserWebView.GoBack();
    private void ForwardButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => BrowserWebView.GoForward();
    private void RefreshButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => BrowserWebView.Reload();
}
