using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace UsageMon;

// "Start with Windows". Unpackaged: a per-user Run entry. Packaged (MSIX/Store): the Run key is virtualised away, so use the manifest's startup task.
// The OS is the source of truth in both cases, so nothing is stored in settings.json.
internal static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "UsageMon";
    private const string TaskId = "UsageMonStartup";

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentPackageFullName(ref int length, char[]? name);

    private static readonly bool Packaged = IsPackaged();

    private static bool IsPackaged()
    {
        int length = 0;
        return GetCurrentPackageFullName(ref length, null) != 15700; // APPMODEL_ERROR_NO_PACKAGE
    }

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        try
        {
            if (Packaged) return StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult().State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException or COMException) { return false; }
    }

    public static async Task<bool> SetAsync(bool enabled)
    {
        try
        {
            if (Packaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                if (enabled) return await task.RequestEnableAsync() is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
                task.Disable();
                return true;
            }
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, false);
            return true;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException or COMException) { return false; }
    }
}
