using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using System.Text;
using Trowser.Core.Models;
using WinUIEx;
using WinRT.Interop;

namespace Trowser.Views;

public sealed partial class BrowserWindow : WinUIEx.WindowEx
{
    private const int PopupMargin = 12;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private readonly DispatcherQueueTimer _hideTimer;
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
            AppWindow.Resize(GetScaledPopupSize(config.FlyoutWidth, config.FlyoutHeight));
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

        MoveAndResizeNearCursor(_config.FlyoutWidth, _config.FlyoutHeight);
        _hideTimer.Stop();
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
    }

    private void UpdatePresenterForPinnedState()
    {
        if (_presenter is null)
        {
            return;
        }

        _presenter.IsResizable = _isPinned;
        _presenter.SetBorderAndTitleBar(_isPinned, _isPinned);
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

    private void MoveAndResizeNearCursor(int width, int height)
    {
        if (!GetCursorPos(out POINT cursor))
        {
            AppWindow.Resize(GetScaledPopupSize(width, height));
            return;
        }

        IntPtr monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        MONITORINFO monitorInfo = new()
        {
            cbSize = Marshal.SizeOf<MONITORINFO>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            AppWindow.Resize(GetScaledPopupSize(width, height));
            return;
        }

        Windows.Graphics.SizeInt32 scaledSize = GetScaledPopupSize(width, height);
        int maxWidth = Math.Max(200, monitorInfo.rcWork.Right - monitorInfo.rcWork.Left - (PopupMargin * 2));
        int maxHeight = Math.Max(200, monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top - (PopupMargin * 2));
        int popupWidth = Math.Min(scaledSize.Width, maxWidth);
        int popupHeight = Math.Min(scaledSize.Height, maxHeight);

        int preferredX = cursor.X - (popupWidth / 2);
        int preferredY = cursor.Y - (popupHeight / 2);

        int x = Math.Clamp(preferredX, monitorInfo.rcWork.Left + PopupMargin, monitorInfo.rcWork.Right - popupWidth - PopupMargin);
        int y = Math.Clamp(preferredY, monitorInfo.rcWork.Top + PopupMargin, monitorInfo.rcWork.Bottom - popupHeight - PopupMargin);

        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, popupWidth, popupHeight));
    }

    private Windows.Graphics.SizeInt32 GetScaledPopupSize(int width, int height)
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        uint dpi = GetDpiForWindow(hwnd);
        double scale = Math.Max(dpi, 96) / 96d;

        return new Windows.Graphics.SizeInt32(
            Math.Max(200, (int)Math.Round(width * scale)),
            Math.Max(200, (int)Math.Round(height * scale)));
    }
}
