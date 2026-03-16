using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Trowser.Views;

public sealed partial class SettingsWindow : WinUIEx.WindowEx
{
    public SettingsWindow(bool navigateToWelcome = false)
    {
        InitializeComponent();
        Title = "Trowser";
        Width = 900;
        Height = 620;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/trowser.ico"));

        // Extend content into the title bar for a modern, borderless look.
        // Must be set in code — setting in XAML causes an error.
        ExtendsContentIntoTitleBar = true;

        // Make caption buttons transparent so Mica shows through.
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindowTitleBar titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = Colors.Transparent;
            titleBar.ButtonPressedBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        NavView.SelectionChanged += OnNavSelectionChanged;
        Activated += OnWindowActivated;

        string initialTag = navigateToWelcome ? "welcome" : "browsers";
        bool navigated = false;
        Activated += (_, _) =>
        {
            if (!navigated)
            {
                navigated = true;
                NavigateTo(initialTag);
            }
        };
    }

    public void NavigateTo(string tag)
    {
        foreach (object item in NavView.MenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag as string == tag)
            {
                NavView.SelectedItem = nvi;
                return;
            }
        }
        foreach (object item in NavView.FooterMenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag as string == tag)
            {
                NavView.SelectedItem = nvi;
                return;
            }
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            Type? pageType = tag switch
            {
                "welcome" => typeof(WelcomePage),
                "browsers" => typeof(SettingsPage),
                "about" => typeof(AboutPage),
                _ => null
            };

            if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);

                if (ContentFrame.Content is WelcomePage welcomePage)
                    welcomePage.RequestNavigateToBrowsers = () => NavigateTo("browsers");
            }
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        // Dim the title bar text when the window loses focus, matching system conventions.
        TitleBarText.Opacity = args.WindowActivationState == WindowActivationState.Deactivated
            ? 0.5
            : 1.0;
    }
}
