using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace UsageMon.Usage;

// Grok and GitHub Copilot report a percentage quota, but they ride the balance badges: the badge shows what is left.
public sealed partial class BalanceProviders
{
    // Grok: the Grok CLI's own sign-in (~/.grok/auth.json) against grok.com's billing endpoint (gRPC-web, no public schema).
    public async Task<BalanceSnapshot> GrokAsync(CancellationToken cancellation)
    {
        const string name = "Grok";
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "auth.json");
        string? token = GrokToken(path);
        if (token is null) return BalanceSnapshot.Failure(name, "Sign in with the Grok CLI (grok login), then refresh", UsageState.SignInRequired, "quota");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://grok.com/grok_api_v2.GrokBuildBilling/GetGrokCreditsConfig");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("Origin", "https://grok.com");
            request.Headers.TryAddWithoutValidation("Referer", "https://grok.com/?_s=usage");
            request.Headers.TryAddWithoutValidation("x-grpc-web", "1");
            request.Headers.TryAddWithoutValidation("x-user-agent", "connect-es/2.1.1");
            request.Headers.UserAgent.ParseAdd("UsageMon/0.2.0");
            request.Content = new ByteArrayContent(new byte[5]); // empty gRPC-web frame
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc-web+proto");
            using var response = await _http.SendAsync(request, cancellation);
            const string expired = "Grok sign-in expired · run grok login";
            if (Rejected(response)) return BalanceSnapshot.Failure(name, expired, UsageState.SignInRequired, "quota");
            if (!response.IsSuccessStatusCode) return BalanceSnapshot.Failure(name, $"Quota unavailable (HTTP {(int)response.StatusCode})", noun: "quota");
            if (response.Headers.TryGetValues("grpc-status", out var codes) && codes.FirstOrDefault() is { } code && code != "0")
                return code is "16" or "7" ? BalanceSnapshot.Failure(name, expired, UsageState.SignInRequired, "quota")
                    : BalanceSnapshot.Failure(name, $"Quota unavailable (grpc-status {code})", noun: "quota");
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellation);
            if (!GrokBilling.TryParse(body, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out double used, out long? reset))
                return BalanceSnapshot.Failure(name, "Could not read Grok quota", noun: "quota");
            return Quota(name, used, reset, [], "SuperGrok credits · experimental");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return BalanceSnapshot.Failure(name, "Quota request timed out", noun: "quota"); }
        catch (Exception error) when (error is HttpRequestException or FormatException) { return BalanceSnapshot.Failure(name, "Could not read Grok quota", noun: "quota"); }
    }

    // GitHub Copilot: the token Copilot's own editor plugins/CLI saved, against GitHub's Copilot usage endpoint.
    public async Task<BalanceSnapshot> CopilotAsync(CancellationToken cancellation)
    {
        const string name = "Copilot";
        string? token = CopilotToken();
        if (token is null) return BalanceSnapshot.Failure(name, "Sign in to GitHub Copilot in VS Code or the Copilot CLI, then refresh", UsageState.SignInRequired, "quota");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/copilot_internal/user");
            request.Headers.TryAddWithoutValidation("Authorization", "token " + token);
            request.Headers.TryAddWithoutValidation("editor-version", "vscode/1.110.1");
            request.Headers.TryAddWithoutValidation("editor-plugin-version", "copilot-chat/0.38.2");
            request.Headers.TryAddWithoutValidation("x-github-api-version", "2025-10-01");
            request.Headers.UserAgent.ParseAdd("GitHubCopilotChat/0.38.2");
            using var response = await _http.SendAsync(request, cancellation);
            if (Rejected(response) || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return BalanceSnapshot.Failure(name, "GitHub sign-in expired · sign in to Copilot again", UsageState.SignInRequired, "quota");
            if (!response.IsSuccessStatusCode) return BalanceSnapshot.Failure(name, $"Quota unavailable (HTTP {(int)response.StatusCode})", noun: "quota");
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
            var root = document.RootElement;
            if (!root.TryGetProperty("quota_snapshots", out var snapshots) || snapshots.ValueKind != JsonValueKind.Object)
                return BalanceSnapshot.Failure(name, "No quota returned for this account", noun: "quota");
            var details = new List<KeyValuePair<string, string>>();
            if (Text(root, "copilot_plan") is { } plan) details.Add(new("Plan", plan));
            var windows = new List<UsageWindow>(); // metered items in ring order: premium requests, chat, completions
            foreach (var (key, label, windowName) in new[] { ("premium_interactions", "Premium requests", "premium"), ("chat", "Chat", "chat"), ("completions", "Completions", "completions") })
            {
                if (!snapshots.TryGetProperty(key, out var item) || item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("unlimited", out var unlimited) && unlimited.ValueKind == JsonValueKind.True) { details.Add(new(label, "Unlimited")); continue; }
                // Free plans report has_quota=false for premium requests: not used up, just not included.
                if (item.TryGetProperty("has_quota", out var has) && has.ValueKind == JsonValueKind.False) continue;
                if (!item.TryGetProperty("percent_remaining", out var left) || !left.TryGetDouble(out double remaining)) continue;
                remaining = Math.Clamp(remaining, 0, 100);
                windows.Add(new(windowName, 100 - remaining, null));
                string count = Number(item, "entitlement") is { } total && Number(item, "remaining") is { } rest ? $" ({rest}/{total})" : "";
                details.Add(new(label, $"{remaining:0.#}% left{count}"));
            }
            if (windows.Count == 0)
                return new(name, UsageState.Live, "∞", details, DateTimeOffset.UtcNow, "Copilot · no metered usage · experimental", "quota");
            DateTimeOffset? reset = null;
            if ((Text(root, "quota_reset_date_utc") ?? Text(root, "quota_reset_date")) is { } text
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                reset = date;
            windows = windows.Select(w => w with { ResetsAt = reset }).ToList();
            if (reset is { } resetAt) details.Add(new("Resets", resetAt.ToLocalTime().ToString("g")));
            // Premium requests are the headline when the plan has them; otherwise the most-used of chat/completions.
            double headline = windows.Any(w => w.Name == "premium") ? windows.First(w => w.Name == "premium").UsedPercent : windows.Max(w => w.UsedPercent);
            return new(name, UsageState.Live, $"{100 - headline:0}%", details, DateTimeOffset.UtcNow,
                windows.Any(w => w.Name == "premium") ? "Copilot premium requests · experimental" : "Copilot chat/completions · experimental", "quota", windows);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return BalanceSnapshot.Failure(name, "Quota request timed out", noun: "quota"); }
        catch (Exception error) when (error is HttpRequestException or JsonException or FormatException) { return BalanceSnapshot.Failure(name, "Could not read Copilot quota", noun: "quota"); }
    }

    // A percent-used reading shown like a balance: the badge says how much is left.
    private static BalanceSnapshot Quota(string name, double usedPercent, long? resetUnix, List<KeyValuePair<string, string>> extra, string message)
    {
        usedPercent = Math.Clamp(usedPercent, 0, 100);
        var details = new List<KeyValuePair<string, string>> { new("Used", $"{usedPercent:0.#}%"), new("Left", $"{100 - usedPercent:0.#}%") };
        details.AddRange(extra);
        if (resetUnix is { } unix && unix > 0)
        {
            try { details.Add(new("Resets", DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("g"))); }
            catch (ArgumentOutOfRangeException) { }
        }
        DateTimeOffset? resetAt = null;
        if (resetUnix is { } stamp && stamp > 0) { try { resetAt = DateTimeOffset.FromUnixTimeSeconds(stamp); } catch (ArgumentOutOfRangeException) { } }
        return new(name, UsageState.Live, $"{100 - usedPercent:0}%", details, DateTimeOffset.UtcNow, message, "quota", [new("used", usedPercent, resetAt)]);
    }

    // ~/.grok/auth.json maps scope -> entry. Prefer the SuperGrok OIDC entry ("https://auth.x.ai::<client>"), else the legacy sign-in entry.
    private static string? GrokToken(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            string? oidc = null, legacy = null;
            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object || Text(entry.Value, "key") is not { Length: > 0 } key) continue;
                if (entry.Name.StartsWith("https://auth.x.ai::", StringComparison.Ordinal)) oidc = key;
                else if (entry.Name.Contains("/sign-in", StringComparison.Ordinal)) legacy = key;
            }
            return oidc ?? legacy;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    // Copilot's editors and CLI keep the GitHub OAuth token in apps.json (older: hosts.json) under %LOCALAPPDATA%\github-copilot.
    internal static string CopilotFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "github-copilot");
    private static string? CopilotToken()
    {
        if (CopilotSignIn.LoadToken() is { Length: > 0 } own) return own;
        return EditorCopilotToken();
    }
    private static string? EditorCopilotToken()
    {
        foreach (string file in new[] { "apps.json", "hosts.json" })
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(CopilotFolder, file)));
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var entry in document.RootElement.EnumerateObject())
                    if (entry.Name.StartsWith("github.com", StringComparison.Ordinal) && entry.Value.ValueKind == JsonValueKind.Object
                        && Text(entry.Value, "oauth_token") is { Length: > 0 } token)
                        return token;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return null;
    }
}

// grok.com answers with an undocumented protobuf; scan it generically (heuristics ported from CodexBar / cc-switch).
internal static class GrokBilling
{
    private sealed class Scan
    {
        public readonly List<(List<ulong> Path, float Value, int Order)> Floats = [];
        public readonly List<(List<ulong> Path, ulong Value)> Varints = [];
    }

    public static bool TryParse(byte[] data, long nowSeconds, out double usedPercent, out long? resetsAt)
    {
        usedPercent = 0; resetsAt = null;
        var payloads = DataFrames(data);
        if (payloads.Count == 0 && data.Length > 0 && (data[0] >> 3) > 0 && (data[0] & 7) is 0 or 1 or 2 or 5) payloads = [data];
        if (payloads.Count == 0) return false;
        var scan = new Scan();
        foreach (var payload in payloads) Walk(payload, 0, [], 0, scan);

        // Percent: a float whose field number is 1 and value is 0-100; shallowest, then earliest.
        var percent = scan.Floats.Where(f => f.Path[^1] == 1 && float.IsFinite(f.Value) && f.Value is >= 0 and <= 100)
            .OrderBy(f => f.Path.Count).ThenBy(f => f.Order).Select(f => (double?)f.Value).FirstOrDefault();
        var resets = scan.Varints.Where(v => v.Value is >= 1_700_000_000 and <= 2_100_000_000 && (long)v.Value > nowSeconds).ToList();
        long? reset = resets.Where(v => v.Path.SequenceEqual(new ulong[] { 1, 5, 1 })).Select(v => (long?)(long)v.Value).Min()
            ?? resets.Select(v => (long?)(long)v.Value).Min();
        // proto3 omits a zero percent: a reset time plus a usage-period marker means 0% used.
        bool period = scan.Varints.Any(v => (v.Path.Count >= 2 && v.Path[0] == 1 && v.Path[1] == 6)
            || (v.Path.SequenceEqual(new ulong[] { 1, 8, 1 }) && v.Value is 1 or 2));
        if (percent is null && scan.Floats.Count == 0 && reset is not null && period) percent = 0;
        if (percent is not { } value) return false;
        usedPercent = value; resetsAt = reset;
        return true;
    }

    private static List<byte[]> DataFrames(byte[] data)
    {
        var frames = new List<byte[]>();
        int index = 0;
        while (index < data.Length)
        {
            if (index + 5 > data.Length) return [];
            long length = (long)data[index + 1] << 24 | (long)data[index + 2] << 16 | (long)data[index + 3] << 8 | data[index + 4];
            int start = index + 5;
            if (start + length > data.Length) return [];
            if ((data[index] & 0x80) == 0) frames.Add(data[start..(int)(start + length)]);
            index = (int)(start + length);
        }
        return frames;
    }

    private static bool Varint(byte[] bytes, ref int index, out ulong value)
    {
        value = 0;
        for (int shift = 0; index < bytes.Length && shift < 64; shift += 7)
        {
            byte b = bytes[index++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }
        return false;
    }

    // Length-delimited fields are tried as nested messages (depth <= 4); undecodable bytes resync one byte on.
    private static int Walk(byte[] bytes, int depth, List<ulong> path, int order, Scan scan)
    {
        int index = 0;
        while (index < bytes.Length)
        {
            int start = index;
            if (!Varint(bytes, ref index, out ulong key) || key == 0) { index = start + 1; continue; }
            var fieldPath = new List<ulong>(path) { key >> 3 };
            switch (key & 7)
            {
                case 0:
                    if (Varint(bytes, ref index, out ulong value)) scan.Varints.Add((fieldPath, value)); else index = start + 1;
                    break;
                case 1:
                    if (index + 8 > bytes.Length) return order;
                    index += 8;
                    break;
                case 2:
                    if (!Varint(bytes, ref index, out ulong length) || length > (ulong)(bytes.Length - index)) { index = start + 1; break; }
                    if (depth < 4) order = Walk(bytes[index..(index + (int)length)], depth + 1, fieldPath, order, scan);
                    index += (int)length;
                    break;
                case 5:
                    if (index + 4 > bytes.Length) return order;
                    scan.Floats.Add((fieldPath, BitConverter.ToSingle(bytes, index), order++));
                    index += 4;
                    break;
                default: index = start + 1; break;
            }
        }
        return order;
    }
}

public sealed partial class BalanceProviders
{
    // Kimi: an Open Platform API key gives the money balance; a browser sign-in adds the Kimi Code plan quota. Either, or both.
    public async Task<BalanceSnapshot> KimiAsync(CancellationToken cancellation)
    {
        const string name = "Kimi";
        string? key = StoredKey("moonshot-key") ?? Key("MOONSHOT_API_KEY") ?? Key("KIMI_API_KEY");
        bool login = KimiSignIn.HasLogin;
        if (key is null && !login)
            return BalanceSnapshot.Failure(name, "Add a Kimi API key or sign in under Settings", UsageState.SignInRequired);
        var balance = key is null ? null : await KimiBalanceAsync(key, cancellation);
        var (quota, signInNeeded, error) = login ? await KimiSignIn.ReadAsync(cancellation) : (null, false, null);
        var lines = quota is null ? [] : quota.Windows.Select(w => new KeyValuePair<string, string>(
            "Kimi Code " + w.Name, $"{w.UsedPercent:0.#}% used" + (w.ResetsAt is { } reset ? " · " + reset.ToLocalTime().ToString("g") : ""))).ToList();
        if (quota?.Plan is { Length: > 0 } plan) lines.Insert(0, new("Kimi Code plan", plan));
        if (balance is { State: UsageState.Live }) { balance.Details.AddRange(lines); return quota is { Windows.Count: > 0 } ? balance with { Windows = quota.Windows } : balance; }
        if (quota is not null)
        {
            if (quota.Windows.Count == 0)
                return new(name, UsageState.Live, quota.Plan ?? "–", lines, DateTimeOffset.UtcNow, "Kimi Code plan has no usage quota", "quota");
            double used = quota.Windows[0].UsedPercent;
            return new(name, UsageState.Live, $"{100 - used:0}%", lines, DateTimeOffset.UtcNow, "Kimi Code plan · experimental", "quota", quota.Windows);
        }
        if (balance is not null) return balance;
        return signInNeeded ? BalanceSnapshot.Failure(name, "Kimi sign-in expired · sign in again", UsageState.SignInRequired, "quota")
            : BalanceSnapshot.Failure(name, error ?? "Quota unavailable", noun: "quota");
    }
}
