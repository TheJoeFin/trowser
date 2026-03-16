using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Trowser.Views;

public sealed partial class WelcomePage : Page
{
    public Action? RequestNavigateToBrowsers { get; set; }

    public WelcomePage()
    {
        InitializeComponent();
    }

    private void GetStartedButton_Click(object sender, RoutedEventArgs e)
    {
        RequestNavigateToBrowsers?.Invoke();
    }
}
