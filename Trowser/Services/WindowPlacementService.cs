using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinUIEx;

namespace Trowser.Services;

/// <summary>
/// Places borderless popup windows near the tray icon that opened them.
/// Ported from Trdo so both tray apps share the same placement rules.
/// </summary>
internal static partial class WindowPlacementService
{
    private const int WindowMargin = 12;

    /// <summary>
    /// Smallest popup we will ever place. Configs are clamped to 200-2000 when
    /// saved from settings, but TrayBrowsers.json is deserialized without
    /// validation, so a hand-edited or truncated file could otherwise ask for a
    /// zero-size (invisible, unrecoverable) window.
    /// </summary>
    private const int MinimumPopupSize = 200;

    private static PointInt32? _lastAnchorPoint;
    private static nint _trayIconWindowHandle;
    private static uint _trayIconId;
    private static bool _hasTrayIconSource;

    public static void CapturePointerAnchor()
    {
        // Always drop the previous anchor first. If GetCursorPos fails (it does
        // when the calling thread is not on the input desktop, e.g. across a
        // UAC/secure-desktop transition) the last popup's cursor position must
        // not be silently reused — fall through to the tray-icon/taskbar
        // fallbacks in GetAnchorPoint instead.
        _lastAnchorPoint = null;

        if (GetCursorPos(out POINT point))
        {
            _lastAnchorPoint = new PointInt32(point.X, point.Y);
        }
    }

    /// <summary>
    /// Records which tray icon should anchor the next popup. Trowser has one
    /// icon per config, so this is set from the icon's own click handler
    /// immediately before the window is shown.
    /// </summary>
    public static void SetTrayIconSource(TrayIcon trayIcon)
    {
        if (TryGetTrayIconWindowHandle(trayIcon, out nint hwnd))
        {
            _trayIconWindowHandle = hwnd;
            _trayIconId = trayIcon.TrayIconId;
            _hasTrayIconSource = true;
        }
        else
        {
            _hasTrayIconSource = false;
            App.Log("WindowPlacementService: no tray icon window handle; falling back to pointer placement");
        }
    }

    /// <summary>
    /// Forgets the tray icon anchor, e.g. when a window is opened from settings
    /// rather than from an icon, so placement falls back to the pointer.
    /// </summary>
    public static void ClearTrayIconSource() => _hasTrayIconSource = false;

    public static void PositionWindowNearAnchor(Window window, int width, int height)
    {
        bool usePointerPlacement = _lastAnchorPoint is PointInt32;
        PointInt32 anchor = GetAnchorPoint();
        DisplayArea? displayArea = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Nearest);
        RectInt32 workArea = displayArea?.WorkArea ?? DisplayArea.Primary.WorkArea;

        // Win32 and WinUI positioning APIs all use physical pixels. Scale the
        // caller's logical width/height so placement and clamping are correct
        // at any DPI (125%, 150%, 200%, etc.). The DPI must come from the
        // anchor's monitor, not the window: a hidden window keeps the DPI of
        // wherever it last was, which goes stale across monitor/scale changes.
        uint dpi = GetDpiForAnchor(anchor, window);
        int physWidth = ToPhysical(Math.Max(width, MinimumPopupSize), dpi);
        int physHeight = ToPhysical(Math.Max(height, MinimumPopupSize), dpi);

        // Trowser-specific: popup size is user-configurable (up to 2000 logical
        // px), so a popup can be larger than the display it opens on. Shrink to
        // the work area rather than letting it hang off the screen edge.
        physWidth = Math.Min(physWidth, workArea.Width);
        physHeight = Math.Min(physHeight, workArea.Height);

        // The gap between the anchor and the popup is a logical measure like the
        // extents above, so it has to be scaled too — a raw 12px offset collapses
        // to 6 DIP at 200% and 4 DIP at 300%.
        int margin = ToPhysical(WindowMargin, dpi);

        int x;
        int y;

        // When the pointer is over the tray icon, center on the icon rather than
        // offset from the cursor — matches native Windows tray flyout behavior.
        bool trayIconAvailable = TryGetTrayIconRect(out RECT iconRect);
        bool pointerIsOverTrayIcon = usePointerPlacement && trayIconAvailable
            && anchor.X >= iconRect.Left && anchor.X <= iconRect.Right
            && anchor.Y >= iconRect.Top && anchor.Y <= iconRect.Bottom;

        if (usePointerPlacement && !pointerIsOverTrayIcon)
        {
            bool placeLeft = anchor.X >= workArea.X + (workArea.Width / 2);
            bool placeAbove = anchor.Y >= workArea.Y + (workArea.Height / 2);

            x = placeLeft ? anchor.X - physWidth - margin : anchor.X + margin;
            y = placeAbove ? anchor.Y - physHeight - margin : anchor.Y + margin;
        }
        else if (trayIconAvailable)
        {
            int iconCenterX = (iconRect.Left + iconRect.Right) / 2;
            x = iconCenterX - (physWidth / 2);

            if (TryGetTaskbarRect(out RECT taskbarRect, out uint taskbarEdge))
            {
                y = taskbarEdge switch
                {
                    ABE_BOTTOM => taskbarRect.Top - physHeight,
                    ABE_TOP => taskbarRect.Bottom,
                    _ => iconRect.Top >= workArea.Y + (workArea.Height / 2)
                        ? iconRect.Top - physHeight - margin
                        : iconRect.Bottom + margin
                };
            }
            else
            {
                bool iconOnBottomHalf = iconRect.Top >= workArea.Y + (workArea.Height / 2);
                y = iconOnBottomHalf
                    ? iconRect.Top - physHeight - margin
                    : iconRect.Bottom + margin;
            }
        }
        else if (TryGetTaskbarRect(out RECT taskbarRect, out uint taskbarEdge))
        {
            bool isRtl = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
            int taskbarMidY = taskbarRect.Top + ((taskbarRect.Bottom - taskbarRect.Top) / 2);

            x = taskbarEdge switch
            {
                ABE_BOTTOM or ABE_TOP => isRtl
                    ? taskbarRect.Left + margin
                    : taskbarRect.Right - margin - physWidth,
                _ => workArea.X + (workArea.Width / 2) - (physWidth / 2)
            };

            y = taskbarEdge switch
            {
                ABE_BOTTOM => taskbarRect.Top - physHeight,
                ABE_TOP => taskbarRect.Bottom,
                ABE_LEFT => taskbarMidY - (physHeight / 2),
                ABE_RIGHT => taskbarMidY - (physHeight / 2),
                _ => taskbarRect.Top - physHeight
            };

            if (taskbarEdge is ABE_LEFT or ABE_RIGHT)
            {
                x = taskbarEdge == ABE_LEFT
                    ? taskbarRect.Right + margin
                    : taskbarRect.Left - physWidth - margin;
            }
        }
        else
        {
            bool placeAbove = anchor.Y >= workArea.Y + (workArea.Height / 2);

            x = anchor.X - (physWidth / 2);
            y = placeAbove ? anchor.Y - physHeight - margin : anchor.Y + margin;
        }

        int maxX = Math.Max(workArea.X, workArea.X + workArea.Width - physWidth);
        int maxY = Math.Max(workArea.Y, workArea.Y + workArea.Height - physHeight);

        x = Math.Clamp(x, workArea.X, maxX);
        y = Math.Clamp(y, workArea.Y, maxY);

        window.AppWindow.MoveAndResize(new RectInt32(x, y, physWidth, physHeight));
    }

    /// <summary>
    /// Resizes an already-visible window to a logical size, using the DPI of
    /// the monitor the window is currently on.
    /// </summary>
    public static void ResizeToLogicalSize(Window window, int width, int height)
    {
        uint dpi = GetDpiForWindow(window.GetWindowHandle());
        if (dpi == 0)
        {
            dpi = 96;
        }

        window.AppWindow.Resize(new SizeInt32(
            ToPhysical(Math.Max(width, MinimumPopupSize), dpi),
            ToPhysical(Math.Max(height, MinimumPopupSize), dpi)));
    }

    /// <summary>
    /// Converts a logical (DIP) extent to physical pixels, rounding *up*. A
    /// window sized from measured content must never end up a fraction of a
    /// pixel shorter than that content: at fractional scales (125%, 150%) a
    /// truncating cast loses up to a pixel, and windows that hug their content
    /// pay for it with clipped text.
    /// </summary>
    private static int ToPhysical(int logical, uint dpi) =>
        (int)Math.Ceiling(logical * dpi / 96.0);

    private static uint GetDpiForAnchor(PointInt32 anchor, Window window)
    {
        POINT point = new() { X = anchor.X, Y = anchor.Y };
        nint monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        if (monitor != 0
            && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
            && dpiX != 0)
        {
            return dpiX;
        }

        uint dpi = GetDpiForWindow(window.GetWindowHandle());
        return dpi == 0 ? 96u : dpi;
    }

    private static PointInt32 GetAnchorPoint()
    {
        if (_lastAnchorPoint is PointInt32 anchor)
        {
            return anchor;
        }

        if (TryGetTrayIconAnchorPoint(out anchor))
        {
            return anchor;
        }

        if (TryGetTaskbarAnchorPoint(out anchor))
        {
            return anchor;
        }

        RectInt32 workArea = DisplayArea.Primary.WorkArea;
        return new PointInt32(
            workArea.X + (workArea.Width / 2),
            workArea.Y + workArea.Height - WindowMargin);
    }

    private static bool TryGetTrayIconAnchorPoint(out PointInt32 anchor)
    {
        if (!TryGetTrayIconRect(out RECT iconRect))
        {
            anchor = default;
            return false;
        }

        anchor = new PointInt32(
            (iconRect.Left + iconRect.Right) / 2,
            (iconRect.Top + iconRect.Bottom) / 2);
        return true;
    }

    private static bool TryGetTrayIconRect(out RECT iconRect)
    {
        iconRect = default;

        if (!_hasTrayIconSource || _trayIconWindowHandle == 0)
        {
            return false;
        }

        NOTIFYICONIDENTIFIER identifier = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _trayIconWindowHandle,
            uID = _trayIconId,
        };

        return Shell_NotifyIconGetRect(ref identifier, out iconRect) == 0;
    }

    private static bool TryGetTrayIconWindowHandle(TrayIcon trayIcon, out nint hwnd)
    {
        hwnd = 0;

        try
        {
            FieldInfo? field = typeof(TrayIcon).GetField(
                "_windowHandle",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (field?.GetValue(trayIcon) is nint handle && handle != 0)
            {
                hwnd = handle;
                return true;
            }

            App.Log($"WindowPlacementService: TrayIcon._windowHandle unavailable (field found: {field is not null})");
        }
        catch (Exception ex)
        {
            // WinUIEx internals may change across versions
            App.Log($"WindowPlacementService: failed to read TrayIcon._windowHandle: {ex}");
        }

        return false;
    }

    private static bool TryGetTaskbarRect(out RECT rc, out uint edge)
    {
        APPBARDATA appBarData = new()
        {
            cbSize = Marshal.SizeOf<APPBARDATA>()
        };

        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref appBarData) == 0)
        {
            rc = default;
            edge = 0;
            return false;
        }

        rc = appBarData.rc;
        edge = appBarData.uEdge;
        return true;
    }

    private static bool TryGetTaskbarAnchorPoint(out PointInt32 anchor)
    {
        if (!TryGetTaskbarRect(out RECT taskbarRect, out uint edge))
        {
            anchor = default;
            return false;
        }

        bool isRtl = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
        int taskbarMidY = taskbarRect.Top + ((taskbarRect.Bottom - taskbarRect.Top) / 2);

        anchor = edge switch
        {
            ABE_BOTTOM => new PointInt32(
                isRtl ? taskbarRect.Left + WindowMargin : taskbarRect.Right - WindowMargin,
                taskbarMidY),
            ABE_TOP => new PointInt32(
                isRtl ? taskbarRect.Left + WindowMargin : taskbarRect.Right - WindowMargin,
                taskbarRect.Bottom - WindowMargin),
            ABE_LEFT => new PointInt32(
                taskbarRect.Right - WindowMargin,
                taskbarRect.Bottom - WindowMargin),
            ABE_RIGHT => new PointInt32(
                taskbarRect.Left + WindowMargin,
                taskbarRect.Bottom - WindowMargin),
            _ => new PointInt32(
                isRtl ? taskbarRect.Left + WindowMargin : taskbarRect.Right - WindowMargin,
                taskbarRect.Bottom - WindowMargin)
        };

        return true;
    }

    private const uint ABM_GETTASKBARPOS = 0x00000005;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint MDT_EFFECTIVE_DPI = 0;
    private const uint ABE_LEFT = 0;
    private const uint ABE_TOP = 1;
    private const uint ABE_RIGHT = 2;
    private const uint ABE_BOTTOM = 3;

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
    private struct NOTIFYICONIDENTIFIER
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint hMonitor, uint dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("shell32.dll")]
    private static partial uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);
}
