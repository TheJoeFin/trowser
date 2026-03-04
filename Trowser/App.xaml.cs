using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private readonly ConcurrentDictionary<Guid, BrowserWindow> _browserWindows = new();
    private Mutex? _singleInstanceMutex;
    private Views.SettingsWindow? _settingsWindow;
    private uint _nextTrayIconId = 0;

    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Trowser", "trowser-debug.log");

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
            OpenSettingsWindow();
        };
        icon.IsVisible = true;
        _trayIcons[defaultId] = icon;
        Log($"Default tray icon created and visible (TrayIconId={iconId})");
    }

    private async Task CreateTrayIconForConfigAsync(TrayBrowserConfig config)
    {
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
                args.Flyout = CreateContextMenu();
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
        Log($"RefreshTrayIconsAsync — disposing {_trayIcons.Count} existing icon(s)");
        foreach (KeyValuePair<Guid, TrayIcon> kvp in _trayIcons)
        {
            kvp.Value.Dispose();
        }
        _trayIcons.Clear();
        _nextTrayIconId = 0;

        Log("Existing icons disposed — recreating");
        await InitializeTrayIconsAsync();
        Log($"RefreshTrayIconsAsync complete — {_trayIcons.Count} icon(s) now active");
    }

    private Flyout CreateBrowserFlyout(TrayBrowserConfig config)
    {
        Log($"CreateBrowserFlyout for '{config.Name}' Url='{config.Url}'");
        BrowserPage browserPage = new();
        browserPage.Navigate(config.Url, config.Name, config.Id);
        browserPage.ViewModel.RequestPopOut = () =>
        {
            PopOutBrowser(config);
        };

        Flyout flyout = new()
        {
            Content = browserPage,
            FlyoutPresenterStyle = CreateNoPaddingStyle(),
        };

        flyout.Opened += (s, e) => Log($"Flyout opened for '{config.Name}'");
        flyout.Closing += (s, e) =>
        {
            Log($"Flyout closing for '{config.Name}'");
            if (s is Flyout f)
                f.Content = null;
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

    private void PopOutBrowser(TrayBrowserConfig config)
    {
        if (_browserWindows.TryGetValue(config.Id, out BrowserWindow? existingWindow))
        {
            existingWindow.Activate();
            return;
        }

        BrowserWindow window = new(config.Url, config.Name);
        window.Closed += (_, _) => _browserWindows.TryRemove(config.Id, out _);
        _browserWindows[config.Id] = window;
        window.Activate();
    }

    #endregion

    #region Context Menu

    private MenuFlyout CreateContextMenu()
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
        foreach (KeyValuePair<Guid, BrowserWindow> kvp in _browserWindows)
        {
            kvp.Value.Close();
        }
        _browserWindows.Clear();

        // Close settings window
        _settingsWindow?.Close();
        _settingsWindow = null;

        // Dispose all tray icons
        foreach (KeyValuePair<Guid, TrayIcon> kvp in _trayIcons)
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
