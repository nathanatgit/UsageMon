using System.Runtime.InteropServices;

namespace UsageMon;

internal static class NativeMethods
{
    internal delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BlurBehind
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Enabled;
        public nint Region;
        [MarshalAs(UnmanagedType.Bool)] public bool Transition;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MinMaxInfo
    {
        public Point Reserved, MaximumSize, MaximumPosition, MinimumTrackSize, MaximumTrackSize;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public uint Size; public nint Window; public uint Id, Flags, CallbackMessage; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("comctl32.dll")] internal static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] internal static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] internal static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string text);
    [DllImport("user32.dll")] internal static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] internal static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] internal static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rectangle);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] internal static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref Rect rect, nint brush);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint hwnd, ref BlurBehind blur);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    internal static void ShapePanel(nint hwnd, int width, int height)
    {
        // Disabling DWM decoration alone leaves USER32's classic frame visible.
        // Remove both frame styles as well as DWM's border/shadow.
        long style = GetWindowLongPtr(hwnd, -16).ToInt64(); // GWL_STYLE
        long extended = GetWindowLongPtr(hwnd, -20).ToInt64(); // GWL_EXSTYLE
        long frameless = style & ~0x00C40000L; // WS_CAPTION | WS_THICKFRAME
        long flat = extended & ~0x00020301L; // modal, window, client, static edges
        if (style != frameless || extended != flat)
        {
            SetWindowLongPtr(hwnd, -16, (nint)frameless);
            SetWindowLongPtr(hwnd, -20, (nint)flat);
            SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x37); // FRAMECHANGED, no move/size/activation/z-order
        }
        int disabled = 1; // DWMNCRP_DISABLED
        DwmSetWindowAttribute(hwnd, 2, ref disabled, sizeof(int));
        int noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(int));
        int noRounding = 1; // DWMWCP_DONOTROUND
        DwmSetWindowAttribute(hwnd, 33, ref noRounding, sizeof(int));
        // Remove binary GDI clipping: the rounded material element supplies alpha edges.
        SetWindowRgn(hwnd, 0, true);
        ConfigureTransparentPanel(hwnd);
    }
    internal static void ConfigureTransparentPanel(nint hwnd)
    {
        var margins = new Margins();
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        nint region = CreateRectRgn(-2, -2, -1, -1);
        try
        {
            var blur = new BlurBehind { Flags = 3, Enabled = true, Region = region };
            DwmEnableBlurBehindWindow(hwnd, ref blur);
        }
        finally { if (region != 0) DeleteObject(region); }
    }
    internal static void ClearTransparentBackground(nint hwnd, nint dc)
    {
        if (GetClientRect(hwnd, out var rect)) FillRect(dc, ref rect, GetStockObject(4)); // BLACK_BRUSH, zero alpha
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    internal static nint TaskbarHandle() => FindWindow("Shell_TrayWnd", null);
    internal static bool GetTaskbarRects(out Rect taskbar, out Rect notify)
    {
        notify = default; taskbar = default;
        nint tray = FindWindow("Shell_TrayWnd", null);
        if (tray == 0 || !GetWindowRect(tray, out taskbar)) return false;
        nint area = FindWindowEx(tray, 0, "TrayNotifyWnd", null);
        if (area == 0 || !GetWindowRect(area, out notify)) notify = new Rect { Left = taskbar.Right, Right = taskbar.Right, Top = taskbar.Top, Bottom = taskbar.Bottom };
        return true;
    }
    internal static void PlaceTopmost(nint hwnd, int x, int y, int width, int height) =>
        SetWindowPos(hwnd, -1, x, y, width, height, 0x10 | 0x40); // HWND_TOPMOST, NOACTIVATE | SHOWWINDOW
    internal static void MakeToolWindow(nint hwnd)
    {
        long extended = GetWindowLongPtr(hwnd, -20).ToInt64();
        SetWindowLongPtr(hwnd, -20, (nint)(extended | 0x80L | 0x08000000L)); // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
    }
    internal static bool SystemUsesLightTheme()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
