using System.Text.Json;

namespace UsageMon.Usage;

public sealed class UsageMonitor : IDisposable
{
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageMon");
    private readonly Preferences _preferences;
    private readonly CodexUsageProvider _codex;
    private readonly ClaudeUsageProvider _claude = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private Task? _loop;
    public UsageSnapshot Codex { get; private set; } = UsageSnapshot.Initial("Codex");
    public UsageSnapshot Claude { get; private set; } = UsageSnapshot.Initial("Claude");
    public BalanceSnapshot DeepSeek { get; private set; } = BalanceSnapshot.Initial("DeepSeek");
    public BalanceSnapshot Kimi { get; private set; } = BalanceSnapshot.Initial("Kimi");
    private readonly BalanceProviders _balances = new();
    public event Action? Updated;
    public UsageMonitor(Preferences preferences) { _preferences = preferences; _codex = new(preferences); LoadCache(); }
    public void Start() => _loop ??= Task.Run(RunAsync);
    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await RefreshAsync();
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_preferences.UsageRefreshSeconds, 30, 600)), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    public async Task RefreshAsync()
    {
        if (_stop.IsCancellationRequested || !await _refresh.WaitAsync(0)) return;
        try
        {
            var codex = SafeRead("Codex", () => _codex.ReadAsync(_stop.Token));
            var claude = SafeRead("Claude", () => _claude.ReadAsync(_stop.Token));
            await Task.WhenAll(codex, claude);
            Codex = Merge(Codex, await codex);
            Claude = Merge(Claude, await claude);
            if (_preferences.ShowDeepSeek) DeepSeek = KeepLastGood(DeepSeek, await SafeBalance("DeepSeek", () => _balances.DeepSeekAsync(_stop.Token)));
            if (_preferences.ShowKimi) Kimi = KeepLastGood(Kimi, await SafeBalance("Kimi", () => _balances.KimiAsync(_stop.Token)));
            SaveCache();
            Updated?.Invoke();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { _refresh.Release(); }
    }
    private static async Task<UsageSnapshot> SafeRead(string provider, Func<Task<UsageSnapshot>> read)
    {
        try { return await read(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new(provider, UsageState.Unavailable, [], null, "Usage unavailable · check settings", ""); }
    }
    private static async Task<BalanceSnapshot> SafeBalance(string provider, Func<Task<BalanceSnapshot>> read)
    {
        try { return await read(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return BalanceSnapshot.Failure(provider, "Balance unavailable"); }
    }
    private static BalanceSnapshot KeepLastGood(BalanceSnapshot previous, BalanceSnapshot next) =>
        next.State != UsageState.Live && previous.Amount.Length > 0 && previous.State is UsageState.Live or UsageState.Stale
            ? previous with { State = UsageState.Stale, Message = next.Message } : next;
    internal static UsageSnapshot Merge(UsageSnapshot previous, UsageSnapshot next)
    {
        if (previous.Windows.Count == 0) return next;
        // A failed refresh, or an older fallback reading (for example the Claude Code status-line file while
        // the live request is rate limited), must never replace a newer reading.
        bool older = next.Windows.Count > 0 && next.UpdatedAt is { } incoming && previous.UpdatedAt is { } known && incoming < known;
        return next.Windows.Count == 0 || older ? previous with { State = UsageState.Stale, Message = next.Message } : next;
    }
    private void LoadCache()
    {
        try
        {
            var cached = JsonSerializer.Deserialize<UsageSnapshot[]>(File.ReadAllText(Path.Combine(DataFolder, "usage-cache.json")));
            foreach (var snapshot in cached ?? [])
            {
                if (snapshot.Windows.Count == 0) continue;
                var stale = snapshot with { State = UsageState.Stale, Message = "Saved reading · checking for updates…" };
                if (snapshot.Provider == "Codex") Codex = stale;
                if (snapshot.Provider == "Claude") Claude = stale;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }
    private void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            string path = Path.Combine(DataFolder, "usage-cache.json");
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new[] { Codex, Claude }));
            File.Move(temporary, path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    public void Dispose() { _stop.Cancel(); _codex.Dispose(); _claude.Dispose(); _balances.Dispose(); }
}
