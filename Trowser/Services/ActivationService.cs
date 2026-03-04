using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Trowser.Contracts.Services;

namespace Trowser.Services;

public class ActivationService : IActivationService
{
    private readonly IThemeSelectorService _themeSelectorService;

    public ActivationService(IThemeSelectorService themeSelectorService)
    {
        _themeSelectorService = themeSelectorService;
    }

    public async Task ActivateAsync(object activationArgs)
    {
        await InitializeAsync();

        if (App.MainWindow.Content == null)
        {
            App.MainWindow.Content = new Frame();
        }

        App.MainWindow.Activate();

        await StartupAsync();
    }

    private async Task InitializeAsync()
    {
        await _themeSelectorService.InitializeAsync().ConfigureAwait(false);
    }

    private async Task StartupAsync()
    {
        await _themeSelectorService.SetRequestedThemeAsync();
    }
}
