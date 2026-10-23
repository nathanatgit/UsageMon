using System.Diagnostics;
using System.Text.Json;

namespace UsageMon.Usage;

public sealed class CodexUsageProvider : IDisposable
{
    private Process? _activeProcess;
    private readonly Preferences _preferences;
    public CodexUsageProvider(Preferences preferences) => _preferences = preferences;
    public async Task<UsageSnapshot> ReadAsync(CancellationToken cancellation)
    {
        string? executable = FindExecutable(_preferences.CodexExecutable);
        if (executable is null) return Failure("Codex CLI not found. Set its executable in settings.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("app-server"); info.ArgumentList.Add("--listen"); info.ArgumentList.Add("stdio://");
        using var process = new Process { StartInfo = info };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            process.Start();
            _activeProcess = process;
            cancellation.ThrowIfCancellationRequested();
            // Drain stderr to avoid deadlocks; raw client output is never persisted or displayed.
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"usage_mon\",\"title\":\"UsageMon\",\"version\":\"0.2.0\"}}}");
            await ReadResponse(process, 1, timeout.Token);
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}");
            await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
            var result = await ReadResponse(process, 2, timeout.Token);
            return UsageParser.Codex(result, DateTimeOffset.UtcNow);
        }
        catch (UsageSignInException) { return Failure("Sign in to Codex CLI", UsageState.SignInRequired); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return Failure("Codex usage request timed out"); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or JsonException or InvalidOperationException)
        { return Failure("Could not read Codex usage. Check the CLI and login."); }
        finally
        {
            _activeProcess = null;
            Kill(process);
        }
    }
    private static async Task<JsonElement> ReadResponse(Process process, int id, CancellationToken cancellation)
    {
        while (await process.StandardOutput.ReadLineAsync(cancellation) is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || !responseId.TryGetInt32(out int number) || number != id) continue;
            if (root.TryGetProperty("error", out var error))
            {
                string message = error.TryGetProperty("message", out var text) ? text.GetString() ?? "" : "";
                if (message.Contains("auth", StringComparison.OrdinalIgnoreCase) || message.Contains("login", StringComparison.OrdinalIgnoreCase)) throw new UsageSignInException();
                throw new InvalidOperationException("Codex quota request failed.");
            }
            return root.GetProperty("result").Clone();
        }
        throw new IOException("Codex app-server closed its output.");
    }
    public static string? FindExecutable(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin", "codex.exe");
        if (File.Exists(local)) return local;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { string path = Path.Combine(folder.Trim('"'), "codex.exe"); if (File.Exists(path)) return path; }
            catch (ArgumentException) { }
        }
        return null;
    }
    private static UsageSnapshot Failure(string message, UsageState state = UsageState.Unavailable) => new("Codex", state, [], null, message, "Codex app-server");
    private sealed class UsageSignInException : Exception { }
    private static void Kill(Process process)
    {
        try { if (process.Id > 0 && !process.HasExited) process.Kill(true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    public void Dispose() { if (_activeProcess is { } process) Kill(process); }
}
