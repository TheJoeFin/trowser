using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Trowser.Contracts.Services;
using Trowser.Core.Models;

namespace Trowser.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
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

    [RelayCommand]
    private void StartAdd()
    {
        EditName = string.Empty;
        EditUrl = string.Empty;
        EditIconPath = string.Empty;
        EditIconMode = IconMode.FetchFavicon;
        EditingId = null;
        IsEditing = true;
    }

    [RelayCommand]
    private void StartEdit()
    {
        if (SelectedBrowser is null) return;

        EditName = SelectedBrowser.Name;
        EditUrl = SelectedBrowser.Url;
        EditIconPath = SelectedBrowser.IconPath;
        EditIconMode = SelectedBrowser.IconMode;
        EditingId = SelectedBrowser.Id;
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
        };

        await _trayBrowserService.SaveAsync(config);
        await LoadBrowsersAsync();
        IsEditing = false;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
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
