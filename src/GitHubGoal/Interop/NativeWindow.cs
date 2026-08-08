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
}
