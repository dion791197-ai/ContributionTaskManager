using System.Runtime.InteropServices;

namespace GitHubGoal.Interop;

/// <summary>
/// The Win32 calls WinUI 3 does not surface: rounded corners, click-through-free
/// dragging, and monitor geometry for keeping the widget on screen.
/// </summary>
internal static class NativeWindow
{
    // --- DWM ---------------------------------------------------------------
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    // --- monitors ----------------------------------------------------------
    private const int MONITOR_DEFAULTTONEAREST = 2;

    // --- borderless resize -------------------------------------------------
    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private static readonly UIntPtr ResizeSubclassId = new(1);

    /// <summary>
    /// Keeps subclass delegates alive for the lifetime of their window. Without this
    /// the GC would collect them and Windows would call into freed memory.
    /// </summary>
    private static readonly Dictionary<IntPtr, SubclassProc> ResizeSubclasses = [];

    private delegate IntPtr SubclassProc(
        IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr subclassId, IntPtr refData);

    /// <summary>
    /// Opts the window into Windows 11's rounded corner geometry. A no-op on
    /// Windows 10, where the call simply fails.
    /// </summary>
    public static void ApplyRoundedCorners(IntPtr hwnd)
    {
        var preference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    /// <summary>
    /// Scale factor for the monitor the window is on (1.0 at 96 DPI).
    ///
    /// Used instead of XamlRoot.RasterizationScale because XamlRoot is still null while
    /// the window is being constructed. Reading 1.0 there and the real scale later made
    /// the saved size shrink by the scale factor on every restart.
    /// </summary>
    public static double ScaleFor(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1d : dpi / 96d;
    }

    /// <summary>
    /// Cursor position in physical screen pixels — the same space AppWindow.Move uses,
    /// so dragging stays correct across monitors with different scaling.
    /// </summary>
    public static (int X, int Y) CursorPosition()
    {
        if (GetCursorPos(out var point))
        {
            return (point.X, point.Y);
        }

        return (0, 0);
    }

    /// <summary>
    /// Restores edge resizing on a window that has no Win32 border.
    ///
    /// SetBorderAndTitleBar(hasBorder: false, ...) is what removes the visible frame,
    /// but it also removes the non-client edges Windows would normally hit-test for
    /// resizing. Subclassing WM_NCHITTEST puts the resize edges back without drawing
    /// any chrome: the grab band sits just inside the window, over our own content.
    /// </summary>
    /// <param name="hwnd">Target window.</param>
    /// <param name="grabThickness">Width of the grab band in logical pixels.</param>
    public static void EnableBorderlessResize(IntPtr hwnd, int grabThickness = 6)
    {
        var scale = ScaleFor(hwnd);
        var band = Math.Max(4, (int)Math.Round(grabThickness * scale));

        // The delegate must outlive this call; the subclass holds a raw function
        // pointer and Windows will call it long after we return.
        var callback = new SubclassProc((h, msg, wParam, lParam, _, _) =>
        {
            // Collapse the non-client area into nothing, so the client area is the whole
            // window. Without this Windows still paints a caption strip along the top
            // edge even with hasTitleBar: false, which shows as a light bar above the card.
            if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (msg != WM_NCHITTEST)
            {
                return DefSubclassProc(h, msg, wParam, lParam);
            }

            if (!GetWindowRect(h, out var rect))
            {
                return DefSubclassProc(h, msg, wParam, lParam);
            }

            // lParam packs screen coordinates as two signed 16-bit values.
            var x = unchecked((short)(long)lParam);
            var y = unchecked((short)((long)lParam >> 16));

            var onLeft = x < rect.Left + band;
            var onRight = x >= rect.Right - band;
            var onTop = y < rect.Top + band;
            var onBottom = y >= rect.Bottom - band;

            var hit = (onLeft, onRight, onTop, onBottom) switch
            {
                (true, _, true, _) => HTTOPLEFT,
                (_, true, true, _) => HTTOPRIGHT,
                (true, _, _, true) => HTBOTTOMLEFT,
                (_, true, _, true) => HTBOTTOMRIGHT,
                (true, _, _, _) => HTLEFT,
                (_, true, _, _) => HTRIGHT,
                (_, _, true, _) => HTTOP,
                (_, _, _, true) => HTBOTTOM,
                _ => 0,
            };

            // Anywhere else falls through so XAML keeps receiving pointer input,
            // which is what drives dragging and the header buttons.
            return hit != 0 ? hit : DefSubclassProc(h, msg, wParam, lParam);
        });

        if (SetWindowSubclass(hwnd, callback, ResizeSubclassId, IntPtr.Zero))
        {
            ResizeSubclasses[hwnd] = callback;
        }
    }

    /// <summary>The work area (excludes the taskbar) of the monitor nearest a point.</summary>
    public static RECT WorkAreaForPoint(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        return WorkArea(monitor);
    }

    /// <summary>The work area of the primary monitor, used when nothing else is known.</summary>
    public static RECT PrimaryWorkArea()
    {
        var monitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTONEAREST);
        return WorkArea(monitor);
    }

    private static RECT WorkArea(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            return info.rcWork;
        }

        // Fall back to a conservative desktop-sized rectangle.
        return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;

        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id, IntPtr refData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
