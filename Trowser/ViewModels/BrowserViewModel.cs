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

    public Action? RequestPopOut { get; set; }

    [RelayCommand]
    private void PopOut()
    {
        RequestPopOut?.Invoke();
    }
}
