using Microsoft.UI.Xaml.Controls;

namespace Trowser.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = GetAppVersion();
    }

    private static string GetAppVersion()
    {
        try
        {
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"Version {version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            return "Version 1.0";
        }
    }
}
