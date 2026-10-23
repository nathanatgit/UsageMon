using System.Runtime.InteropServices;

namespace UsageMon;

// A dock tab must be smaller than Windows' default top-level window minimum.
internal sealed class CompactWindowSizing
{
    private readonly NativeMethods.SubclassProc _callback;
    internal CompactWindowSizing(nint hwnd)
    {
        _callback = ProcessMessage;
        if (!NativeMethods.SetWindowSubclass(hwnd, _callback, 7, 0))
            throw new InvalidOperationException("Could not configure compact window sizing.");
    }

    private nint ProcessMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x83) return 0; // WM_NCCALCSIZE: the entire window is client content, with no classic frame.
        if (message == 0x14) // WM_ERASEBKGND: do not fill transparent corners with an opaque stock background.
        {
            NativeMethods.ClearTransparentBackground(hwnd, (nint)wParam);
            return 1;
        }
        if (message == 0x31E) NativeMethods.ConfigureTransparentPanel(hwnd); // WM_DWMCOMPOSITIONCHANGED
        nint result = NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
        if (message == 0x24) // WM_GETMINMAXINFO, after the default minimum has been supplied.
        {
            var info = Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(lParam);
            info.MinimumTrackSize = new NativeMethods.Point { X = 1, Y = 1 };
            Marshal.StructureToPtr(info, lParam, false);
        }
        if (message == 0x82) NativeMethods.RemoveWindowSubclass(hwnd, _callback, 7); // WM_NCDESTROY
        return result;
    }
}
