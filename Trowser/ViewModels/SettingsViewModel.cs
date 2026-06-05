using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Trowser.Contracts.Services;
using Trowser.Core.Models;

namespace Trowser.ViewModels;

public record StaleTimeoutOption(int Minutes, string Label);

public partial class SettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<StaleTimeoutOption> StaleTimeoutOptionsSource =
    [
        new(1,   "1 minute"),
        new(5,   "5 minutes"),
        new(15,  "15 minutes"),
        new(30,  "30 minutes"),
        new(60,  "1 hour"),
        new(120, "2 hours"),
        new(240, "4 hours"),
    ];

    public IReadOnlyList<StaleTimeoutOption> StaleTimeoutOptions => StaleTimeoutOptionsSource;
    private readonly ITrayBrowserService _trayBrowserService;
    private readonly IThemeSelectorService _themeSelectorService;

    [ObservableProperty]
    private ElementTheme _elementTheme;

    [ObservableProperty]
    private ObservableCollection<TrayBrowserConfig> _browsers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideToggleLabel))]
    private TrayBrowserConfig? _selectedBrowser;

    // Form fields for add/edit
    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editUrl = string.Empty;

    [ObservableProperty]
    private string _editIconPath = string.Empty;

    [ObservableProperty]
    private double _editFlyoutWidth = 400;

    [ObservableProperty]
    private double _editFlyoutHeight = 600;

    [ObservableProperty]
    private bool _editStaleTimeoutEnabled;

    [ObservableProperty]
    private StaleTimeoutOption _editStaleTimeoutOption = StaleTimeoutOptionsSource[2]; // 30 min default

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIconModeFetchFavicon))]
    [NotifyPropertyChangedFor(nameof(IsIconModeCustomFile))]
    private IconMode _editIconMode = IconMode.FetchFavicon;

    public bool IsIconModeFetchFavicon
    {
        get => EditIconMode == IconMode.FetchFavicon;
        set { if (value) EditIconMode = IconMode.FetchFavicon; }
    }

    public bool IsIconModeCustomFile
    {
        get => EditIconMode == IconMode.CustomFile;
        set { if (value) EditIconMode = IconMode.CustomFile; }
    }

    public string HideToggleLabel => SelectedBrowser?.IsHidden == true ? "Show" : "Hide";

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private Guid? _editingId;

    public ICommand SwitchThemeCommand { get; }

    public SettingsViewModel(ITrayBrowserService trayBrowserService, IThemeSelectorService themeSelectorService)
    {
        _trayBrowserService = trayBrowserService;
        _themeSelectorService = themeSelectorService;
        _elementTheme = _themeSelectorService.Theme;

        SwitchThemeCommand = new AsyncRelayCommand<string?>(SwitchThemeAsync);

        _ = LoadBrowsersAsync();
    }

    private async Task SwitchThemeAsync(string? themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName) || !Enum.TryParse(themeName, out ElementTheme theme))
        {
            App.Log($"SwitchThemeCommand received invalid theme parameter '{themeName ?? "<null>"}'.");
            return;
        }

        if (ElementTheme != theme)
        {
            ElementTheme = theme;
            await _themeSelectorService.SetThemeAsync(theme);
        }
    }

    private async Task LoadBrowsersAsync()
    {
        List<TrayBrowserConfig> configs = await _trayBrowserService.GetAllAsync();
        Browsers = new ObservableCollection<TrayBrowserConfig>(configs);
    }

    partial void OnSelectedBrowserChanged(TrayBrowserConfig? value)
    {
        if (value is null) return;

        EditName = value.Name;
        EditUrl = value.Url;
        EditIconPath = value.IconPath;
        EditIconMode = value.IconMode;
        EditFlyoutWidth = value.FlyoutWidth;
        EditFlyoutHeight = value.FlyoutHeight;
        EditStaleTimeoutEnabled = value.StaleTimeoutEnabled;
        EditStaleTimeoutOption = StaleTimeoutOptions.FirstOrDefault(o => o.Minutes == value.StaleTimeoutMinutes)
            ?? StaleTimeoutOptions[2];
        EditingId = value.Id;
        IsEditing = true;
    }

    [RelayCommand]
    private void StartAdd()
    {
        SelectedBrowser = null;
        EditName = string.Empty;
        EditUrl = string.Empty;
        EditIconPath = string.Empty;
        EditIconMode = IconMode.FetchFavicon;
        EditFlyoutWidth = 400;
        EditFlyoutHeight = 600;
        EditStaleTimeoutEnabled = false;
        EditStaleTimeoutOption = StaleTimeoutOptions[2];
        EditingId = null;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveBrowser()
    {
        TrayBrowserConfig config = new()
        {
            Id = EditingId ?? Guid.NewGuid(),
            Name = EditName,
            Url = EditUrl,
            IconPath = EditIconPath,
            IconMode = EditIconMode,
            FlyoutWidth = Math.Clamp((int)EditFlyoutWidth, 200, 2000),
            FlyoutHeight = Math.Clamp((int)EditFlyoutHeight, 200, 2000),
            StaleTimeoutEnabled = EditStaleTimeoutEnabled,
            StaleTimeoutMinutes = EditStaleTimeoutOption.Minutes,
        };

        await _trayBrowserService.SaveAsync(config);
        await LoadBrowsersAsync();
        IsEditing = false;
        SelectedBrowser = null;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        SelectedBrowser = null;
    }

    [RelayCommand]
    private async Task DeleteBrowser()
    {
        if (SelectedBrowser is null) return;

        await _trayBrowserService.DeleteAsync(SelectedBrowser.Id);
        SelectedBrowser = null;
        await LoadBrowsersAsync();
    }

    [RelayCommand]
    private void ShowPinnedBrowser()
    {
        if (SelectedBrowser is null) return;
        ((App)Microsoft.UI.Xaml.Application.Current).OpenBrowserWindow(SelectedBrowser);
    }

    [RelayCommand]
    private async Task ToggleHideBrowser()
    {
        if (SelectedBrowser is null) return;

        Guid id = SelectedBrowser.Id;
        SelectedBrowser.IsHidden = !SelectedBrowser.IsHidden;
        await _trayBrowserService.SaveAsync(SelectedBrowser);
        await LoadBrowsersAsync();
        SelectedBrowser = Browsers.FirstOrDefault(b => b.Id == id);
    }
}
