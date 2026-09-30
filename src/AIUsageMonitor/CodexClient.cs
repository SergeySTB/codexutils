using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("AIUsageMonitor.Checks")]

namespace AIUsageMonitor;

public enum FailureKind { SignIn, RateLimited, Connection, Protocol }
public sealed class CodexException(FailureKind kind, string message) : Exception(message)
{
    public FailureKind Kind { get; } = kind;
}
public sealed record AccountSnapshot(string? Email, string? Plan, Limits Limits, DateTimeOffset UpdatedAt);
public interface IUsageClient : IDisposable
{
    Task<AccountSnapshot> ReadAsync();
}

public sealed class CodexClient : IUsageClient
{
    // The same public client and internal service routes used by the Android app.
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    private const string Auth = "https://auth.openai.com";
    private const string Usage = "https://chatgpt.com/backend-api/wham/usage";
    private readonly string profile;
    private readonly HttpClient http;
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private byte[]? savedBytes;
    private bool disposed;
    private sealed record Credentials(string IdToken, string AccessToken, string RefreshToken,
        string AccountId, string? Email, string? Plan, long ExpiresAt);

    public CodexClient(string profile, ProxySettings? proxy = null)
        : this(profile, (proxy ?? new ProxySettings()).CreateHandler()) { }

    internal CodexClient(string profile, HttpMessageHandler handler)
    {
        this.profile = profile;
        http = new(handler) { Timeout = TimeSpan.FromSeconds(25) };
    }

    public async Task<AccountSnapshot> ReadAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operation.WaitAsync(lifetime.Token);
        try
        {
            var credentials = LoadCredentials();
            if (credentials.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300)
                credentials = await RefreshAsync(credentials, lifetime.Token);
            var response = await ReadUsageAsync(credentials, lifetime.Token);
            if (response.Status == HttpStatusCode.Unauthorized)
            {
                credentials = await RefreshAsync(credentials, lifetime.Token);
                response = await ReadUsageAsync(credentials, lifetime.Token);
            }
            RequireSuccess(response.Status);
            return new(credentials.Email, credentials.Plan, Limits.ParseUsage(response.Body), DateTimeOffset.Now);
        }
        finally { operation.Release(); }
    }

    public async Task LoginAsync(Action<Uri, string> showCode, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var login = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        login.CancelAfter(TimeSpan.FromMinutes(15));
        await operation.WaitAsync(login.Token);
        try
        {
            // A damaged credential store must still allow the user to sign in again.
            Credentials? previous = null;
            try { previous = LoadCredentials(); }
            catch (CodexException error) when (error.Kind == FailureKind.SignIn) { }
            var device = await JsonPostAsync(Auth + "/api/accounts/deviceauth/usercode", new { client_id = ClientId }, login.Token);
            RequireSuccess(device.Status);
            string deviceId = RequiredText(device.Body, "device_auth_id");
            string code = Text(device.Body, "user_code") ?? RequiredText(device.Body, "usercode");
            if (code.Length > 128 || code.Any(char.IsControl)) throw ProtocolError();
            int interval = int.TryParse(device.Body.TryGetProperty("interval", out var value) ? value.ToString() : "", out var seconds)
                ? Math.Clamp(seconds, 5, 30) : 5;
            showCode(new Uri(Auth + "/codex/device"), code);
            while (true)
            {
                var approved = await JsonPostAsync(Auth + "/api/accounts/deviceauth/token",
                    new { device_auth_id = deviceId, user_code = code }, login.Token);
                if (approved.Status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                {
                    await Task.Delay(TimeSpan.FromSeconds(interval), login.Token);
                    continue;
                }
                RequireSuccess(approved.Status);
                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code", ["client_id"] = ClientId,
                    ["code"] = RequiredText(approved.Body, "authorization_code"),
                    ["redirect_uri"] = Auth + "/deviceauth/callback",
                    ["code_verifier"] = RequiredText(approved.Body, "code_verifier")
                });
                var tokens = await RequestAsync(HttpMethod.Post, Auth + "/oauth/token", form, null, login.Token);
                RequireSuccess(tokens.Status);
                var credentials = ParseCredentials(tokens.Body);
                if (previous != null && credentials.AccountId != previous.AccountId) throw AccountChanged();
                SaveCredentials(credentials);
                return;
            }
        }
        finally { operation.Release(); }
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> ReadUsageAsync(Credentials credentials, CancellationToken token)
        => RequestAsync(HttpMethod.Get, Usage, null, credentials, token);

    private async Task<Credentials> RefreshAsync(Credentials previous, CancellationToken token)
    {
        var response = await JsonPostAsync(Auth + "/oauth/token", new
            { grant_type = "refresh_token", client_id = ClientId, refresh_token = previous.RefreshToken }, token);
        RequireSuccess(response.Status);
        var updated = ParseCredentials(response.Body, previous);
        if (updated.AccountId != previous.AccountId) throw AccountChanged();
        // Persist rotated tokens before requesting usage; never replay a token exchange.
        SaveCredentials(updated);
        return updated;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> JsonPostAsync(string address, object body, CancellationToken token)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await RequestAsync(HttpMethod.Post, address, content, null, token);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> RequestAsync(HttpMethod method, string address,
        HttpContent? content, Credentials? credentials, CancellationToken token)
    {
        var uri = new Uri(address);
        if (uri.Scheme != "https" || uri.Host is not ("auth.openai.com" or "chatgpt.com")) throw ProtocolError();
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("AIUsageMonitor/" + ProductInfo.Version);
        if (credentials != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
            request.Headers.Add("ChatGPT-Account-Id", credentials.AccountId);
        }
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) return (response.StatusCode, default);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(http.Timeout);
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (output.Length + count > 1_048_576) throw ProtocolError();
                output.Write(buffer, 0, count);
            }
            using var document = JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw ProtocolError();
            return (response.StatusCode, document.RootElement.Clone());
        }
        catch (JsonException) { throw ProtocolError(); }
        catch (HttpRequestException)
        { throw new CodexException(FailureKind.Connection, UiText.T("Не удалось подключиться к сервису Codex. Проверьте сеть и proxy.", "Could not connect to the Codex service. Check your network and proxy.")); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException(UiText.T("Нет ответа от сервиса Codex", "No response from the Codex service")); }
    }

    private Credentials LoadCredentials()
    {
        string path = Path.Combine(profile, "codex-auth.dat");
        savedBytes = File.Exists(path) ? ReadBounded(path) : null;
        if (savedBytes != null)
        {
            byte[]? plain = null;
            try
            {
                plain = ProtectedData.Unprotect(savedBytes, null, DataProtectionScope.CurrentUser);
                var credentials = JsonSerializer.Deserialize<Credentials>(plain) ?? throw SignInRequired();
                var validated = ParseCredentials(JsonSerializer.SerializeToElement(new
                    { id_token = credentials.IdToken, access_token = credentials.AccessToken, refresh_token = credentials.RefreshToken }));
                if (credentials.AccountId != validated.AccountId) throw SignInRequired();
                return validated with { ExpiresAt = credentials.ExpiresAt };
            }
            catch (Exception error) when (error is CryptographicException or JsonException or CodexException)
            { throw SignInRequired(); }
            finally { if (plain != null) CryptographicOperations.ZeroMemory(plain); }
        }
        string legacy = Path.Combine(profile, "auth.json");
        if (!File.Exists(legacy)) throw SignInRequired();
        try
        {
            using var document = JsonDocument.Parse(ReadBounded(legacy), new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("tokens", out var tokens)) throw SignInRequired();
            var imported = ParseCredentials(tokens, legacy: true);
            SaveCredentials(imported);
            return imported;
        }
        catch (Exception error) when (error is JsonException or CodexException) { throw SignInRequired(); }
    }

    private static byte[] ReadBounded(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length > 1_048_576) throw SignInRequired();
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = file.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (memory.Length + count > 1_048_576) throw SignInRequired();
            memory.Write(buffer, 0, count);
        }
        return memory.ToArray();
    }

    private void SaveCredentials(Credentials credentials)
    {
        Directory.CreateDirectory(profile);
        string path = Path.Combine(profile, "codex-auth.dat");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var plain = JsonSerializer.SerializeToUtf8Bytes(credentials);
        try
        {
            byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporary, encrypted);
            if (File.Exists(path) ? savedBytes == null || !ReadBounded(path).SequenceEqual(savedBytes) : savedBytes != null)
                throw new IOException(UiText.T("Данные входа изменены. Обновите аккаунт и повторите действие.", "Sign-in data changed. Refresh the account and retry."));
            File.Move(temporary, path, overwrite: true);
            savedBytes = encrypted;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static Credentials ParseCredentials(JsonElement tokens, Credentials? previous = null, bool legacy = false)
    {
        string idToken = Text(tokens, "id_token") ?? previous?.IdToken ?? throw ProtocolError();
        string access = RequiredText(tokens, "access_token");
        string refresh = Text(tokens, "refresh_token") ?? previous?.RefreshToken ?? throw ProtocolError();
        if (new[] { idToken, access, refresh }.Any(text => string.IsNullOrWhiteSpace(text) || text.Length > 65536 || text.Any(char.IsControl)))
            throw ProtocolError();
        using var id = Payload(idToken);
        using var accessPayload = access.Split('.').Length == 3 ? Payload(access) : JsonDocument.Parse("{}");
        var idAuth = id.RootElement.TryGetProperty("https://api.openai.com/auth", out var auth) ? auth : default;
        var accessAuth = accessPayload.RootElement.TryGetProperty("https://api.openai.com/auth", out auth) ? auth : default;
        string? idAccount = Text(idAuth, "chatgpt_account_id"), accessAccount = Text(accessAuth, "chatgpt_account_id");
        string account = idAccount ?? accessAccount ?? throw ProtocolError();
        string? explicitAccount = Text(tokens, "account_id");
        if ((idAccount != null && accessAccount != null && idAccount != accessAccount) ||
            (explicitAccount != null && explicitAccount != account) || account.Length > 256 ||
            account.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw ProtocolError();
        var identity = id.RootElement.TryGetProperty("https://api.openai.com/profile", out var profile) ? profile : default;
        string? email = Text(id.RootElement, "email") ?? Text(identity, "email");
        string? plan = Text(idAuth, "chatgpt_plan_type") ?? Text(accessAuth, "chatgpt_plan_type");
        if (new[] { email, plan }.Any(text => text != null && (text.Length > 256 || text.Any(char.IsControl)))) throw ProtocolError();
        long expires = accessPayload.RootElement.TryGetProperty("exp", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out var expiry) ? expiry : 0;
        if (expires <= 0 && !legacy)
        {
            long duration = tokens.TryGetProperty("expires_in", out var durationValue) && durationValue.ValueKind == JsonValueKind.Number && durationValue.TryGetInt64(out var result) ? result : 3600;
            expires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Clamp(duration, 0, 86400);
        }
        return new(idToken, access, refresh, account, email, plan, expires);
    }

    private static JsonDocument Payload(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3 || parts[1].Length == 0) throw ProtocolError();
            string base64 = parts[1].Replace('-', '+').Replace('_', '/');
            var parsed = JsonDocument.Parse(Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '=')),
                new JsonDocumentOptions { MaxDepth = 16 });
            if (parsed.RootElement.ValueKind == JsonValueKind.Object) return parsed;
            parsed.Dispose();
            throw ProtocolError();
        }
        catch (Exception error) when (error is FormatException or JsonException) { throw ProtocolError(); }
    }

    private static void RequireSuccess(HttpStatusCode status)
    {
        if ((int)status is >= 200 and < 300) return;
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest) throw SignInRequired();
        if (status == HttpStatusCode.TooManyRequests)
            throw new CodexException(FailureKind.RateLimited, UiText.T("Слишком частые запросы. Ожидание повтора", "Too many requests. Waiting to retry"));
        if (status == HttpStatusCode.ProxyAuthenticationRequired)
            throw new CodexException(FailureKind.Connection, UiText.T("Proxy требует авторизацию", "Proxy requires authentication"));
        throw new CodexException(FailureKind.Connection, UiText.T("Не удалось получить данные Codex", "Could not retrieve Codex data"));
    }

    private static CodexException ProtocolError() => new(FailureKind.Protocol, UiText.T("Некорректный ответ сервиса Codex", "Invalid response from the Codex service"));
    private static CodexException SignInRequired() => new(FailureKind.SignIn, UiText.T("Нужен вход через ChatGPT", "Sign in with ChatGPT"));
    private static CodexException AccountChanged() => new(FailureKind.SignIn, UiText.T("Выполнен вход в другой аккаунт. Используйте аккаунт этого профиля.", "Signed in to a different account. Use this profile's account."));
    private static string RequiredText(JsonElement value, string key) => Text(value, key) ?? throw ProtocolError();
    private static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(key, out var text) && text.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(text.GetString()) ? text.GetString() : null;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        http.Dispose();
    }
}
