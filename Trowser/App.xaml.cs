using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    private readonly ConcurrentDictionary<Guid, Flyout> _browserFlyouts = new();
    private readonly ConcurrentDictionary<Guid, BrowserWindow> _browserWindows = new();
    private Mutex? _singleInstanceMutex;
    private Views.SettingsWindow? _settingsWindow;
    private uint _nextTrayIconId = 0;

    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Trowser", "trowser-debug.log");

    private static Task<CoreWebView2Environment>? _sharedWebViewEnvironmentTask;

    public static Task<CoreWebView2Environment> GetSharedWebViewEnvironmentAsync()
    {
        return _sharedWebViewEnvironmentTask ??= CoreWebView2Environment
            .CreateWithOptionsAsync(
                string.Empty,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Trowser", "WebView2Data"),
                new CoreWebView2EnvironmentOptions())
            .AsTask();
    }

    internal static void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Debug.WriteLine(line);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        catch { /* never let logging break the app */ }
    }

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
                services.AddSingleton<BrowserCacheService>();
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
        Log($"UNHANDLED EXCEPTION: {e.Exception}");
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log($"OnLaunched — PID={Environment.ProcessId} thread={Environment.CurrentManagedThreadId}");

        // Single instance enforcement
        const string mutexName = "Global\\Trowser_SingleInstance_Mutex";

        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            if (!createdNew)
            {
                Log("Mutex already held — this is a second instance, exiting");
                Exit();
                return;
            }
            Log("Mutex acquired — this is the primary instance");
        }
        catch (Exception ex)
        {
            Log($"Mutex creation failed: {ex.Message} — continuing anyway");
        }

        base.OnLaunched(args);

        await GetService<IActivationService>().ActivateAsync(args);
        MainWindow.Hide();

        // Subscribe to config changes
        ITrayBrowserService trayService = GetService<ITrayBrowserService>();
        trayService.ConfigsChanged += async (_, _) =>
        {
            Log("ConfigsChanged fired — refreshing tray icons");
            await RefreshTrayIconsAsync();
        };

        Log("Starting InitializeTrayIconsAsync");
        await InitializeTrayIconsAsync();
        Log($"InitializeTrayIconsAsync complete — {_trayIcons.Count} icon(s) registered");
    }

    #region Tray Icon Management

    private async Task InitializeTrayIconsAsync()
    {
        ITrayBrowserService trayService = GetService<ITrayBrowserService>();
        List<TrayBrowserConfig> configs = await trayService.GetAllAsync();

        Log($"InitializeTrayIconsAsync — {configs.Count} config(s) found");

        if (configs.Count == 0)
        {
            Log("No configs — creating default tray icon");
            CreateDefaultTrayIcon();
        }
        else
        {
            foreach (TrayBrowserConfig config in configs)
            {
                Log($"Creating tray icon for config: Id={config.Id} Name='{config.Name}' Url='{config.Url}'");
                await CreateTrayIconForConfigAsync(config);
            }
        }
    }

    private void CreateDefaultTrayIcon()
    {
        Guid defaultId = Guid.Empty;
        uint iconId = _nextTrayIconId++;
        Log($"CreateDefaultTrayIcon — TrayIconId={iconId}");
        TrayIcon icon = new(iconId, "Assets/trowser.ico", "Trowser - Right-click for settings");
        icon.ContextMenu += (sender, args) =>
        {
            Log("Default icon ContextMenu fired");
            args.Flyout = CreateContextMenu();
        };
        icon.Selected += (sender, args) =>
        {
            Log("Default icon Selected fired — opening settings");
            OpenSettingsWindow(navigateToWelcome: true);
        };
        icon.IsVisible = true;
        _trayIcons[defaultId] = icon;
        Log($"Default tray icon created and visible (TrayIconId={iconId})");
    }

    private async Task CreateTrayIconForConfigAsync(TrayBrowserConfig config)
    {
        if (config.IsHidden)
        {
            Log($"Skipping hidden config: Id={config.Id} Name='{config.Name}'");
            return;
        }

        FaviconService faviconService = GetService<FaviconService>();

        string? iconPath = null;
        try
        {
            iconPath = await faviconService.GetIconPathAsync(config);
            Log($"Favicon result for '{config.Name}': {iconPath ?? "(null — will use default)"}");
        }
        catch (Exception ex)
        {
            Log($"Favicon fetch threw: {ex}");
        }

        iconPath ??= "Assets/trowser.ico";
        Log($"Using icon path: '{iconPath}'");

        uint iconId = _nextTrayIconId++;
        Log($"Creating TrayIcon for '{config.Name}' with TrayIconId={iconId} on thread {Environment.CurrentManagedThreadId}");

        TrayIcon icon;
        try
        {
            icon = new(iconId, iconPath, $"Trowser - {config.Name}");
        }
        catch (Exception ex)
        {
            Log($"TrayIcon constructor threw for '{config.Name}': {ex}");
            return;
        }

        icon.Selected += (sender, args) =>
        {
            Log($"Selected fired for '{config.Name}' (TrayIconId={iconId})");
            try
            {
                if (TryActivateBrowserWindow(config))
                {
                    Log($"Activated existing browser window for '{config.Name}'");
                    return;
                }

                args.Flyout = CreateBrowserFlyout(config);
                Log($"Flyout created successfully for '{config.Name}'");
            }
            catch (Exception ex)
            {
                Log($"CreateBrowserFlyout threw for '{config.Name}': {ex}");
            }
        };
        icon.ContextMenu += (sender, args) =>
        {
            Log($"ContextMenu fired for '{config.Name}' (TrayIconId={iconId})");
            try
            {
                args.Flyout = CreateContextMenu(config);
                Log($"Context menu created successfully for '{config.Name}'");
            }
            catch (Exception ex)
            {
                Log($"CreateContextMenu threw for '{config.Name}': {ex}");
            }
        };
        icon.IsVisible = true;
        _trayIcons[config.Id] = icon;
        Log($"Tray icon for '{config.Name}' created and visible (TrayIconId={iconId})");
    }

    private async Task RefreshTrayIconsAsync()
    {
        Log($"RefreshTrayIconsAsync — hiding {_trayIcons.Count} existing icon(s)");
        foreach (KeyValuePair<Guid, TrayIcon> kvp in _trayIcons)
        {
            kvp.Value.IsVisible = false;
            GC.SuppressFinalize(kvp.Value);
        }
        _trayIcons.Clear();

        Log("Existing icons hidden — recreating");
        await InitializeTrayIconsAsync();
        Log($"RefreshTrayIconsAsync complete — {_trayIcons.Count} icon(s) now active");
    }

    private Flyout CreateBrowserFlyout(TrayBrowserConfig config)
    {
        Log($"CreateBrowserFlyout for '{config.Name}' Url='{config.Url}'");
        BrowserPage browserPage = GetService<BrowserCacheService>().GetOrCreate(config);

        if (_browserFlyouts.TryRemove(config.Id, out Flyout? existingFlyout))
        {
            existingFlyout.Content = null;
            existingFlyout.Hide();
        }

        browserPage.PrepareForFlyout(config.FlyoutWidth, config.FlyoutHeight);
        browserPage.ViewModel.RequestPopOut = () =>
        {
            PopOutBrowser(config);
        };

        Flyout flyout = new()
        {
            Content = browserPage,
            FlyoutPresenterStyle = CreateNoPaddingStyle(),
        };
        _browserFlyouts[config.Id] = flyout;

        flyout.Opened += (s, e) =>
        {
            Log($"Flyout opened for '{config.Name}'");
            browserPage.Configure(config.Url, config.Name, config.Id);
        };
        flyout.Closing += (s, e) =>
        {
            Log($"Flyout closing for '{config.Name}'");
            if (_browserFlyouts.TryGetValue(config.Id, out Flyout? activeFlyout) && ReferenceEquals(activeFlyout, flyout))
            {
                _browserFlyouts.TryRemove(config.Id, out _);
            }

            if (s is Flyout f && ReferenceEquals(f.Content, browserPage))
            {
                f.Content = null;
                browserPage.CloseWebView();
            }
        };

        return flyout;
    }

    private static Style CreateNoPaddingStyle()
    {
        Style style = new(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FlyoutPresenter.CornerRadiusProperty, new CornerRadius(8)));
        return style;
    }

    private bool TryActivateBrowserWindow(TrayBrowserConfig config)
    {
        if (_browserWindows.TryGetValue(config.Id, out BrowserWindow? existingWindow))
        {
            existingWindow.Title = $"Trowser - {config.Name}";
            existingWindow.Activate();
            return true;
        }

        return false;
    }

    public void OpenBrowserWindow(TrayBrowserConfig config) => PopOutBrowser(config);

    private void PopOutBrowser(TrayBrowserConfig config)
    {
        if (TryActivateBrowserWindow(config))
        {
            return;
        }

        BrowserPage browserPage = GetService<BrowserCacheService>().GetOrCreate(config);
        if (_browserFlyouts.TryRemove(config.Id, out Flyout? existingFlyout))
        {
            existingFlyout.Content = null;
            existingFlyout.Hide();
        }

        BrowserWindow window = new(config.Name);
        window.AttachBrowserPage(browserPage, config.Name);
        browserPage.Configure(config.Url, config.Name, config.Id);
        window.Closed += (_, _) =>
        {
            window.DetachBrowserPage();
            _browserWindows.TryRemove(config.Id, out _);
        };
        _browserWindows[config.Id] = window;
        window.Activate();
    }

    #endregion

    #region Context Menu

    private MenuFlyout CreateContextMenu(TrayBrowserConfig? config = null)
    {
        MenuFlyoutItem settingsItem = new()
        {
            Command = FindCommand("SettingsCommand"),
        };
        settingsItem.Click += (_, _) => OpenSettingsWindow();

        MenuFlyoutItem closeAllItem = new()
        {
            Command = FindCommand("CloseAllCommand"),
        };
        closeAllItem.Click += (_, _) => ExitApplication();

        MenuFlyout menu = new();

        if (config != null)
        {
            MenuFlyoutItem hideItem = new() { Text = "Hide Icon" };
            FontIcon hideIcon = new FontIcon
            {
                Glyph = "\uED1A"
            };
            hideItem.Icon = hideIcon;
            hideItem.Click += async (_, _) => await HideTrayIconAsync(config);
            menu.Items.Add(hideItem);
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        menu.Items.Add(settingsItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(closeAllItem);

        return menu;
    }

    private async Task HideTrayIconAsync(TrayBrowserConfig config)
    {
        Log($"HideTrayIconAsync — hiding '{config.Name}' (Id={config.Id})");
        config.IsHidden = true;
        await GetService<ITrayBrowserService>().SaveAsync(config);
    }

    private static XamlUICommand? FindCommand(string key)
    {
        if (Current.Resources.TryGetValue(key, out object? resource) && resource is XamlUICommand cmd)
            return cmd;

        return null;
    }

    private void OpenSettingsWindow(bool navigateToWelcome = false)
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(navigateToWelcome);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    private void ExitApplication()
    {
        foreach (KeyValuePair<Guid, Flyout> kvp in _browserFlyouts)
        {
            kvp.Value.Content = null;
        }
        _browserFlyouts.Clear();

        // Close all browser windows
        foreach (KeyValuePair<Guid, BrowserWindow> kvp in _browserWindows)
        {
            kvp.Value.Close();
        }
        _browserWindows.Clear();
        GetService<BrowserCacheService>().Clear();

        // Close settings window
        _settingsWindow?.Close();
        _settingsWindow = null;

        // Hide all tray icons
        foreach (KeyValuePair<Guid, TrayIcon> kvp in _trayIcons)
        {
            kvp.Value.IsVisible = false;
            GC.SuppressFinalize(kvp.Value);
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
