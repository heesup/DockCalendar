using System.Runtime.InteropServices;

namespace DockCalendar.Ui;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_TRANSIENTWINDOW = 3;
    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref MARGINS margins);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attached);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_SYSMENU = 0x00080000;
    private const long WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    /// <summary>
    /// Drops the system menu, whose close button DWM would otherwise draw over the
    /// backdrop, and keeps the flyout out of Alt+Tab.
    /// </summary>
    public static void MakeFlyoutWindow(IntPtr window)
    {
        SetWindowLongPtr(window, GWL_STYLE, new IntPtr(GetWindowLongPtr(window, GWL_STYLE).ToInt64() & ~WS_SYSMENU));
        SetWindowLongPtr(window, GWL_EXSTYLE, new IntPtr(GetWindowLongPtr(window, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW));

        // Style changes only take effect once the frame is recalculated.
        const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_FRAMECHANGED = 0x0020;
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    public static POINT CursorPosition() => GetCursorPos(out var point) ? point : default;

    /// <summary>The monitor under a point: its full bounds, work area and scale (1.0 = 96 DPI).</summary>
    public static (RECT Monitor, RECT Work, double Scale) MonitorAt(POINT point)
    {
        var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
        return (info.Monitor, info.Work, scale);
    }

    public static void MoveWindow(IntPtr window, int x, int y, int width, int height) =>
        SetWindowPos(window, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);

    /// <summary>Rounded corners and an acrylic backdrop behind a borderless window (Windows 11).</summary>
    public static void ApplyFlyoutChrome(IntPtr window, bool dark)
    {
        var darkMode = dark ? 1 : 0;
        DwmSetWindowAttribute(window, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

        var corners = DWMWCP_ROUND;
        DwmSetWindowAttribute(window, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));

        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(window, ref margins);

        var backdrop = DWMSBT_TRANSIENTWINDOW;
        DwmSetWindowAttribute(window, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
    }

    /// <summary>
    /// Brings the flyout to the foreground. The extension process is not the one the
    /// user just clicked in, so Windows refuses a plain SetForegroundWindow; sharing
    /// the foreground thread's input queue for the call gets around that.
    /// </summary>
    public static void ForceForeground(IntPtr window)
    {
        var foreground = GetForegroundWindow();
        if (foreground == window)
        {
            return;
        }

        var current = GetCurrentThreadId();
        var target = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var attached = target != 0 && target != current && AttachThreadInput(current, target, true);
        try
        {
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(current, target, false);
            }
        }

        if (GetForegroundWindow() != window)
        {
            // Last resort: a synthetic Alt tap makes this thread eligible to take focus.
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            SetForegroundWindow(window);
        }
    }
}
