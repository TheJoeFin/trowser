using Microsoft.UI.Xaml.Controls;

using Trowser.ViewModels;

namespace Trowser.Views;

public sealed partial class BrowserPage : Page
{
    public BrowserViewModel ViewModel { get; }

    public BrowserPage()
    {
        ViewModel = new BrowserViewModel();
        InitializeComponent();
    }

    public void Navigate(string url, string name, Guid configId)
    {
        ViewModel.Url = url;
        ViewModel.Name = name;
        ViewModel.ConfigId = configId;
    }
}
