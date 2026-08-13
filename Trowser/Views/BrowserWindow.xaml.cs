using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using System.Text;
using Trowser.Core.Models;
using Trowser.Services;
using WinUIEx;
using WinRT.Interop;

namespace Trowser.Views;

public sealed partial class BrowserWindow : WinUIEx.WindowEx
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint GA_ROOT = 2;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private readonly DispatcherQueueTimer _hideTimer;
    private readonly DispatcherQueueTimer _staleTimer;
    private BrowserPage? _browserPage;
    private TrayBrowserConfig? _config;
    private OverlappedPresenter? _presenter;
    private bool _isPinned;
    private bool _isPopupVisible;
    private bool _isShowing;
    private bool _syncingPinState;

    public bool IsPinned => _isPinned;
    public bool IsPopupVisible => _isPopupVisible;

    public BrowserWindow()
    {
        InitializeComponent();

        Title = "Trowser";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/trowser.ico"));
        ConfigurePresenter();
        ConfigureToolWindow();

        Activated += OnWindowActivated;

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(150);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => HideIfFocusLeftWindow();

        _staleTimer = DispatcherQueue.CreateTimer();
        _staleTimer.IsRepeating = false;
        _staleTimer.Tick += (_, _) => Close();
    }

    public void AttachBrowserPage(BrowserPage browserPage)
    {
        if (!ReferenceEquals(_browserPage, browserPage))
        {
            BrowserHost.Content = null;
            _browserPage = browserPage;
            BrowserHost.Content = browserPage;
        }

        browserPage.ViewModel.RequestPinChanged = OnPinnedChanged;
        browserPage.ViewModel.RequestHide = HidePopup;
        browserPage.PrepareForPopup(_isPinned);
    }

    public void UpdateConfig(TrayBrowserConfig config)
    {
        _config = config;
        Title = $"Trowser - {config.Name}";

        if (_browserPage != null)
        {
            _browserPage.PrepareForPopup(_isPinned);
            _browserPage.Configure(config.Url, config.Name, config.Id);
        }

        if (_isPopupVisible)
        {
            WindowPlacementService.ResizeToLogicalSize(this, config.FlyoutWidth, config.FlyoutHeight);
        }
    }

    public void ShowNearCursor(bool pinWindow = false)
    {
        if (_config == null)
        {
            throw new InvalidOperationException("BrowserWindow requires a config before it can be shown.");
        }

        if (pinWindow)
        {
            SetPinned(true);
        }

        // Position before showing so the window never flashes at a stale location.
        WindowPlacementService.PositionWindowNearAnchor(this, _config.FlyoutWidth, _config.FlyoutHeight);

        _hideTimer.Stop();
        _staleTimer.Stop();
        _isShowing = true;
        _isPopupVisible = true;
        Activate();
        SetForegroundWindow(WindowNative.GetWindowHandle(this));
    }

    public void HidePopup()
    {
        _hideTimer.Stop();
        _isShowing = false;

        if (!_isPopupVisible)
        {
            return;
        }

        _isPopupVisible = false;
        this.Hide();

        if (_config is { StaleTimeoutEnabled: true, StaleTimeoutMinutes: > 0 })
        {
            _staleTimer.Interval = TimeSpan.FromMinutes(_config.StaleTimeoutMinutes);
            _staleTimer.Start();
        }
    }

    public void SetPinned(bool isPinned)
    {
        if (_isPinned == isPinned && _browserPage != null)
        {
            SyncPinnedState();
            return;
        }

        _isPinned = isPinned;
        IsAlwaysOnTop = isPinned;
        UpdatePresenterForPinnedState();
        SyncPinnedState();
    }

    public void DetachBrowserPage()
    {
        if (_browserPage == null)
        {
            return;
        }

        _browserPage.ViewModel.RequestPinChanged = null;
        _browserPage.ViewModel.RequestHide = null;
        BrowserHost.Content = null;
        _browserPage.CloseWebView();
        _browserPage = null;
    }

    private void ConfigurePresenter()
    {
        _presenter = OverlappedPresenter.Create();
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        AppWindow.SetPresenter(_presenter);
        AppWindow.IsShownInSwitchers = false;
        UpdatePresenterForPinnedState();

        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        int cornerPreference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
    }

    private void UpdatePresenterForPinnedState()
    {
        if (_presenter is null)
        {
            return;
        }

        _presenter.IsResizable = _isPinned;
        _presenter.SetBorderAndTitleBar(_isPinned, _isPinned);
        ApplyFrameStyles();
    }

    /// <summary>
    /// Removes the frame the popup should not have. The presenter's
    /// SetBorderAndTitleBar(false, false) leaves the caption/resize-frame styles
    /// behind, which DWM renders as a visible white frame around the popup, so
    /// they are stripped directly in the unpinned state. The 1px DWM border is
    /// suppressed in both states.
    /// </summary>
    private void ApplyFrameStyles()
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(this);

        if (!_isPinned)
        {
            int style = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, style & ~(WS_CAPTION | WS_THICKFRAME));
        }

        int borderColor = DWMWA_COLOR_NONE;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

        // Style changes only take effect once the non-client area is recalculated.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    private void ConfigureToolWindow()
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _isShowing = false;
            _hideTimer.Stop();
            return;
        }

        if (_isShowing || _isPinned || !_isPopupVisible)
        {
            return;
        }

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideIfFocusLeftWindow()
    {
        _hideTimer.Stop();

        if (_isPinned || !_isPopupVisible)
        {
            return;
        }

        IntPtr foreground = GetForegroundWindow();
        if (ShouldRemainVisible(foreground))
        {
            return;
        }

        HidePopup();
    }

    private bool ShouldRemainVisible(IntPtr foregroundWindow)
    {
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        if (foregroundWindow == windowHandle)
        {
            return true;
        }

        if (GetAncestor(foregroundWindow, GA_ROOT) == windowHandle)
        {
            return true;
        }

        return IsMenuWindow(foregroundWindow);
    }

    private static bool IsMenuWindow(IntPtr hwnd)
    {
        StringBuilder className = new(32);
        return GetClassName(hwnd, className, className.Capacity) > 0
            && string.Equals(className.ToString(), "#32768", StringComparison.Ordinal);
    }

    private void OnPinnedChanged(bool isPinned)
    {
        if (_syncingPinState)
        {
            return;
        }

        SetPinned(isPinned);
    }

    private void SyncPinnedState()
    {
        if (_browserPage == null)
        {
            return;
        }

        _syncingPinState = true;
        _browserPage.PrepareForPopup(_isPinned);
        _syncingPinState = false;
    }

}
