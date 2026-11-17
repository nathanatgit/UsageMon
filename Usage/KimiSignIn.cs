using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UsageMon.Usage;

// Kimi Code device-code login (same OAuth client as Kimi's own CLI, as cc-switch does) for the Coding plan 5h/weekly quota.
// UsageMon keeps its own DPAPI-protected token slot, so it never reads or rotates Kimi CLI's credentials.
public static class KimiSignIn
{
    private const string ClientId = "17e5f671-d194-4dfb-9706-5516cb48c098";
    private const string Secret = "kimi-oauth", DeviceSecret = "kimi-device";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public sealed record Region(string Name, string OAuthHost, string BaseUrl);
    public static readonly Region China = new("China (kimi.com)", "https://auth.kimi.com", "https://api.kimi.com/coding/v1");
    public static readonly Region Global = new("International (kimi.ai)", "https://auth.kimi.ai", "https://api.kimi.ai/coding/v1");
    public sealed record Challenge(Region Region, string UserCode, string VerificationUri, string DeviceCode, int IntervalSeconds, DateTimeOffset Expires);
    public sealed record Credential(Region Region, string Access, string Refresh, DateTimeOffset Expires);
    public sealed record Quota(List<UsageWindow> Windows, string? Plan);

    public static bool HasLogin => SecretStore.Has(Secret);
    public static void SignOut() => SecretStore.Delete(Secret);

    public static async Task<Challenge?> StartAsync(Region region, CancellationToken cancellation)
    {
        using var document = await SendAsync(HttpMethod.Post, region.OAuthHost + "/api/oauth/device_authorization", null, new() { ["client_id"] = ClientId }, cancellation);
        if (document is null) return null;
        var root = document.RootElement;
        string? device = Str(root, "device_code"), user = Str(root, "user_code");
        string? uri = Str(root, "verification_uri_complete") is { Length: > 0 } complete ? complete : Str(root, "verification_uri");
        // Only ever open an https page.
        if (device is null || user is null || uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps) return null;
        return new(region, user, uri, device, Seconds(root, "interval", 5), DateTimeOffset.UtcNow.AddSeconds(Seconds(root, "expires_in", 600)));
    }

    // Polls until the user approves (true), the code expires or is denied, or it is cancelled (false).
    public static async Task<bool> WaitAsync(Challenge challenge, CancellationToken cancellation)
    {
        int interval = challenge.IntervalSeconds;
        while (DateTimeOffset.UtcNow < challenge.Expires)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellation);
            using var document = await SendAsync(HttpMethod.Post, challenge.Region.OAuthHost + "/api/oauth/token", null, new()
            {
                ["client_id"] = ClientId, ["device_code"] = challenge.DeviceCode, ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }, cancellation);
            if (document is null) continue;
            if (Parse(challenge.Region, document.RootElement) is { } credential) { Save(credential); return true; }
            string? error = Str(document.RootElement, "error");
            if (error == "slow_down") interval += 5;
            else if (error != "authorization_pending") return false; // expired_token, access_denied, ...
        }
        return false;
    }

    // Reads the Coding plan quota. Null when signed out or the login can no longer be used.
    public static async Task<(Quota? Quota, bool SignInNeeded, string? Error)> ReadAsync(CancellationToken cancellation)
    {
        var credential = Load();
        if (credential is null) return (null, true, null);
        if (credential.Expires <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            // Kimi rotates refresh tokens: persist the new pair straight away.
            using var refreshed = await SendAsync(HttpMethod.Post, credential.Region.OAuthHost + "/api/oauth/token", null, new()
            {
                ["client_id"] = ClientId, ["grant_type"] = "refresh_token", ["refresh_token"] = credential.Refresh
            }, cancellation);
            if (refreshed is null) return (null, false, "Could not refresh the Kimi sign-in");
            if (Parse(credential.Region, refreshed.RootElement) is not { } next) return (null, true, null);
            Save(next);
            credential = next;
        }
        var (usages, status) = await RequestAsync(HttpMethod.Get, credential.Region.BaseUrl + "/usages", credential.Access, null, cancellation);
        if (usages is null && status == 0) // a dropped connection right after a token refresh: try once more
        {
            await Task.Delay(1500, cancellation);
            (usages, status) = await RequestAsync(HttpMethod.Get, credential.Region.BaseUrl + "/usages", credential.Access, null, cancellation);
        }
        using var usageDocument = usages;
        if (status is 401 or 403) return (null, true, null);
        if (usages is null) return (null, false, status == 0 ? "Quota unavailable (network error)" : $"Quota unavailable (HTTP {status})");
        var root = usages.RootElement;
        var windows = new List<UsageWindow>();
        if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
            foreach (var item in limits.EnumerateArray())
                if (item.TryGetProperty("detail", out var detail) && Window("5h", detail) is { } five) windows.Add(five);
        if (root.TryGetProperty("usage", out var usage) && Window("weekly", usage) is { } weekly) windows.Add(weekly);
        using var me = await SendAsync(HttpMethod.Get, credential.Region.BaseUrl + "/me", credential.Access, null, cancellation);
        string? plan = me is null ? null : Str(me.RootElement, "user_level_name");
        // Free plans return an empty /usages: signed in fine, just nothing to meter.
        if (windows.Count == 0 && plan is null) return (null, false, "No quota returned for this account");
        return (new(windows, plan), false, null);
    }

    private static UsageWindow? Window(string name, JsonElement detail)
    {
        if (detail.ValueKind != JsonValueKind.Object) return null;
        double limit = Number(detail, "limit") ?? 1, remaining = Number(detail, "remaining") ?? 0;
        double used = limit > 0 ? Math.Clamp((limit - remaining) / limit * 100, 0, 100) : 0;
        DateTimeOffset? reset = null;
        if (detail.TryGetProperty("resetTime", out var time))
        {
            if (time.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)) reset = date;
            else if (time.ValueKind == JsonValueKind.Number && time.TryGetInt64(out long unix))
            {
                try { reset = unix > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unix) : DateTimeOffset.FromUnixTimeSeconds(unix); }
                catch (ArgumentOutOfRangeException) { }
            }
        }
        return new(name, used, reset);
    }

    private static Credential? Parse(Region region, JsonElement root)
    {
        string? access = Str(root, "access_token"), refresh = Str(root, "refresh_token");
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) return null;
        return new(region, access, refresh, DateTimeOffset.UtcNow.AddSeconds(Seconds(root, "expires_in", 3600)));
    }

    private static void Save(Credential credential) =>
        SecretStore.Save(Secret, string.Join('\n', credential.Region == Global ? "global" : "china", credential.Access, credential.Refresh, credential.Expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));

    private static Credential? Load()
    {
        string[]? lines = SecretStore.Load(Secret)?.Split('\n');
        if (lines is not { Length: 4 } || !long.TryParse(lines[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long expires)) return null;
        return new(lines[0] == "global" ? Global : China, lines[1], lines[2], DateTimeOffset.FromUnixTimeSeconds(expires));
    }

    private static async Task<JsonDocument?> SendAsync(HttpMethod method, string url, string? bearer, Dictionary<string, string>? form, CancellationToken cancellation) =>
        (await RequestAsync(method, url, bearer, form, cancellation)).Document;
    private static async Task<(JsonDocument? Document, int Status)> RequestAsync(HttpMethod method, string url, string? bearer, Dictionary<string, string>? form, CancellationToken cancellation)
    {
        int status = 0;
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (form is not null) request.Content = new FormUrlEncodedContent(form);
            if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("UsageMon/0.2.0");
            request.Headers.TryAddWithoutValidation("X-Msh-Platform", "kimi_code_cli");
            request.Headers.TryAddWithoutValidation("X-Msh-Version", "0.2.0");
            request.Headers.TryAddWithoutValidation("X-Msh-Device-Id", DeviceId());
            request.Headers.TryAddWithoutValidation("X-Msh-Device-Name", Ascii(Environment.MachineName));
            request.Headers.TryAddWithoutValidation("X-Msh-Device-Model", "windows " + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
            request.Headers.TryAddWithoutValidation("X-Msh-Os-Version", "windows");
            using var response = await Http.SendAsync(request, cancellation);
            status = (int)response.StatusCode;
            var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
            // A rejected token request (pending, denied) still carries a JSON body the caller needs.
            if (response.IsSuccessStatusCode || form is not null) return (document, status);
            document.Dispose();
            return (null, status);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException) { return (null, status); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return (null, status); }
    }

    private static string DeviceId()
    {
        if (SecretStore.Load(DeviceSecret) is { Length: > 0 } id) return id;
        id = Guid.NewGuid().ToString();
        SecretStore.Save(DeviceSecret, id);
        return id;
    }
    private static string Ascii(string text)
    {
        var builder = new StringBuilder();
        foreach (char c in text) if (c is > ' ' and < '\u007f' && builder.Length < 128) builder.Append(c);
        return builder.Length == 0 ? "Unknown" : builder.ToString();
    }
    private static string? Str(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int Seconds(JsonElement element, string key, int fallback) => element.TryGetProperty(key, out var value) && value.TryGetInt32(out int seconds) && seconds > 0 ? seconds : fallback;
    private static double? Number(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value)) return null;
        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : null;
    }
}
