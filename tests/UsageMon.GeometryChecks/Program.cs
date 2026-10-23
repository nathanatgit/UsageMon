using UsageMon;
using Windows.Graphics;
using UsageMon.Usage;
using System.Text.Json;

int checks = 0;
void Check(string name, RectInt32 panel, RectInt32 work, RectInt32[] others, int threshold, int x, int y)
{
    var result = PanelSnapping.Snap(panel, work, others, threshold);
    if (result.X != x || result.Y != y) throw new Exception($"{name}: expected ({x}, {y}), got ({result.X}, {result.Y})");
    checks++;
}
var screen = new RectInt32(0, 0, 1920, 1040);
Check("left edge", new(8, 200, 162, 82), screen, [], 14, 0, 200);
Check("bottom right", new(1750, 950, 162, 82), screen, [], 14, 1758, 958);
Check("outside snap radius", new(15, 200, 162, 82), screen, [], 14, 15, 200);
Check("high DPI radius", new(20, 200, 324, 164), screen, [], 28, 0, 200);
Check("negative monitor", new(-1910, -190, 162, 82), new(-1920, -200, 1920, 1080), [], 14, -1920, -200);
Check("adjacent panels", new(270, 300, 162, 82), screen, [new(100, 300, 162, 82)], 14, 262, 300);
Check("aligned stack", new(108, 390, 162, 82), screen, [new(100, 300, 162, 82)], 14, 100, 382);
Check("remote panel cannot attract", new(270, 700, 162, 82), screen, [new(100, 300, 162, 82)], 14, 270, 700);
Console.WriteLine($"Passed {checks} snapping geometry checks.");
if (args.Length > 0 && args[0] != "--live")
{
    string input = Path.GetFullPath(args[0]);
    string folder = Path.Combine(Path.GetDirectoryName(input)!, "import-check");
    var source = await Windows.Storage.StorageFile.GetFileFromPathAsync(input);
    string copy = await BackgroundImageStore.ImportAsync(source, folder);
    if (copy == input || !File.ReadAllBytes(input).SequenceEqual(File.ReadAllBytes(copy)))
        throw new Exception("PNG import did not produce an identical independent local copy.");
    string invalid = Path.Combine(folder, "invalid.png");
    File.WriteAllText(invalid, "This is not a PNG.");
    bool rejected = false;
    try { await BackgroundImageStore.ImportAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(invalid), folder); }
    catch (Exception) { rejected = true; }
    if (!rejected) throw new Exception("Invalid PNG was accepted.");
    Console.WriteLine("Passed PNG decoding, independent copy, and invalid-file rejection checks.");
}

var now = DateTimeOffset.UtcNow;
UsageSnapshot ParseCodex(string json) { using var doc = JsonDocument.Parse(json); return UsageParser.Codex(doc.RootElement, now); }
UsageSnapshot ParseClaude(string json, bool bridge = false) { using var doc = JsonDocument.Parse(json); return UsageParser.Claude(doc.RootElement, now, bridge); }
void Assert(bool condition, string name) { if (!condition) throw new Exception("Usage check failed: " + name); checks++; }
var quota = ParseCodex("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":0,"windowDurationMins":300},"secondary":{"usedPercent":23,"windowDurationMins":10080}}}}""");
Assert(quota.Windows.Count == 2 && quota.Windows[0].UsedPercent == 0 && quota.Windows[0].Name == "5h", "zero is valid usage");
Assert(quota.Windows[1].Name == "7d", "weekly duration label");
Assert(ParseCodex("""{"rateLimits":{"primary":{"usedPercent":null}}}""").Windows.Count == 0, "missing quota is not zero");
Assert(ParseCodex("""{"rateLimits":{"primary":{"usedPercent":101}}}""").Windows.Count == 0, "invalid percentage rejected");
Assert(ParseCodex("""{"rateLimits":{"primary":{"usedPercent":7,"windowDurationMins":120}}}""").Windows[0].Name == "2h", "window lengths are not hardcoded");
Assert(ParseClaude("""{"five_hour":{"utilization":12.5,"resets_at":"2030-01-01T00:00:00Z"},"seven_day":null}""").Windows.Single().UsedPercent == 12.5, "Claude fractional quota and nullable windows");
Assert(ParseClaude("""{"rate_limits":{"five_hour":{"used_percentage":0,"resets_at":1900000000}}}""", true).Windows.Single().UsedPercent == 0, "official bridge quota parsing");
Assert(ParseClaude("""{"five_hour":{"utilization":null}}""").Windows.Count == 0, "Claude missing data unavailable");
var failure = new UsageSnapshot("Codex", UsageState.SignInRequired, [], null, "Sign in required", "");
var stale = UsageMonitor.Merge(quota, failure);
Assert(stale.State == UsageState.Stale && stale.UpdatedAt == quota.UpdatedAt && stale.Windows.Count == 2, "failure preserves reading and timestamp as stale");
Assert(stale.Summary(now).StartsWith("~ "), "stale marker is visible");
Assert(UsageMonitor.Merge(stale, quota).State == UsageState.Live, "new live response replaces stale state");
var expired = quota with { Windows = [new("5h", 90, now.AddSeconds(-1))] };
Assert(expired.Summary(now) == "Awaiting refresh", "expired windows cannot masquerade as current quota");
Console.WriteLine($"Passed {checks - 8} usage parsing and stale-state checks.");
Assert(quota.FindWindow("5h", now)?.UsedPercent == 0, "zero quota remains an available ring");
Assert(expired.FindWindow("5h", now) is null, "expired quota cannot fill a ring");
Assert(failure.FindWindow("7d", now) is null, "unknown quota stays an unknown ring");
Assert(ParseCodex("""{"rateLimits":{"primary":{"usedPercent":7,"windowDurationMins":120}}}""").FindWindow("5h", now) is null,
    "two-hour window cannot masquerade as five-hour ring");
Console.WriteLine("Passed 4 quota-to-ring mapping checks.");
if (args.Contains("--live"))
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35));
    using var codexProvider = new CodexUsageProvider(Preferences.Load());
    using var claudeProvider = new ClaudeUsageProvider();
    var results = await Task.WhenAll(codexProvider.ReadAsync(cancellation.Token), claudeProvider.ReadAsync(cancellation.Token));
    foreach (var result in results) Console.WriteLine($"{result.Provider}: {result.State} · {result.Summary(DateTimeOffset.UtcNow)}");
}
