using System.Text;
using System.Text.Json;

namespace UsageMon.Usage;

public enum UsageState { Loading, Live, Stale, Unavailable, SignInRequired }
public sealed record UsageWindow(string Name, double UsedPercent, DateTimeOffset? ResetsAt);
public sealed record UsageSnapshot(string Provider, UsageState State, List<UsageWindow> Windows,
    DateTimeOffset? UpdatedAt, string Message, string Source)
{
    public static UsageSnapshot Initial(string provider) => new(provider, UsageState.Loading, [], null, "Checking usage…", "");
    public UsageWindow? FindWindow(string name, DateTimeOffset now) => Windows.FirstOrDefault(w => w.Name == name && (w.ResetsAt is null || w.ResetsAt > now));
    public string Summary(DateTimeOffset now)
    {
        var active = Windows.Where(w => w.ResetsAt is null || w.ResetsAt > now).Take(2).ToList();
        if (active.Count == 0) return Windows.Count > 0 ? "Awaiting refresh" : Message;
        return (State == UsageState.Live ? "" : "~ ") + string.Join(" · ", active.Select(w => $"{w.Name} {w.UsedPercent:0}%"));
    }
    public string ResetSummary(DateTimeOffset now)
    {
        var next = Windows.Where(w => w.ResetsAt > now).OrderBy(w => w.ResetsAt).FirstOrDefault();
        if (next?.ResetsAt is not { } reset) return State == UsageState.Loading ? "" : State == UsageState.Live ? "Updated just now" : "Open settings for details";
        var remaining = reset - now;
        string time = remaining.TotalDays >= 1 ? $"{(int)remaining.TotalDays}d {remaining.Hours}h"
            : remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours}h {remaining.Minutes}m" : $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
        return (State == UsageState.Live ? "Resets in " : "Cached reset · ") + time;
    }
    public string Details(DateTimeOffset now)
    {
        var text = new StringBuilder($"{Provider}: {State}\n{Message}");
        foreach (var window in Windows)
            text.Append($"\n{window.Name}: {window.UsedPercent:0.#}% used" + (window.ResetsAt is { } reset ? $" · resets {reset.ToLocalTime():g}" : ""));
        if (UpdatedAt is { } updated) text.Append($"\nLast received: {updated.ToLocalTime():g} ({Math.Max(0, (int)(now - updated).TotalMinutes)} min ago)");
        if (Source.Length > 0) text.Append($"\nSource: {Source}");
        return text.ToString();
    }
}

public static class UsageParser
{
    public static UsageSnapshot Codex(JsonElement result, DateTimeOffset now)
    {
        var windows = new List<UsageWindow>();
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
        {
            // Prefer the named Codex bucket. Keep every other bucket in the details.
            var ordered = buckets.EnumerateObject().OrderBy(p => p.Name == "codex" ? 0 : 1);
            foreach (var bucket in ordered) ReadCodexBucket(bucket.Value, bucket.Name == "codex" ? "" : bucket.Name + " ", windows);
        }
        if (windows.Count == 0 && result.TryGetProperty("rateLimits", out var legacy)) ReadCodexBucket(legacy, "", windows);
        return new("Codex", windows.Count > 0 ? UsageState.Live : UsageState.Unavailable, windows,
            windows.Count > 0 ? now : null, windows.Count > 0 ? "Account quota · percent used" : "Quota unavailable for this account", "Codex app-server");
    }
    private static void ReadCodexBucket(JsonElement bucket, string prefix, List<UsageWindow> windows)
    {
        if (bucket.ValueKind != JsonValueKind.Object) return;
        foreach (string key in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Object || !Percent(value, "usedPercent", out double used)) continue;
            int minutes = value.TryGetProperty("windowDurationMins", out var duration) && duration.TryGetInt32(out int m) ? m : 0;
            string name = minutes == 10080 ? "7d" : minutes > 0 && minutes % 1440 == 0 ? $"{minutes / 1440}d"
                : minutes > 0 && minutes % 60 == 0 ? $"{minutes / 60}h" : minutes > 0 ? $"{minutes}m" : key;
            windows.Add(new(prefix + name, used, Reset(value, "resetsAt")));
        }
    }
    public static UsageSnapshot Claude(JsonElement root, DateTimeOffset now, bool bridge = false)
    {
        var windows = new List<UsageWindow>();
        JsonElement data = bridge && root.TryGetProperty("rate_limits", out var limits) ? limits : root;
        if (data.ValueKind == JsonValueKind.Object)
            foreach (var (key, name) in new[] { ("five_hour", "5h"), ("seven_day", "7d"), ("seven_day_sonnet", "Sonnet 7d"), ("seven_day_opus", "Opus 7d"), ("spend_limit", "Spend") })
                if (data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object
                    && Percent(value, bridge ? "used_percentage" : "utilization", out double used))
                    windows.Add(new(name, used, Reset(value, "resets_at")));
        return new("Claude", windows.Count > 0 ? UsageState.Live : UsageState.Unavailable, windows,
            windows.Count > 0 ? now : null, windows.Count > 0 ? "Account quota · percent used" : "No quota supplied by this account",
            bridge ? "Claude Code status line" : "Anthropic OAuth usage (internal endpoint)");
    }
    private static bool Percent(JsonElement value, string key, out double percent)
    {
        percent = 0;
        return value.TryGetProperty(key, out var number) && number.ValueKind == JsonValueKind.Number
            && number.TryGetDouble(out percent) && double.IsFinite(percent) && percent >= 0 && percent <= 100;
    }
    private static DateTimeOffset? Reset(JsonElement value, string key)
    {
        if (!value.TryGetProperty(key, out var reset)) return null;
        if (reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out long seconds))
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { return null; }
        }
        return reset.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(reset.GetString(), out var date) ? date : null;
    }
}
