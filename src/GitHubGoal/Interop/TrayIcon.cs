using System.Runtime.InteropServices;

namespace GitHubGoal.Interop;

/// <summary>
/// A Windows notification-area icon built directly on Shell_NotifyIcon.
///
/// Owns a message-only window for the callback so the icon keeps working while the
/// widget itself is hidden, and so we never have to subclass WinUI's own window
/// procedure.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WM_DESTROY = 0x0002;
    private const int WM_COMMAND = 0x0111;
    private const int WM_APP_NOTIFY = 0x0400 + 1;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x00000010;
    private const int LR_DEFAULTSIZE = 0x00000040;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint MF_GRAYED = 0x00000001;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private readonly WndProcDelegate _wndProc;   // held so the GC cannot collect the callback
    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;
    private readonly string _className;
    private readonly List<MenuEntry> _menu = [];

    private bool _added;
    private bool _disposed;

    public TrayIcon(string iconPath, string tooltip)
    {
        _className = "GitHubGoalTray_" + Guid.NewGuid().ToString("N");
        _wndProc = WindowProc;

        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = _className,
        };

        if (RegisterClassEx(ref wc) == 0)
        {
            throw new InvalidOperationException($"Could not register the tray window class (Win32 {Marshal.GetLastWin32Error()}).");
        }

        // HWND_MESSAGE (-3) creates a message-only window: no pixels, just a queue.
        _hwnd = CreateWindowEx(0, _className, "GitHubGoal", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Could not create the tray message window (Win32 {Marshal.GetLastWin32Error()}).");
        }

        // LoadImage handles the PNG-compressed frames in our .ico, which the
        // GDI+ Icon class cannot decode.
        _icon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);

        var data = CreateData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_APP_NOTIFY;
        data.hIcon = _icon;
        data.szTip = Truncate(tooltip, 127);

        // The shell can refuse the icon if Explorer is not ready yet (common when the
        // app is launched from the Run key during logon), so retry briefly.
        for (var attempt = 0; attempt < 5 && !_added; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(400);
            }

            _added = Shell_NotifyIcon(NIM_ADD, ref data);
        }

        if (!_added)
        {
            throw new InvalidOperationException(
                $"The shell rejected the notification icon (Win32 {Marshal.GetLastWin32Error()}).");
        }
    }

    /// <summary>Raised for a left click (and for the default "Open" menu item).</summary>
    public event Action? Activated;

    /// <summary>Rebuilds the context menu. Called just before it is shown.</summary>
    public Func<IReadOnlyList<MenuEntry>>? MenuBuilder { get; set; }

    public void SetTooltip(string tooltip)
    {
        if (_disposed || !_added)
        {
            return;
        }

        var data = CreateData();
        data.uFlags = NIF_TIP;
        data.szTip = Truncate(tooltip, 127);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public sealed record MenuEntry(string Text, Action? Invoke, bool IsSeparator = false, bool IsEnabled = true)
    {
        public static MenuEntry Separator { get; } = new(string.Empty, null, IsSeparator: true);

        /// <summary>A non-clickable status line, e.g. "Today: 7 / 10".</summary>
        public static MenuEntry Header(string text) => new(text, null, IsEnabled: false);
    }

    private NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
    };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WM_APP_NOTIFY:
                // lParam carries the mouse message for the icon.
                switch ((int)lParam)
                {
                    case WM_LBUTTONUP:
                        Activated?.Invoke();
                        return IntPtr.Zero;

                    case WM_RBUTTONUP:
                        ShowMenu();
                        return IntPtr.Zero;
                }

                break;

            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        _menu.Clear();
        _menu.AddRange(MenuBuilder?.Invoke() ?? []);

        if (_menu.Count == 0)
        {
            return;
        }

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            for (var i = 0; i < _menu.Count; i++)
            {
                var entry = _menu[i];

                if (entry.IsSeparator)
                {
                    AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
                    continue;
                }

                var flags = MF_STRING | (entry.IsEnabled ? 0 : MF_GRAYED);

                // Command ids are 1-based; TrackPopupMenu returns 0 for "dismissed".
                AppendMenu(menu, flags, new IntPtr(i + 1), entry.Text);
            }

            GetCursorPos(out var cursor);

            // Required so the menu dismisses when the user clicks elsewhere.
            SetForegroundWindow(_hwnd);

            var selected = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, cursor.X, cursor.Y, _hwnd, IntPtr.Zero);

            if (selected > 0 && selected <= _menu.Count)
            {
                _menu[selected - 1].Invoke?.Invoke();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_added)
        {
            var data = CreateData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
        }

        UnregisterClass(_className, GetModuleHandle(null));
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, IntPtr id, string? item);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, int type, int cx, int cy, int load);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
