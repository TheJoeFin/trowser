using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

using Trowser.ViewModels;

namespace Trowser.Views;

public sealed partial class BrowserPage : Page
{
    private string? _configuredUrl;
    private Task? _initializationTask;

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

    public void Configure(string url, string name, Guid configId)
    {
        ViewModel.Url = url;
        ViewModel.Name = name;
        ViewModel.ConfigId = configId;

        if (IsLoaded)
        {
            ConfigureAsync(url);
        }
        else
        {
            void OnLoaded(object sender, RoutedEventArgs e)
            {
                Loaded -= OnLoaded;
                ConfigureAsync(url);
            }
            Loaded += OnLoaded;
        }
    }

    public void PrepareForFlyout(int width, int height)
    {
        Width = width;
        Height = height;
        PopOutButton.Visibility = Visibility.Visible;
    }

    public void PrepareForWindow()
    {
        Width = double.NaN;
        Height = double.NaN;
        PopOutButton.Visibility = Visibility.Collapsed;
    }

    public void ResetNavigation()
    {
        _configuredUrl = null;
    }

    public void CloseWebView()
    {
        _configuredUrl = null;
        _initializationTask = null;

        try
        {
            BrowserWebView.Close();
        }
        catch { }
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

    private async void ConfigureAsync(string url)
    {
        await EnsureWebViewInitializedAsync();

        if (string.Equals(_configuredUrl, url, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _configuredUrl = url;
        BrowserWebView.CoreWebView2.Navigate(url);
    }

    private async Task EnsureWebViewInitializedAsync()
    {
        if (BrowserWebView.CoreWebView2 != null)
        {
            return;
        }

        _initializationTask ??= InitializeWebViewAsync();
        await _initializationTask;
    }

    private async Task InitializeWebViewAsync()
    {
        var env = await App.GetSharedWebViewEnvironmentAsync();
        await BrowserWebView.EnsureCoreWebView2Async(env);
    }
}
