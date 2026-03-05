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
        ViewModel.CanGoBack = sender.CanGoBack;
        ViewModel.CanGoForward = sender.CanGoForward;
    }

    public async void Navigate(string url, string name, Guid configId)
    {
        ViewModel.Url = url;
        ViewModel.Name = name;
        ViewModel.ConfigId = configId;
        var env = await App.GetSharedWebViewEnvironmentAsync();
        await BrowserWebView.EnsureCoreWebView2Async(env);
        BrowserWebView.CoreWebView2.Navigate(url);
    }
}
