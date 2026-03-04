using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Trowser.ViewModels;

public partial class BrowserViewModel : ObservableObject
{
    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private Guid _configId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool _canGoBack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    private bool _canGoForward;

    public Action? RequestPopOut { get; set; }
    public Action? RequestGoBack { get; set; }
    public Action? RequestGoForward { get; set; }
    public Action? RequestRefresh { get; set; }

    [RelayCommand]
    private void PopOut() => RequestPopOut?.Invoke();

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => RequestGoBack?.Invoke();

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => RequestGoForward?.Invoke();

    [RelayCommand]
    private void Refresh() => RequestRefresh?.Invoke();
}
