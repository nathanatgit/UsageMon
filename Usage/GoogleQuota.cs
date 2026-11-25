using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UsageMon.Usage;

// Google Gemini (Gemini CLI) and Antigravity quotas, following cc-switch's loadCodeAssist / retrieveUserQuota /
// retrieveUserQuotaSummary endpoints, plan naming, eligibility checks, and agy CLI fallback.
public sealed partial class BalanceProviders
{
    private const string GeminiApiBase = "https://cloudcode-pa.googleapis.com/v1internal";
    private const string AgyApiBase = "https://daily-cloudcode-pa.googleapis.com/v1internal";
    private const string FallbackAgyVersion = "1.4.6";

    private readonly object _geminiCacheLock = new();
    private (string RefreshToken, string AccessToken, DateTimeOffset Expires)? _geminiCliRefreshCache;
    private string? _agyVersion;

    internal static string GeminiCredsPath()
    {
        string? custom = Environment.GetEnvironmentVariable("GEMINI_CLI_HOME");
        string home = !string.IsNullOrWhiteSpace(custom) ? custom.Trim() : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".gemini", "oauth_creds.json");
    }

    internal static bool HasGeminiSignIn() => GoogleOAuth.HasLogin(GoogleOAuth.GeminiClient) || File.Exists(GeminiCredsPath());
    internal static bool HasAntigravitySignIn() => GoogleOAuth.HasLogin(GoogleOAuth.AntigravityClient) || FindAgyExecutable() is not null;

    public async Task<BalanceSnapshot> GeminiAsync(CancellationToken cancellation)
    {
        const string name = "Gemini";
        var (token, refresh, ownLogin, expired, parseError) = ReadGeminiToken();
        if (token is null && refresh is null)
            return parseError is not null
                ? BalanceSnapshot.Failure(name, parseError, noun: "quota")
                : BalanceSnapshot.Failure(name, "Sign in to Gemini in Settings or with the Gemini CLI, then refresh", UsageState.SignInRequired, "quota");

        bool refreshed = false;
        if ((token is null || expired) && refresh is not null)
        {
            var (next, invalidGrant) = await RefreshGeminiTokenAsync(refresh, ownLogin, cancellation);
            if (next is not null) { token = next; refreshed = true; }
            else if (invalidGrant) return BalanceSnapshot.Failure(name, "Gemini sign-in expired · sign in again", UsageState.SignInRequired, "quota");
        }
        if (token is null) return BalanceSnapshot.Failure(name, "Gemini sign-in expired · sign in again", UsageState.SignInRequired, "quota");

        try
        {
            var result = await QueryGeminiQuotaAsync(token, cancellation);
            if (result.Unauthorized && !refreshed && refresh is not null)
            {
                var (next, invalidGrant) = await RefreshGeminiTokenAsync(refresh, ownLogin, cancellation);
                if (next is not null) result = await QueryGeminiQuotaAsync(next, cancellation);
                else if (invalidGrant) return BalanceSnapshot.Failure(name, "Gemini sign-in expired · sign in again", UsageState.SignInRequired, "quota");
            }
            return result.Snapshot;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return BalanceSnapshot.Failure(name, "Quota request timed out", noun: "quota"); }
        catch (Exception error) when (error is HttpRequestException or JsonException or FormatException) { return BalanceSnapshot.Failure(name, "Could not read Gemini quota", noun: "quota"); }
    }

    public async Task<BalanceSnapshot> AntigravityAsync(CancellationToken cancellation)
    {
        const string name = "Antigravity";
        var credential = GoogleOAuth.Load(GoogleOAuth.AntigravityClient);
        if (credential is null)
            return await QueryAntigravityCliAsync(cancellation);

        bool refreshed = false;
        string token = credential.Access;
        if (credential.Expires <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            var (next, invalidGrant) = await GoogleOAuth.RefreshAsync(GoogleOAuth.AntigravityClient, credential.Refresh, cancellation);
            if (next is not null) { GoogleOAuth.Save(GoogleOAuth.AntigravityClient, next); credential = next; token = next.Access; refreshed = true; }
            else if (invalidGrant) return BalanceSnapshot.Failure(name, "Antigravity sign-in expired · sign in again", UsageState.SignInRequired, "quota");
        }

        try
        {
            var result = await QueryAntigravityQuotaAsync(token, cancellation);
            if (result.Unauthorized && !refreshed)
            {
                var (next, invalidGrant) = await GoogleOAuth.RefreshAsync(GoogleOAuth.AntigravityClient, credential.Refresh, cancellation);
                if (next is not null) { GoogleOAuth.Save(GoogleOAuth.AntigravityClient, next); result = await QueryAntigravityQuotaAsync(next.Access, cancellation); }
                else if (invalidGrant) return BalanceSnapshot.Failure(name, "Antigravity sign-in expired · sign in again", UsageState.SignInRequired, "quota");
            }
            return result.Snapshot;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return BalanceSnapshot.Failure(name, "Quota request timed out", noun: "quota"); }
        catch (Exception error) when (error is HttpRequestException or JsonException or FormatException) { return BalanceSnapshot.Failure(name, "Could not read Antigravity quota", noun: "quota"); }
    }

    private (string? Access, string? Refresh, bool OwnLogin, bool Expired, string? Error) ReadGeminiToken()
    {
        if (GoogleOAuth.Load(GoogleOAuth.GeminiClient) is { } own)
            return (own.Access, own.Refresh, true, own.Expires <= DateTimeOffset.UtcNow.AddMinutes(2), null);

        string path = GeminiCredsPath();
        if (!File.Exists(path)) return (null, null, false, false, null);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null, false, false, "Could not read Gemini credentials");
            string? access = GoogleOAuth.NonEmpty(GoogleOAuth.Str(root, "access_token"));
            string? refresh = GoogleOAuth.NonEmpty(GoogleOAuth.Str(root, "refresh_token"));
            bool expired = root.TryGetProperty("expiry_date", out var exp) && exp.TryGetInt64(out long ms)
                && ms <= DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds();
            // Reuse an in-memory refreshed access token for the same CLI refresh token without modifying ~/.gemini/oauth_creds.json.
            if (expired && refresh is not null)
            {
                lock (_geminiCacheLock)
                {
                    if (_geminiCliRefreshCache is { } cached && cached.RefreshToken == refresh && cached.Expires > DateTimeOffset.UtcNow.AddMinutes(2))
                        return (cached.AccessToken, refresh, false, false, null);
                }
            }
            return (access, refresh, false, expired, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return (null, null, false, false, "Could not read Gemini credentials"); }
    }

    private async Task<(string? AccessToken, bool InvalidGrant)> RefreshGeminiTokenAsync(string refreshToken, bool ownLogin, CancellationToken cancellation)
    {
        var (next, invalidGrant) = await GoogleOAuth.RefreshAsync(GoogleOAuth.GeminiClient, refreshToken, cancellation);
        if (next is null) return (null, invalidGrant);
        if (ownLogin) GoogleOAuth.Save(GoogleOAuth.GeminiClient, next);
        else lock (_geminiCacheLock) { _geminiCliRefreshCache = (refreshToken, next.Access, next.Expires); }
        return (next.Access, false);
    }

    private async Task<(BalanceSnapshot Snapshot, bool Unauthorized)> QueryGeminiQuotaAsync(string accessToken, CancellationToken cancellation)
    {
        const string name = "Gemini";
        using var loadResponse = await PostGoogleJsonAsync($"{GeminiApiBase}:loadCodeAssist", accessToken, "UsageMon/0.2.0",
            "{\"metadata\":{\"ideType\":\"GEMINI_CLI\",\"pluginType\":\"GEMINI\"}}", cancellation);
        if (Rejected(loadResponse))
            return (BalanceSnapshot.Failure(name, "Gemini sign-in expired · sign in again", UsageState.SignInRequired, "quota"), true);
        if (!loadResponse.IsSuccessStatusCode)
            return (BalanceSnapshot.Failure(name, $"Quota unavailable (HTTP {(int)loadResponse.StatusCode})", noun: "quota"), false);

        string? projectId;
        string? plan;
        using (var loadDoc = await JsonDocument.ParseAsync(await loadResponse.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation))
        {
            projectId = GoogleOAuth.ExtractProject(loadDoc.RootElement);
            plan = GoogleOAuth.ParsePlan(loadDoc.RootElement);
        }

        string quotaBody = ProjectBodyJson(projectId);
        using var quotaResponse = await PostGoogleJsonAsync($"{GeminiApiBase}:retrieveUserQuota", accessToken, "UsageMon/0.2.0", quotaBody, cancellation);
        if (Rejected(quotaResponse))
            return (BalanceSnapshot.Failure(name, "Gemini sign-in expired · sign in again", UsageState.SignInRequired, "quota"), true);
        if (!quotaResponse.IsSuccessStatusCode)
            return (BalanceSnapshot.Failure(name, $"Quota unavailable (HTTP {(int)quotaResponse.StatusCode})", noun: "quota"), false);

        using var quotaDoc = await JsonDocument.ParseAsync(await quotaResponse.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
        var categories = new Dictionary<string, (double Remaining, DateTimeOffset? Reset)>(StringComparer.Ordinal);
        if (quotaDoc.RootElement.TryGetProperty("buckets", out var buckets) && buckets.ValueKind == JsonValueKind.Array)
        {
            foreach (var bucket in buckets.EnumerateArray())
            {
                if (bucket.ValueKind != JsonValueKind.Object) continue;
                string modelId = GoogleOAuth.Str(bucket, "modelId") ?? "unknown";
                string category = ClassifyGeminiModel(modelId);
                double remaining = bucket.TryGetProperty("remainingFraction", out var frac) && frac.TryGetDouble(out double f) && double.IsFinite(f) ? Math.Clamp(f, 0, 1) : 1;
                DateTimeOffset? reset = ParseIsoTime(GoogleOAuth.Str(bucket, "resetTime"));
                if (!categories.TryGetValue(category, out var existing))
                    categories[category] = (remaining, reset);
                else if (remaining < existing.Remaining)
                    categories[category] = (remaining, reset ?? existing.Reset);
            }
        }

        var ordered = categories.OrderBy(kv => GeminiCategoryOrder(kv.Key)).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        var details = new List<KeyValuePair<string, string>>();
        if (plan is not null) details.Add(new("Plan", plan));
        var windows = new List<UsageWindow>();
        foreach (var (category, (remaining, reset)) in ordered)
        {
            double used = Math.Clamp((1 - remaining) * 100, 0, 100);
            windows.Add(new(category, used, reset));
            string resetText = reset is { } at ? " · " + at.ToLocalTime().ToString("g") : "";
            details.Add(new(GeminiCategoryLabel(category), $"{remaining * 100:0.#}% left{resetText}"));
        }
        if (windows.Count == 0)
            return (plan is not null
                ? new(name, UsageState.Live, plan, details, DateTimeOffset.UtcNow, "Gemini · no quota buckets returned", "quota")
                : BalanceSnapshot.Failure(name, "No quota returned for this account", noun: "quota"), false);

        double headline = windows[0].UsedPercent;
        string message = plan is not null ? $"Gemini · {plan}" : "Gemini CLI quota";
        return (new(name, UsageState.Live, $"{100 - headline:0}%", details, DateTimeOffset.UtcNow, message, "quota", windows), false);
    }

    private async Task<(BalanceSnapshot Snapshot, bool Unauthorized)> QueryAntigravityQuotaAsync(string accessToken, CancellationToken cancellation)
    {
        const string name = "Antigravity";
        string ua = $"UsageMon/0.2.0 (antigravity/cli/{await GetAgyVersionAsync(cancellation)})";

        string? plan = null;
        string? projectId = null;
        bool loadChecked = false;
        try
        {
            using var loadResponse = await PostGoogleJsonAsync($"{AgyApiBase}:loadCodeAssist", accessToken, ua,
                "{\"metadata\":{\"ideType\":\"ANTIGRAVITY\"}}", cancellation);
            if (loadResponse.StatusCode == HttpStatusCode.Unauthorized)
                return (BalanceSnapshot.Failure(name, "Antigravity sign-in expired · sign in again", UsageState.SignInRequired, "quota"), true);
            if (loadResponse.IsSuccessStatusCode)
            {
                using var loadDoc = await JsonDocument.ParseAsync(await loadResponse.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
                if (GoogleOAuth.ParseIneligibleReason(loadDoc.RootElement) is { } reason)
                    return (BalanceSnapshot.Failure(name, reason, noun: "quota"), false);
                plan = GoogleOAuth.ParsePlan(loadDoc.RootElement);
                projectId = GoogleOAuth.ExtractProject(loadDoc.RootElement);
                loadChecked = true;
            }
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !cancellation.IsCancellationRequested)
        {
            // Non-401 loadCodeAssist failure degrades gracefully to calling retrieveUserQuotaSummary without a project (matches cc-switch).
        }

        using var summaryResponse = await PostGoogleJsonAsync($"{AgyApiBase}:retrieveUserQuotaSummary", accessToken, ua, ProjectBodyJson(projectId), cancellation);
        if (summaryResponse.StatusCode == HttpStatusCode.Unauthorized)
            return (BalanceSnapshot.Failure(name, "Antigravity sign-in expired · sign in again", UsageState.SignInRequired, "quota"), true);

        byte[] summaryBytes = await summaryResponse.Content.ReadAsByteArrayAsync(cancellation);
        if (!summaryResponse.IsSuccessStatusCode)
        {
            if (summaryResponse.StatusCode == HttpStatusCode.Forbidden && !loadChecked)
            {
                string? ineligible = await ProbeAgyIneligibleAsync(accessToken, ua, cancellation);
                if (ineligible is not null) return (BalanceSnapshot.Failure(name, ineligible, noun: "quota"), false);
            }
            string? detail = TryExtractErrorMessage(summaryBytes);
            string msg = detail is not null ? $"Quota unavailable (HTTP {(int)summaryResponse.StatusCode}): {detail}" : $"Quota unavailable (HTTP {(int)summaryResponse.StatusCode})";
            return (BalanceSnapshot.Failure(name, msg, noun: "quota"), false);
        }

        using var summaryDoc = JsonDocument.Parse(summaryBytes);
        var windows = ParseAgyGroups(summaryDoc.RootElement, "bucketId", "remainingFraction", "resetTime");
        return (BuildAntigravitySnapshot(windows, plan, "Antigravity quota"), false);
    }

    private async Task<string?> ProbeAgyIneligibleAsync(string accessToken, string ua, CancellationToken cancellation)
    {
        try
        {
            using var response = await PostGoogleJsonAsync($"{AgyApiBase}:loadCodeAssist", accessToken, ua,
                "{\"metadata\":{\"ideType\":\"ANTIGRAVITY\"}}", cancellation);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
            return GoogleOAuth.ParseIneligibleReason(doc.RootElement);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException) { return null; }
    }

    // Fallback when not signed in via UsageMon's Antigravity OAuth: run `agy --print /usage --output-format json`.
    private static async Task<BalanceSnapshot> QueryAntigravityCliAsync(CancellationToken cancellation)
    {
        const string name = "Antigravity";
        string? executable = FindAgyExecutable();
        if (executable is null)
            return BalanceSnapshot.Failure(name, "Sign in to Antigravity in Settings or install the agy CLI", UsageState.SignInRequired, "quota");

        var info = StartAgyProcess(executable, "--print", "/usage", "--output-format", "json");
        info.RedirectStandardInput = true;
        info.Environment.Remove("GEMINI_API_KEY");
        info.Environment.Remove("GOOGLE_GEMINI_BASE_URL");

        using var process = new Process { StartInfo = info };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            process.Start();
            process.StandardInput.Close();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string stdout = await stdoutTask;
            _ = await stderrTask;
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return BalanceSnapshot.Failure(name, "Sign in to Antigravity in Settings or with the agy CLI", UsageState.SignInRequired, "quota");

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (GoogleOAuth.Str(root, "status") != "SUCCESS"
                || !root.TryGetProperty("command", out var cmd) || cmd.ValueKind != JsonValueKind.Object
                || !cmd.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return BalanceSnapshot.Failure(name, "Sign in to Antigravity in Settings or with the agy CLI", UsageState.SignInRequired, "quota");

            var windows = ParseAgyGroups(data, "id", "remaining_fraction", "reset_time");
            return BuildAntigravitySnapshot(windows, null, "Antigravity CLI quota");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return BalanceSnapshot.Failure(name, "Quota request timed out", noun: "quota"); }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or JsonException or InvalidOperationException)
        { return BalanceSnapshot.Failure(name, "Could not read Antigravity quota", noun: "quota"); }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(true); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

    private static List<UsageWindow> ParseAgyGroups(JsonElement container, string idKey, string remainingKey, string resetKey)
    {
        var items = new List<(int Order, UsageWindow Window)>();
        if (!container.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array) return [];
        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object || !group.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array) continue;
            foreach (var bucket in buckets.EnumerateArray())
            {
                if (bucket.ValueKind != JsonValueKind.Object) continue;
                string? bucketId = GoogleOAuth.Str(bucket, idKey);
                var mapped = MapAgyBucket(bucketId);
                if (mapped is null) continue;
                if (!bucket.TryGetProperty(remainingKey, out var frac) || !frac.TryGetDouble(out double remaining) || !double.IsFinite(remaining)) continue;
                remaining = Math.Clamp(remaining, 0, 1);
                double used = Math.Clamp((1 - remaining) * 100, 0, 100);
                items.Add((mapped.Value.Order, new(mapped.Value.Name, used, ParseIsoTime(GoogleOAuth.Str(bucket, resetKey)))));
            }
        }
        return items.OrderBy(item => item.Order).Select(item => item.Window).ToList();
    }

    private static BalanceSnapshot BuildAntigravitySnapshot(List<UsageWindow> windows, string? plan, string fallbackMessage)
    {
        const string name = "Antigravity";
        if (windows.Count == 0)
            return BalanceSnapshot.Failure(name, "No quota returned for this account", noun: "quota");
        var details = new List<KeyValuePair<string, string>>();
        if (plan is not null) details.Add(new("Plan", plan));
        foreach (var window in windows)
        {
            string resetText = window.ResetsAt is { } at ? " · " + at.ToLocalTime().ToString("g") : "";
            details.Add(new(AgyWindowLabel(window.Name), $"{100 - window.UsedPercent:0.#}% left{resetText}"));
        }
        double headline = windows[0].UsedPercent;
        string message = plan is not null ? $"Antigravity · {plan}" : fallbackMessage;
        return new(name, UsageState.Live, $"{100 - headline:0}%", details, DateTimeOffset.UtcNow, message, "quota", windows);
    }

    private Task<HttpResponseMessage> PostGoogleJsonAsync(string url, string accessToken, string userAgent, string jsonBody, CancellationToken cancellation)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        return _http.SendAsync(request, cancellation);
    }

    private static string ProjectBodyJson(string? projectId)
    {
        if (string.IsNullOrEmpty(projectId)) return "{}";
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("project", projectId);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? TryExtractErrorMessage(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return GoogleOAuth.ExtractErrorMessage(doc.RootElement);
        }
        catch (JsonException) { return null; }
    }

    private async Task<string> GetAgyVersionAsync(CancellationToken cancellation)
    {
        if (_agyVersion is not null) return _agyVersion;
        string version = FallbackAgyVersion;
        if (FindAgyExecutable() is { } bin)
        {
            try
            {
                using var process = new Process { StartInfo = StartAgyProcess(bin, "--version") };
                process.Start();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                string output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                if (process.ExitCode == 0 && ParseVersionToken(output) is { } parsed) version = parsed;
            }
            catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException) { }
        }
        return _agyVersion = version;
    }

    private static string? ParseVersionToken(string output)
    {
        foreach (string raw in output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string word = raw.TrimStart('v', 'V');
            if (word.Contains('.') && word.Length > 0 && char.IsAsciiDigit(word[0]) && word.All(c => char.IsAsciiDigit(c) || c == '.'))
                return word;
        }
        return null;
    }

    private static ProcessStartInfo StartAgyProcess(string executable, params string[] args)
    {
        bool batch = executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        var info = new ProcessStartInfo(batch ? "cmd.exe" : executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (batch) { info.ArgumentList.Add("/c"); info.ArgumentList.Add(executable); }
        foreach (string arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    internal static string? FindAgyExecutable()
    {
        var dirs = new List<string>();
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            dirs.Add(entry.Trim('"'));
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin"));
        string[] names = ["agy.exe", "agy.cmd", "agy"];
        foreach (string dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (string file in names)
            {
                try
                {
                    string candidate = Path.Combine(dir, file);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
        }
        return null;
    }

    private static string ClassifyGeminiModel(string modelId)
    {
        if (modelId.Contains("flash-lite", StringComparison.OrdinalIgnoreCase)) return "flash-lite";
        if (modelId.Contains("flash", StringComparison.OrdinalIgnoreCase)) return "flash";
        if (modelId.Contains("pro", StringComparison.OrdinalIgnoreCase)) return "pro";
        return modelId;
    }

    private static int GeminiCategoryOrder(string category) => category switch { "pro" => 0, "flash" => 1, "flash-lite" => 2, _ => 3 };
    private static string GeminiCategoryLabel(string category) => category switch { "pro" => "Pro", "flash" => "Flash", "flash-lite" => "Flash Lite", _ => category };

    private static (string Name, int Order)? MapAgyBucket(string? bucketId) => bucketId switch
    {
        "gemini-5h" => ("gemini-5h", 0),
        "gemini-weekly" => ("gemini-weekly", 1),
        "gemini-argon-wtu-weekly" => ("gemini-argon", 2),
        "3p-5h" => ("3p-5h", 3),
        "3p-weekly" => ("3p-weekly", 4),
        _ => null
    };

    private static string AgyWindowLabel(string name) => name switch
    {
        "gemini-5h" => "Gemini 5h",
        "gemini-weekly" => "Gemini weekly",
        "gemini-argon" => "Gemini Argon weekly",
        "3p-5h" => "Claude / GPT 5h",
        "3p-weekly" => "Claude / GPT weekly",
        _ => name
    };

    private static DateTimeOffset? ParseIsoTime(string? value) =>
        value is { Length: > 0 } && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
}
