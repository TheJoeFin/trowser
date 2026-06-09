using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;

namespace Trowser.Views;

public sealed partial class AboutPage : Page
{
    private const string StartupTaskId = "TrowserStartupTask";
    private StartupTask? _startupTask;
    private bool _isUpdatingToggle;

    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = GetAppVersion();
        _ = InitializeStartupTaskAsync();
    }

    private async Task InitializeStartupTaskAsync()
    {
        try
        {
            _startupTask = await StartupTask.GetAsync(StartupTaskId);
            UpdateStartupTaskUI();
        }
        catch
        {
            StartupSection.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateStartupTaskUI()
    {
        if (_startupTask is null)
            return;

        _isUpdatingToggle = true;
        try
        {
            var state = _startupTask.State;
            LaunchAtLoginToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            LaunchAtLoginToggle.IsEnabled = state is not StartupTaskState.EnabledByPolicy
                                             and not StartupTaskState.DisabledByPolicy
                                             and not StartupTaskState.DisabledByUser;
            DisabledByUserInfo.IsOpen = state == StartupTaskState.DisabledByUser;
        }
        finally
        {
            _isUpdatingToggle = false;
        }
    }

    private async void LaunchAtLoginToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingToggle || _startupTask is null)
            return;

        if (LaunchAtLoginToggle.IsOn)
            await _startupTask.RequestEnableAsync();
        else
            _startupTask.Disable();

        UpdateStartupTaskUI();
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