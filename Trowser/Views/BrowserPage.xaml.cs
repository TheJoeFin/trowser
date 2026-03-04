using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

using Trowser.ViewModels;

namespace Trowser.Views;

public sealed partial class BrowserPage : Page
{
    public BrowserViewModel ViewModel { get; }

    public BrowserPage()
    {
        ViewModel = new BrowserViewModel();
        InitializeComponent();
        BrowserWebView.CoreWebView2Initialized += OnCoreWebView2Initialized;
        BrowserWebView.NavigationCompleted += OnNavigationCompleted;

        ViewModel.RequestGoBack = () => BrowserWebView.GoBack();
        ViewModel.RequestGoForward = () => BrowserWebView.GoForward();
        ViewModel.RequestRefresh = () => BrowserWebView.Reload();
    }

    private async void OnCoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        await sender.CoreWebView2.CallDevToolsProtocolMethodAsync(
            "Emulation.setDeviceMetricsOverride",
            """{"width":0,"height":0,"deviceScaleFactor":0,"mobile":true}""");
    }

    private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        ViewModel.CanGoBack = sender.CanGoBack;
        ViewModel.CanGoForward = sender.CanGoForward;
    }

    public void Navigate(string url, string name, Guid configId)
    {
        ViewModel.Url = url;
        ViewModel.Name = name;
        ViewModel.ConfigId = configId;
    }
}
