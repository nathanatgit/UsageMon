using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace UsageMon.Usage;

// Experimental: pay-per-use API balances (money, not a quota percentage).
public sealed record BalanceSnapshot(string Provider, UsageState State, string Amount,
    List<KeyValuePair<string, string>> Details, DateTimeOffset? UpdatedAt, string Message)
{
    public static BalanceSnapshot Initial(string provider) => new(provider, UsageState.Loading, "", [], null, "Checking balance…");
    public static BalanceSnapshot Failure(string provider, string message, UsageState state = UsageState.Unavailable) => new(provider, state, "", [], null, message);
    public string Summary => State == UsageState.Live || State == UsageState.Stale ? $"{Provider} balance {Amount}" : $"{Provider} · {Message}";
}

public sealed class BalanceProviders : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<BalanceSnapshot> DeepSeekAsync(CancellationToken cancellation)
    {
        const string name = "DeepSeek";
        string? key = Key("DEEPSEEK_API_KEY");
        if (key is null) return BalanceSnapshot.Failure(name, "Set the DEEPSEEK_API_KEY environment variable, then restart UsageMon", UsageState.SignInRequired);
        try
        {
            using var response = await Send("https://api.deepseek.com/user/balance", key, cancellation);
            if (Rejected(response)) return BalanceSnapshot.Failure(name, "API key rejected", UsageState.SignInRequired);
            if (!response.IsSuccessStatusCode) return BalanceSnapshot.Failure(name, $"Balance unavailable (HTTP {(int)response.StatusCode})");
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
            var root = document.RootElement;
            if (!root.TryGetProperty("balance_infos", out var infos) || infos.ValueKind != JsonValueKind.Array || infos.GetArrayLength() == 0)
                return BalanceSnapshot.Failure(name, "No balance returned for this key");
            var details = new List<KeyValuePair<string, string>>();
            string? amount = null;
            foreach (var info in infos.EnumerateArray())
            {
                string currency = Text(info, "currency") ?? "";
                string symbol = Symbol(currency);
                string? total = Number(info, "total_balance");
                if (total is null) continue;
                amount ??= Format(symbol, total);
                details.Add(new($"Total ({currency})", symbol + total));
                if (Number(info, "granted_balance") is { } granted) details.Add(new("Granted", symbol + granted));
                if (Number(info, "topped_up_balance") is { } topped) details.Add(new("Topped up", symbol + topped));
            }
            if (amount is null) return BalanceSnapshot.Failure(name, "Unexpected balance response");
            bool available = !root.TryGetProperty("is_available", out var flag) || flag.ValueKind != JsonValueKind.False;
            return new(name, UsageState.Live, amount, details, DateTimeOffset.UtcNow,
                available ? "Account balance · experimental" : "Balance is too low for API calls");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return BalanceSnapshot.Failure(name, "Balance request timed out"); }
        catch (Exception error) when (error is HttpRequestException or JsonException or FormatException)
        { return BalanceSnapshot.Failure(name, "Could not read DeepSeek balance"); }
    }

    public async Task<BalanceSnapshot> KimiAsync(CancellationToken cancellation)
    {
        const string name = "Kimi";
        string? key = Key("MOONSHOT_API_KEY") ?? Key("KIMI_API_KEY");
        if (key is null) return BalanceSnapshot.Failure(name, "Set the MOONSHOT_API_KEY environment variable, then restart UsageMon", UsageState.SignInRequired);
        try
        {
            // Keys are region-specific: try the international host first, then mainland China.
            foreach (var (host, symbol) in new[] { ("api.moonshot.ai", "$"), ("api.moonshot.cn", "¥") })
            {
                using var response = await Send($"https://{host}/v1/users/me/balance", key, cancellation);
                if (Rejected(response)) continue;
                if (!response.IsSuccessStatusCode) return BalanceSnapshot.Failure(name, $"Balance unavailable (HTTP {(int)response.StatusCode})");
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
                var data = document.RootElement.TryGetProperty("data", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : document.RootElement;
                if (Number(data, "available_balance") is not { } available) return BalanceSnapshot.Failure(name, "Unexpected balance response");
                var details = new List<KeyValuePair<string, string>> { new("Available", symbol + available) };
                if (Number(data, "cash_balance") is { } cash) details.Add(new("Cash", symbol + cash));
                if (Number(data, "voucher_balance") is { } voucher) details.Add(new("Voucher", symbol + voucher));
                return new(name, UsageState.Live, Format(symbol, available), details, DateTimeOffset.UtcNow, $"Open Platform balance · {host} · experimental");
            }
            return BalanceSnapshot.Failure(name, "API key rejected (Open Platform key required)", UsageState.SignInRequired);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return BalanceSnapshot.Failure(name, "Balance request timed out"); }
        catch (Exception error) when (error is HttpRequestException or JsonException or FormatException)
        { return BalanceSnapshot.Failure(name, "Could not read Kimi balance"); }
    }

    private Task<HttpResponseMessage> Send(string url, string key, CancellationToken cancellation)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.UserAgent.ParseAdd("UsageMon/0.2.0");
        return _http.SendAsync(request, cancellation);
    }
    private static bool Rejected(HttpResponseMessage response) => response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    private static string? Key(string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
    private static string? Text(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    // Amounts arrive as numbers or numeric strings.
    private static string? Number(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value)) return null;
        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number.ToString("0.##", CultureInfo.InvariantCulture) : null;
    }
    private static string Symbol(string currency) => currency.ToUpperInvariant() switch { "CNY" => "¥", "USD" => "$", _ => currency + " " };
    private static string Format(string symbol, string number)
    {
        decimal value = decimal.Parse(number, CultureInfo.InvariantCulture);
        string text = value >= 1000 ? $"{value / 1000:0.#}k" : value >= 100 ? $"{value:0}" : $"{value:0.00}";
        return symbol + text;
    }
    public void Dispose() => _http.Dispose();
}
