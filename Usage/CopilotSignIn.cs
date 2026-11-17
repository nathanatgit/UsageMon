using System.Net.Http.Headers;
using System.Text.Json;

namespace UsageMon.Usage;

// GitHub device-flow sign-in for Copilot quota (the Copilot CLI keeps its login in the OS credential store, which we cannot read).
// The token is kept DPAPI-protected in UsageMon's own folder; it is only ever sent to api.github.com.
public static class CopilotSignIn
{
    // The OAuth client id the official VS Code Copilot extension uses; the usage endpoint only accepts Copilot clients.
    private const string ClientId = "Iv1.b507a08c87ecfe98";
    private const string Secret = "copilot-token";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public sealed record Challenge(string UserCode, string VerificationUri, string DeviceCode, int IntervalSeconds, DateTimeOffset Expires);

    public static bool HasToken => SecretStore.Has(Secret);
    public static void SignOut() => SecretStore.Delete(Secret);
    public static string? LoadToken() => SecretStore.Load(Secret);

    public static async Task<Challenge?> StartAsync(CancellationToken cancellation)
    {
        using var document = await PostAsync("https://github.com/login/device/code", new() { ["client_id"] = ClientId, ["scope"] = "read:user" }, cancellation);
        if (document is null) return null;
        var root = document.RootElement;
        string? user = Str(root, "user_code"), uri = Str(root, "verification_uri"), device = Str(root, "device_code");
        if (user is null || uri is null || device is null) return null;
        int interval = root.TryGetProperty("interval", out var i) && i.TryGetInt32(out int s) ? Math.Max(s, 1) : 5;
        int life = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int l) ? l : 900;
        return new(user, uri, device, interval, DateTimeOffset.UtcNow.AddSeconds(life));
    }

    // Polls until the user approves (true), the code expires, or it is cancelled (false).
    public static async Task<bool> WaitAsync(Challenge challenge, CancellationToken cancellation)
    {
        int interval = challenge.IntervalSeconds;
        while (DateTimeOffset.UtcNow < challenge.Expires)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellation);
            using var document = await PostAsync("https://github.com/login/oauth/access_token", new()
            {
                ["client_id"] = ClientId, ["device_code"] = challenge.DeviceCode, ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }, cancellation);
            if (document is null) continue;
            if (Str(document.RootElement, "access_token") is { Length: > 0 } token)
            {
                SecretStore.Save(Secret, token);
                return true;
            }
            string? error = Str(document.RootElement, "error");
            if (error == "slow_down") interval += 5;
            else if (error != "authorization_pending") return false; // expired_token, access_denied, ...
        }
        return false;
    }

    private static async Task<JsonDocument?> PostAsync(string url, Dictionary<string, string> form, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("UsageMon/0.2.0");
            using var response = await Http.SendAsync(request, cancellation);
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException) { return null; }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return null; }
    }
    private static string? Str(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
