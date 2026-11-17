using System.Runtime.InteropServices;
using System.Text;

namespace UsageMon.Usage;

// API keys and sign-in tokens entered in Settings: DPAPI-protected (this Windows user only) files in UsageMon's data folder.
public static class SecretStore
{
    private static string PathOf(string name) => Path.Combine(UsageMonitor.DataFolder, name + ".bin");
    public static bool Has(string name) => File.Exists(PathOf(name));
    public static void Delete(string name) { try { File.Delete(PathOf(name)); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    public static string? Load(string name)
    {
        try { return Encoding.UTF8.GetString(Transform(File.ReadAllBytes(PathOf(name)), protect: false)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }
    public static void Save(string name, string value)
    {
        Directory.CreateDirectory(UsageMonitor.DataFolder);
        File.WriteAllBytes(PathOf(name), Transform(Encoding.UTF8.GetBytes(value), protect: true));
    }

    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);

    private static byte[] Transform(byte[] data, bool protect)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new Blob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
            bool ok = protect ? CryptProtectData(ref input, null, 0, 0, 0, 0, out var output) : CryptUnprotectData(ref input, 0, 0, 0, 0, 0, out output);
            if (!ok) throw new InvalidOperationException("DPAPI failed");
            try { var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, output.Size); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { handle.Free(); }
    }
}
