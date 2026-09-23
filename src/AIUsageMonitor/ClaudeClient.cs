using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIUsageMonitor;

public sealed class ClaudeClient(string configDir) : IUsageClient
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<AccountSnapshot> ReadAsync()
    {
        string path = Path.Combine(configDir, ".credentials.json");
        if (!File.Exists(path)) throw new CodexException(FailureKind.SignIn, "Войдите в Claude Code для этого профиля");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        if (!document.RootElement.TryGetProperty("claudeAiOauth", out var credentials) ||
            credentials.ValueKind != JsonValueKind.Object ||
            !credentials.TryGetProperty("accessToken", out var token) || token.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(token.GetString()))
            throw new CodexException(FailureKind.SignIn, "Нужен вход в Claude Code через подписку");
        if (!credentials.TryGetProperty("expiresAt", out var expiry) || expiry.ValueKind != JsonValueKind.Number ||
            !expiry.TryGetInt64(out var milliseconds) ||
            milliseconds <= DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds())
            throw new CodexException(FailureKind.SignIn, "Откройте Claude Code для обновления входа");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.GetString());
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd("AIUsageMonitor/" + ProductInfo.Version);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new CodexException(FailureKind.SignIn, "Нужен повторный вход в Claude Code");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new CodexException(FailureKind.RateLimited, "Claude ограничил запросы. Ожидание повтора");
        if (!response.IsSuccessStatusCode)
            throw new CodexException(FailureKind.Connection, "Не удалось получить лимиты Claude");
        await response.Content.LoadIntoBufferAsync(1_048_576);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var payload = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 16 });
        var limits = Parse(payload.RootElement);
        string? plan = credentials.TryGetProperty("subscriptionType", out var subscription) && subscription.ValueKind == JsonValueKind.String
            ? subscription.GetString() : null;
        return new(null, plan, limits, DateTimeOffset.Now);
    }

    public static Limits Parse(JsonElement root) => new(ParseWindow(root, "five_hour"), ParseWindow(root, "seven_day"));

    private static LimitWindow? ParseWindow(JsonElement root, string key)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("utilization", out var used) ||
            used.ValueKind != JsonValueKind.Number || !used.TryGetDouble(out double percent) ||
            !double.IsFinite(percent) || percent < 0 || percent > 100) return null;
        DateTimeOffset? reset = null;
        if (value.TryGetProperty("resets_at", out var time) && time.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            reset = parsed;
        return new LimitWindow(100 - percent, reset);
    }

    public void Dispose() => http.Dispose();
}
