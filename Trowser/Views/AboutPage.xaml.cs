using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;

namespace Trowser.Views;

public sealed partial class AboutPage : Page
{
    /// <summary>
    /// Must match the TaskId of the windows.startupTask extension in Package.appxmanifest.
    /// </summary>
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
        catch (Exception ex)
        {
            // Unpackaged runs have no startup task to query, and a TaskId that
            // does not match the manifest throws here too — log it, because the
            // only visible symptom is the toggle silently disappearing.
            App.Log($"Startup task '{StartupTaskId}' unavailable — hiding launch-at-login UI: {ex.Message}");
            StartupSection.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateStartupTaskUI()
    {
        if (_startupTask is null)
        {
            return;
        }

        _isUpdatingToggle = true;
        try
        {
            StartupTaskState state = _startupTask.State;
            LaunchAtLoginToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            LaunchAtLoginToggle.IsEnabled = state is not StartupTaskState.EnabledByPolicy
                                             and not StartupTaskState.DisabledByPolicy
                                             and not StartupTaskState.DisabledByUser;
            DisabledByUserInfo.IsOpen = state == StartupTaskState.DisabledByUser;
            ManagedByPolicyInfo.IsOpen = state is StartupTaskState.EnabledByPolicy or StartupTaskState.DisabledByPolicy;
        }
        finally
        {
            _isUpdatingToggle = false;
        }
    }

    private async void LaunchAtLoginToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingToggle || _startupTask is null)
        {
            return;
        }

        try
        {
            if (LaunchAtLoginToggle.IsOn)
            {
                // Windows can refuse the request (user disabled it in Task Manager);
                // UpdateStartupTaskUI puts the toggle back in sync either way.
                StartupTaskState state = await _startupTask.RequestEnableAsync();
                App.Log($"RequestEnableAsync returned {state}");
            }
            else
            {
                _startupTask.Disable();
            }
        }
        catch (Exception ex)
        {
            App.Log($"Toggling launch at login failed: {ex.Message}");
        }

        UpdateStartupTaskUI();
    }

    private static string GetAppVersion()
    {
        try
        {
            PackageVersion version = Package.Current.Id.Version;
            return $"Version {version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            return "Version 1.0";
        }
    }
}
