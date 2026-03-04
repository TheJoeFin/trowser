using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Trowser.Contracts.Services;
using Trowser.Core.Contracts.Services;
using Trowser.Core.Models;
using Trowser.Core.Services;
using Trowser.Models;
using Trowser.Services;
using Trowser.ViewModels;
using Trowser.Views;

using WinUIEx;

using Application = Microsoft.UI.Xaml.Application;

namespace Trowser;

public partial class App : Application
{
    private readonly ConcurrentDictionary<Guid, TrayIcon> _trayIcons = new();
    private readonly ConcurrentDictionary<Guid, BrowserWindow> _browserWindows = new();
    private Mutex? _singleInstanceMutex;
    private Views.SettingsWindow? _settingsWindow;

    public IHost Host { get; }

    public static T GetService<T>() where T : class
    {
        if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");

        return service;
    }

    public static WindowEx MainWindow { get; } = new MainWindow();

    public App()
    {
        InitializeComponent();

        Host = Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices((context, services) =>
            {
                // Services
                services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
                services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
                services.AddSingleton<IActivationService, ActivationService>();
                services.AddSingleton<ITrayBrowserService, TrayBrowserService>();
                services.AddSingleton<FaviconService>();

                // Core Services
                services.AddSingleton<IFileService, FileService>();

                // ViewModels
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<BrowserViewModel>();

                // Configuration
                services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
            })
            .Build();

        UnhandledException += App_UnhandledException;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Log and suppress
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Single instance enforcement
        const string mutexName = "Global\\Trowser_SingleInstance_Mutex";

        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            if (!createdNew)
            {
                Exit();
                return;
            }
        }
        catch
        {
            // Continue even if mutex creation fails
        }

        base.OnLaunched(args);

        await GetService<IActivationService>().ActivateAsync(args);
        MainWindow.Hide();

        // Subscribe to config changes
        var trayService = GetService<ITrayBrowserService>();
        trayService.ConfigsChanged += async (_, _) => await RefreshTrayIconsAsync();

        await InitializeTrayIconsAsync();
    }

    #region Tray Icon Management

    private async Task InitializeTrayIconsAsync()
    {
        var trayService = GetService<ITrayBrowserService>();
        var configs = await trayService.GetAllAsync();

        if (configs.Count == 0)
        {
            // No browsers configured, create a default tray icon that opens settings
            CreateDefaultTrayIcon();
        }
        else
        {
            foreach (var config in configs)
            {
                await CreateTrayIconForConfigAsync(config);
            }
        }
    }

    private void CreateDefaultTrayIcon()
    {
        var defaultId = Guid.Empty;
        var icon = new TrayIcon((uint)defaultId.GetHashCode(), "Assets/trowser.ico", "Trowser - Right-click for settings");
        icon.ContextMenu += (sender, args) => args.Flyout = CreateContextMenu();
        icon.Selected += (sender, args) =>
        {
            // Open settings when no browsers are configured
            OpenSettingsWindow();
        };
        icon.IsVisible = true;
        _trayIcons[defaultId] = icon;
    }

    private async Task CreateTrayIconForConfigAsync(TrayBrowserConfig config)
    {
        var faviconService = GetService<FaviconService>();
        var iconPath = await faviconService.GetIconPathAsync(config);
        iconPath ??= "Assets/trowser.ico";

        var icon = new TrayIcon((uint)config.Id.GetHashCode(), iconPath, $"Trowser - {config.Name}");
        icon.Selected += (sender, args) => args.Flyout = CreateBrowserFlyout(config);
        icon.ContextMenu += (sender, args) => args.Flyout = CreateContextMenu();
        icon.IsVisible = true;

        _trayIcons[config.Id] = icon;
    }

    private async Task RefreshTrayIconsAsync()
    {
        // Dispose all existing icons
        foreach (var kvp in _trayIcons)
        {
            kvp.Value.Dispose();
        }
        _trayIcons.Clear();

        // Recreate from current configs
        await InitializeTrayIconsAsync();
    }

    private Flyout CreateBrowserFlyout(TrayBrowserConfig config)
    {
        var browserPage = new BrowserPage();
        browserPage.Navigate(config.Url, config.Name, config.Id);
        browserPage.ViewModel.RequestPopOut = () =>
        {
            PopOutBrowser(config);
        };

        var flyout = new Flyout
        {
            Content = browserPage,
            FlyoutPresenterStyle = CreateNoPaddingStyle(),
        };

        flyout.Closing += (s, e) =>
        {
            if (s is Flyout f)
                f.Content = null;
        };

        return flyout;
    }

    private static Style CreateNoPaddingStyle()
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(8)));
        return style;
    }

    private void PopOutBrowser(TrayBrowserConfig config)
    {
        if (_browserWindows.TryGetValue(config.Id, out var existingWindow))
        {
            existingWindow.Activate();
            return;
        }

        var window = new BrowserWindow(config.Url, config.Name);
        window.Closed += (_, _) => _browserWindows.TryRemove(config.Id, out _);
        _browserWindows[config.Id] = window;
        window.Activate();
    }

    #endregion

    #region Context Menu

    private MenuFlyout CreateContextMenu()
    {
        var settingsItem = new MenuFlyoutItem
        {
            Command = FindCommand("SettingsCommand"),
        };
        settingsItem.Click += (_, _) => OpenSettingsWindow();

        var closeAllItem = new MenuFlyoutItem
        {
            Command = FindCommand("CloseAllCommand"),
        };
        closeAllItem.Click += (_, _) => ExitApplication();

        return new MenuFlyout
        {
            Items =
            {
                settingsItem,
                new MenuFlyoutSeparator(),
                closeAllItem,
            }
        };
    }

    private static XamlUICommand? FindCommand(string key)
    {
        if (Current.Resources.TryGetValue(key, out object? resource) && resource is XamlUICommand cmd)
            return cmd;

        return null;
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    private void ExitApplication()
    {
        // Close all browser windows
        foreach (var kvp in _browserWindows)
        {
            kvp.Value.Close();
        }
        _browserWindows.Clear();

        // Close settings window
        _settingsWindow?.Close();
        _settingsWindow = null;

        // Dispose all tray icons
        foreach (var kvp in _trayIcons)
        {
            kvp.Value.Dispose();
        }
        _trayIcons.Clear();

        MainWindow.Close();
    }

    #endregion

    ~App()
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch
        {
        }
    }
}
