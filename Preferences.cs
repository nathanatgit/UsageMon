using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace UsageMon;

public sealed class Preferences
{
    public bool AlwaysOnTop { get; set; } = true;
    public bool Vertical { get; set; }
    public bool SeparatePanels { get; set; }
    public bool ShowCodex { get; set; } = true;
    public bool ShowClaude { get; set; } = true;
    public bool ClaudeFirst { get; set; } // legacy; migrated into Order on load
    public List<string> Order { get; set; } = ["Codex", "Claude", "DeepSeek", "Kimi"];
    // Every provider exactly once, in the saved order.
    public List<string> NormalizedOrder()
    {
        var order = Order.Where(k => k is "Codex" or "Claude" or "DeepSeek" or "Kimi").Distinct().ToList();
        foreach (var key in new[] { "Codex", "Claude", "DeepSeek", "Kimi" }) if (!order.Contains(key)) order.Add(key);
        return order;
    }
    // Swap two neighbouring visible providers (by their position among the visible ones).
    public void SwapVisible(int index, IReadOnlyList<string> visible)
    {
        if (index < 0 || index + 1 >= visible.Count) return;
        var order = NormalizedOrder();
        int a = order.IndexOf(visible[index]), b = order.IndexOf(visible[index + 1]);
        (order[a], order[b]) = (order[b], order[a]);
        Order = order;
    }
    public string TaskbarAlign { get; set; } = "Right";
    public string Language { get; set; } = "System"; // System, zh-Hans or en
    public bool ShowTaskbar { get; set; }
    public double TaskbarTextSize { get; set; } = 10;
    public bool ShowDeepSeek { get; set; }
    public bool ShowKimi { get; set; }
    public string Theme { get; set; } = "System";
    [JsonIgnore] public ElementTheme ThemeValue => Theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
    public bool Sticky { get; set; } = true;
    public bool AutoHideEdges { get; set; }
    public int UsageRefreshSeconds { get; set; } = 60;
    public string? CodexExecutable { get; set; }
    public string Material { get; set; } = "Mica";
    public double Transparency { get; set; } = 55;
    public string LabelMode { get; set; } = "Hover";
    public double LabelSize { get; set; } = 16;
    public double PanelSize { get; set; } = 104;
    public string? BackgroundImagePath { get; set; }
    public int? CodexX { get; set; }
    public int? CodexY { get; set; }
    public int? ClaudeX { get; set; }
    public int? ClaudeY { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageMon", "settings.json");

    public static Preferences Load()
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath)) ?? new();
            if (loaded.ClaudeFirst)
            {
                var order = loaded.NormalizedOrder();
                int a = order.IndexOf("Codex"), b = order.IndexOf("Claude");
                (order[a], order[b]) = (order[b], order[a]);
                loaded.Order = order;
                loaded.ClaudeFirst = false;
            }
            return loaded;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Debug.WriteLine(e); }
    }
}
