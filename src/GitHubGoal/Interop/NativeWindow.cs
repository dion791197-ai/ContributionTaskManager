using System.Runtime.InteropServices;

namespace GitHubGoal.Interop;

/// <summary>
/// The Win32 calls WinUI 3 does not surface: rounded corners, click-through-free
/// dragging, and monitor geometry for keeping the widget on screen.
/// </summary>
internal static class NativeWindow
{
    // --- monitors ----------------------------------------------------------
    private const int MONITOR_DEFAULTTONEAREST = 2;

    // --- frameless window ---------------------------------------------------
    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_SIZE = 0x0005;
    private const int WM_DPICHANGED = 0x02E0;

    /// <summary>
    /// Non-client frame thickness. Zero means the client area covers the whole window,
    /// so the card reaches every edge and the window region alone defines the shape.
    /// </summary>
    private const int FrameInset = 0;

    /// <summary>Rows clipped off the top, to drop the system's 1px window border.</summary>
    private const int TopBorderTrim = 1;

    /// <summary>Corner radius per window, in logical pixels, so resizes can rebuild the region.</summary>
    private static readonly Dictionary<IntPtr, int> CornerRadii = [];

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
    /// Clips the window to a rounded rectangle, which is what actually gives the widget
    /// its shape.
    ///
    /// DWM's corner preference is not an option: it needs Windows 11 build 22000, and on
    /// Windows 10 the call silently succeeds while the window stays square. A window
    /// region works on both.
    ///
    /// The region is a hard 1-bit mask, so its arcs are not antialiased. Giving the card
    /// the same CornerRadius hides that: the card's own antialiased curve sits just
    /// inside the clip, and the region only trims the few pixels beyond it.
    /// </summary>
    /// <param name="hwnd">Target window.</param>
    /// <param name="radius">Corner radius in logical pixels; match the card's CornerRadius.</param>
    public static void ApplyRoundedRegion(IntPtr hwnd, int radius)
    {
        CornerRadii[hwnd] = radius;
        RefreshRoundedRegion(hwnd);
    }

    private static void RefreshRoundedRegion(IntPtr hwnd)
    {
        if (!CornerRadii.TryGetValue(hwnd, out var radius) || !GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        var width = rect.Width;
        var height = rect.Height;

        // A minimised window reports an empty rect; a region built from one would leave
        // the window hidden when it is restored.
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var scaled = (int)Math.Round(radius * ScaleFor(hwnd));

        // The ellipse must not exceed the box it rounds, or the window becomes a lozenge
        // while it is being dragged smaller.
        var diameter = Math.Clamp(scaled * 2, 0, Math.Min(width, height));

        // Windows paints a one-pixel window border along the top edge, and collapsing the
        // non-client area in WM_NCCALCSIZE puts it inside the window rather than removing
        // it — a bright #E3E3E3 line straight across the card. The region is the only
        // thing that can take it back off, so the clip starts one row down.
        //
        // CreateRoundRectRgn's right and bottom edges are exclusive.
        var region = CreateRoundRectRgn(0, TopBorderTrim, width + 1, height + 1, diameter, diameter);

        if (region == IntPtr.Zero)
        {
            return;
        }

        // On success Windows owns the region and it must not be freed here; on failure
        // nothing took ownership, so it would leak.
        if (SetWindowRgn(hwnd, region, bRedraw: true) == 0)
        {
            _ = DeleteObject(region);
        }
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
    /// Makes a frameless window resizable again, and collapses its frame.
    ///
    /// WM_NCCALCSIZE removes the non-client area so the content fills the window, which
    /// also removes the edges Windows would normally hit-test for resizing. WM_NCHITTEST
    /// hands those back as a grab band just inside the window, over our own content, so
    /// resizing works without any chrome being drawn for it. The same hook keeps the
    /// rounded region in step with the window size.
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
            // Collapse the non-client frame so the client area covers the whole window
            // and the card reaches every edge. Left alone, the frame insets the content
            // by 8px and paints a light strip along the top, which is what kept reading
            // as a border around the card.
            if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
            {
                var proposed = Marshal.PtrToStructure<RECT>(lParam);
                proposed.Left += FrameInset;
                proposed.Top += FrameInset;
                proposed.Right -= FrameInset;
                proposed.Bottom -= FrameInset;
                Marshal.StructureToPtr(proposed, lParam, fDeleteOld: false);
                return IntPtr.Zero;
            }

            // The region is pixel geometry, so it has to be rebuilt whenever the window
            // changes size or moves to a monitor with a different scale.
            if (msg is WM_SIZE or WM_DPICHANGED)
            {
                var result = DefSubclassProc(h, msg, wParam, lParam);
                RefreshRoundedRegion(h);
                return result;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id, IntPtr refData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
