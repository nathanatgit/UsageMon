using System.Runtime.InteropServices;

namespace UsageMon;

// Reads the Windows 11 taskbar's icon and widgets geometry through UI Automation, so the meter can avoid them.
internal static class TaskbarLayout
{
    internal readonly record struct Info(NativeMethods.Rect? Widgets, NativeMethods.Rect? Icons);

    private static Info _current;
    private static int _running;
    internal static Info Current => _current;

    // Query off the UI thread: UI Automation calls cross into explorer.exe.
    internal static void RefreshInBackground()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        Task.Run(() =>
        {
            try { if (Query() is { } info) _current = info; }
            finally { Volatile.Write(ref _running, 0); }
        });
    }

    private static Info? Query()
    {
        try
        {
            nint tray = NativeMethods.TaskbarHandle();
            if (tray == 0) return null;
            var uia = (IUIAutomation)new CUIAutomation();
            var root = uia.ElementFromHandle(tray);
            var all = root.FindAll(4 /* TreeScope_Descendants */, uia.CreateTrueCondition());
            NativeMethods.Rect? widgets = null, icons = null;
            for (int i = 0; i < all.Length; i++)
            {
                var element = all.GetElement(i);
                if (element.GetCurrentPropertyValue(30011 /* AutomationId */) is not string id || id.Length == 0) continue;
                bool isWidgets = id.Contains("Widgets", StringComparison.OrdinalIgnoreCase);
                bool isIcon = id is "StartButton" or "SearchButton" or "TaskViewButton" || id.StartsWith("Appid:", StringComparison.Ordinal);
                if (!isWidgets && !isIcon) continue;
                if (element.GetCurrentPropertyValue(30001 /* BoundingRectangle */) is not double[] { Length: 4 } box || box[2] <= 0) continue;
                var rect = new NativeMethods.Rect { Left = (int)box[0], Top = (int)box[1], Right = (int)(box[0] + box[2]), Bottom = (int)(box[1] + box[3]) };
                if (isWidgets) widgets = rect;
                else icons = icons is { } u
                    ? new NativeMethods.Rect { Left = Math.Min(u.Left, rect.Left), Top = Math.Min(u.Top, rect.Top), Right = Math.Max(u.Right, rect.Right), Bottom = Math.Max(u.Bottom, rect.Bottom) }
                    : rect;
            }
            if (Environment.GetEnvironmentVariable("USAGEMON_DEBUG") == "1")
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "usagemon-taskbar.txt"), $"widgets={widgets?.Left},{widgets?.Right} icons={icons?.Left},{icons?.Right}");
            return new Info(widgets, icons);
        }
        catch (Exception error) when (error is COMException or InvalidCastException or InvalidOperationException) { return null; }
    }

    [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")] private class CUIAutomation { }

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements(); void CompareRuntimeIds(); void GetRootElement();                    // 3-5
        IUIAutomationElement ElementFromHandle(nint hwnd);                                         // 6
        void M7(); void M8(); void M9(); void M10(); void M11(); void M12(); void M13();           // 7-13
        void M14(); void M15(); void M16(); void M17(); void M18(); void M19(); void M20();        // 14-20
        IUIAutomationCondition CreateTrueCondition();                                              // 21
    }

    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCondition { }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus(); void GetRuntimeId(); void FindFirst();                                    // 3-5
        IUIAutomationElementArray FindAll(int scope, IUIAutomationCondition condition);            // 6
        void FindFirstBuildCache(); void FindAllBuildCache(); void BuildUpdatedCache();            // 7-9
        [return: MarshalAs(UnmanagedType.Struct)] object GetCurrentPropertyValue(int propertyId);  // 10
    }

    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElementArray
    {
        int Length { get; }
        IUIAutomationElement GetElement(int index);
    }
}
