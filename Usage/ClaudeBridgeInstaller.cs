using System.Text.Json.Nodes;

namespace UsageMon.Usage;

public static class ClaudeBridgeInstaller
{
    // The status line runs a copy of the script under DataFolder; restore it if that copy was lost
    // (otherwise Claude Code silently runs a missing file and no usage ever reaches the panel).
    public static void RepairScript()
    {
        try
        {
            string home = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            string path = Path.Combine(home, "settings.json");
            if (!File.Exists(path)) return;
            string? command = JsonNode.Parse(File.ReadAllText(path))?["statusLine"]?["command"]?.GetValue<string>();
            if (command is null || !command.Contains("UsageMon", StringComparison.OrdinalIgnoreCase)
                || !command.Contains("claude-statusline.ps1", StringComparison.OrdinalIgnoreCase)) return;
            string folder = Path.Combine(UsageMonitor.DataFolder, "Integrations");
            string source = Path.Combine(AppContext.BaseDirectory, "Integrations", "claude-statusline.ps1");
            if (!File.Exists(source)) return;
            Directory.CreateDirectory(folder);
            File.Copy(source, Path.Combine(folder, "claude-statusline.ps1"), true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { }
    }

    public static string Install()
    {
        string home = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        string path = Path.Combine(home, "settings.json");
        var settings = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject() : new JsonObject();
        if (settings["statusLine"] is not null)
            return "A Claude status line already exists. It was left unchanged. See the integration instructions in README.";
        string folder = Path.Combine(UsageMonitor.DataFolder, "Integrations");
        Directory.CreateDirectory(folder);
        string script = Path.Combine(folder, "claude-statusline.ps1");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Integrations", "claude-statusline.ps1"), script, true);
        settings["statusLine"] = new JsonObject
        {
            ["type"] = "command",
            ["command"] = $"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script.Replace('\\', '/')}\""
        };
        Directory.CreateDirectory(home);
        if (File.Exists(path)) File.Copy(path, path + ".usagemon-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak", false);
        string temporary = path + ".usagemon.tmp";
        File.WriteAllText(temporary, settings.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
        return "Claude bridge enabled. Restart Claude Code; quota arrives after a normal response. Existing settings were backed up.";
    }
}
