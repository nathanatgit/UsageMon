using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace UsageMon;

internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private readonly nint _hwnd;
    private readonly NativeMethods.SubclassProc _callback;
    private readonly uint _taskbarCreated;
    private readonly Action _settings, _toggle, _exit;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private NativeMethods.NotifyIconData _data;
    private bool _disposed;
    private readonly nint _ownedIcon;

    internal TrayIcon(Window owner, Action settings, Action toggle, Action exit)
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        _dispatcher = owner.DispatcherQueue;
        _settings = settings; _toggle = toggle; _exit = exit;
        _callback = WindowProc;
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        uint dpi = NativeMethods.GetDpiForSystem();
        _ownedIcon = NativeMethods.LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "UsageMon.ico"), 1,
            NativeMethods.GetSystemMetricsForDpi(49, dpi), NativeMethods.GetSystemMetricsForDpi(50, dpi), 0x10);
        if (!NativeMethods.SetWindowSubclass(_hwnd, _callback, 1, 0))
            throw new InvalidOperationException("Could not attach the tray message handler.");
        _data = new NativeMethods.NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(), Window = _hwnd, Id = 1,
            Flags = 1 | 2 | 4, CallbackMessage = CallbackMessage,
            Icon = _ownedIcon != 0 ? _ownedIcon : NativeMethods.LoadIcon(0, 32512), Tip = "UsageMon — right-click for options",
            Info = "", InfoTitle = ""
        };
        if (!NativeMethods.Shell_NotifyIcon(0, ref _data))
        {
            NativeMethods.RemoveWindowSubclass(_hwnd, _callback, 1);
            throw new InvalidOperationException("Could not create the tray icon.");
        }
    }

    private nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == _taskbarCreated) NativeMethods.Shell_NotifyIcon(0, ref _data);
        if (message == CallbackMessage)
        {
            uint mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMessage == 0x202) _dispatcher.TryEnqueue(() => _settings());
            if (mouseMessage == 0x205) ShowMenu();
            return 0;
        }
        return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        nint menu = NativeMethods.CreatePopupMenu();
        uint selected;
        try
        {
            NativeMethods.AppendMenu(menu, 0, 1, Loc.T("Settings"));
            NativeMethods.AppendMenu(menu, 0, 2, Loc.T("Show / hide panel"));
            NativeMethods.AppendMenu(menu, 0x800, 0, "");
            NativeMethods.AppendMenu(menu, 0, 3, Loc.T("Exit UsageMon"));
            NativeMethods.GetCursorPos(out var point);
            NativeMethods.SetForegroundWindow(_hwnd);
            selected = NativeMethods.TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, _hwnd, 0);
            NativeMethods.PostMessage(_hwnd, 0, 0, 0);
        }
        finally { NativeMethods.DestroyMenu(menu); }
        // Defer actions until the native menu callback has unwound.
        if (selected != 0) _dispatcher.TryEnqueue(() =>
        {
            switch (selected) { case 1: _settings(); break; case 2: _toggle(); break; case 3: _exit(); break; }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMethods.Shell_NotifyIcon(2, ref _data);
        NativeMethods.RemoveWindowSubclass(_hwnd, _callback, 1);
        if (_ownedIcon != 0) NativeMethods.DestroyIcon(_ownedIcon);
    }
}
