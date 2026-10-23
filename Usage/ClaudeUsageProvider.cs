using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace UsageMon.Usage;

public sealed class ClaudeUsageProvider : IDisposable
{
    private readonly HttpClient _http;
    private DateTimeOffset _nextRequest;
    private UsageSnapshot? _lastFailure;
    public ClaudeUsageProvider()
    {
        // Authorization must never be redirected to another host.
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        LoadCooldown();
    }
    public async Task<UsageSnapshot> ReadAsync(CancellationToken cancellation)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextRequest) return ReadBridge() ?? _lastFailure ?? Failure("Waiting before next refresh");
        try
        {
            string home = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            string path = Path.Combine(home, ".credentials.json");
            if (!File.Exists(path)) return ReadBridge() ?? Failure("Sign in to Claude Code", UsageState.SignInRequired);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var credentials = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation);
            if (!credentials.RootElement.TryGetProperty("claudeAiOauth", out var oauth) || !oauth.TryGetProperty("accessToken", out var token)
                || token.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(token.GetString()))
                return ReadBridge() ?? Failure("Claude subscription login required", UsageState.SignInRequired);
            if (oauth.TryGetProperty("expiresAt", out var expiration) && expiration.ValueKind == JsonValueKind.Number
                && expiration.TryGetInt64(out long expiry) && expiry <= now.ToUnixTimeMilliseconds())
                return ReadBridge() ?? Failure("Claude sign-in expired · sign in again", UsageState.SignInRequired);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.GetString());
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            request.Headers.UserAgent.ParseAdd("UsageMon/0.2.0");
            using var response = await _http.SendAsync(request, cancellation);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - now) ?? TimeSpan.FromMinutes(2);
                _nextRequest = now + (delay < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay);
                SaveCooldown();
                _lastFailure = Failure($"Claude rate limited · retry after {_nextRequest.ToLocalTime():t}");
                return ReadBridge() ?? _lastFailure;
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return ReadBridge() ?? Failure("Claude sign-in expired · sign in again", UsageState.SignInRequired);
            if (!response.IsSuccessStatusCode) return ReadBridge() ?? Failure($"Claude usage unavailable (HTTP {(int)response.StatusCode})");
            _nextRequest = default;
            SaveCooldown();
            await using var body = await response.Content.ReadAsStreamAsync(cancellation);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellation);
            return UsageParser.Claude(document.RootElement, now);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return ReadBridge() ?? Failure("Claude usage request timed out"); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or HttpRequestException)
        { return ReadBridge() ?? Failure("Could not read Claude usage"); }
    }
    private static UsageSnapshot? ReadBridge()
    {
        try
        {
            string path = Path.Combine(UsageMonitor.DataFolder, "claude-statusline.json");
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("captured_at", out var timestamp) || !timestamp.TryGetDateTimeOffset(out var captured)) return null;
            var result = UsageParser.Claude(root, captured, true);
            if (result.Windows.Count == 0) return null;
            return result with { State = DateTimeOffset.UtcNow - captured > TimeSpan.FromMinutes(2) ? UsageState.Stale : UsageState.Live,
                Message = "Last supplied by Claude Code. Updates while Claude Code is active." };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    private void LoadCooldown()
    {
        try
        {
            string path = Path.Combine(UsageMonitor.DataFolder, "claude-cooldown.json");
            if (File.Exists(path)) _nextRequest = JsonSerializer.Deserialize<DateTimeOffset>(File.ReadAllText(path));
            else
            {
                // Conservative migration for an older cache without an exact retry timestamp.
                string cache = Path.Combine(UsageMonitor.DataFolder, "usage-cache.json");
                if (File.Exists(cache) && JsonSerializer.Deserialize<UsageSnapshot[]>(File.ReadAllText(cache))?
                    .Any(s => s.Provider == "Claude" && s.Message.StartsWith("Claude rate limited", StringComparison.Ordinal)) == true)
                {
                    _nextRequest = DateTimeOffset.UtcNow.AddHours(1);
                    SaveCooldown();
                }
            }
            if (_nextRequest > DateTimeOffset.UtcNow)
                _lastFailure = Failure($"Claude rate limited · retry after {_nextRequest.ToLocalTime():t}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }
    private void SaveCooldown()
    {
        try
        {
            Directory.CreateDirectory(UsageMonitor.DataFolder);
            File.WriteAllText(Path.Combine(UsageMonitor.DataFolder, "claude-cooldown.json"), JsonSerializer.Serialize(_nextRequest));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private static UsageSnapshot Failure(string message, UsageState state = UsageState.Unavailable) => new("Claude", state, [], null, message, "Anthropic OAuth usage (internal endpoint)");
    public void Dispose() => _http.Dispose();
}
