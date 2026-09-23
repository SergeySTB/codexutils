using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

public sealed class CodexClient(string executable, string profile, TimeSpan? requestTimeout = null) : IUsageClient
{
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly TimeSpan timeout = requestTimeout ?? TimeSpan.FromSeconds(25);
    private Process? process;
    private Task? reader;
    private TaskCompletionSource<JsonElement>? login;
    private int nextId;
    private bool disposed;

    public async Task<AccountSnapshot> ReadAsync()
    {
        await operation.WaitAsync(lifetime.Token);
        try
        {
            await EnsureStartedAsync();
            var identity = await RequestAsync("account/read", new { refreshToken = false });
            if (!identity.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object ||
                Text(account, "type") != "chatgpt")
                throw new CodexException(FailureKind.SignIn, "Нужен вход через ChatGPT");
            var result = await RequestAsync("account/rateLimits/read", null);
            return new(Text(account, "email"), Text(account, "planType"), Limits.Parse(result), DateTimeOffset.Now);
        }
        catch (Exception error) when (error is not CodexException { Kind: FailureKind.SignIn or FailureKind.RateLimited })
        {
            await StopAsync();
            throw;
        }
        finally { operation.Release(); }
    }

    public async Task LoginAsync(Action<Uri> openBrowser)
    {
        await operation.WaitAsync(lifetime.Token);
        string? loginId = null;
        try
        {
            await EnsureStartedAsync();
            login = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = await RequestAsync("account/login/start", new { type = "chatgpt" });
            loginId = Text(result, "loginId");
            if (!Uri.TryCreate(Text(result, "authUrl"), UriKind.Absolute, out var url) ||
                url.Scheme != "https" || url.UserInfo.Length != 0 ||
                (url.Host != "auth.openai.com" && url.Host != "chatgpt.com"))
                throw new CodexException(FailureKind.Protocol, "Codex вернул неизвестный адрес входа");
            openBrowser(url);
            var completed = await login.Task.WaitAsync(TimeSpan.FromMinutes(5), lifetime.Token);
            if (!completed.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
                (loginId != null && Text(completed, "loginId") != loginId))
                throw new CodexException(FailureKind.SignIn, "Вход не завершён. Повторите попытку");
            loginId = null;
        }
        finally
        {
            if (loginId != null && !lifetime.IsCancellationRequested)
            {
                try { await RequestAsync("account/login/cancel", new { loginId }); }
                catch { /* The callback also disappears when this owned process stops. */ }
            }
            login = null;
            operation.Release();
        }
    }

    private async Task EnsureStartedAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (process is { HasExited: false }) return;
        await StopAsync();
        Directory.CreateDirectory(profile);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = profile
        };
        start.ArgumentList.Add("app-server");
        start.ArgumentList.Add("--stdio");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("cli_auth_credentials_store=\"file\"");
        start.Environment["CODEX_HOME"] = profile;
        start.Environment.Remove("OPENAI_API_KEY");
        start.Environment.Remove("CODEX_API_KEY");
        process = Process.Start(start) ?? throw new IOException("Не удалось запустить Codex");
        var current = process;
        // Drain diagnostics without persisting tokens, authorization URLs or raw responses.
        _ = DrainErrorsAsync(current);
        reader = ReadLoopAsync(current);
        await RequestAsync("initialize", new { clientInfo = new { name = "ai_usage_monitor", version = ProductInfo.Version } });
        await SendAsync(new { method = "initialized", @params = new { } });
    }

    private async Task<JsonElement> RequestAsync(string method, object? parameters)
    {
        int id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await SendAsync(new { id, method, @params = parameters });
            return await completion.Task.WaitAsync(timeout, lifetime.Token);
        }
        finally { pending.TryRemove(id, out _); }
    }

    private async Task SendAsync(object message)
    {
        await writer.WaitAsync(lifetime.Token);
        try
        {
            var target = process ?? throw new IOException("Codex остановлен");
            await target.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), lifetime.Token);
            await target.StandardInput.FlushAsync(lifetime.Token);
        }
        finally { writer.Release(); }
    }

    private async Task ReadLoopAsync(Process current)
    {
        try
        {
            while (await current.StandardOutput.ReadLineAsync(lifetime.Token) is { } line)
            {
                if (line.Length > 1_048_576) throw new InvalidDataException("Слишком большой ответ Codex");
                using var document = JsonDocument.Parse(line);
                var message = document.RootElement;
                if (message.TryGetProperty("method", out var method))
                {
                    if (message.TryGetProperty("id", out var serverId))
                    {
                        // The widget never approves tools, starts turns or supplies external tokens.
                        await SendAsync(new { id = serverId.Clone(), error = new { code = -32601, message = "Unsupported by usage widget" } });
                    }
                    else if (method.GetString() == "account/login/completed" && message.TryGetProperty("params", out var data))
                        login?.TrySetResult(data.Clone());
                    continue;
                }
                if (!message.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.Number ||
                    !responseId.TryGetInt32(out var id) || !pending.TryGetValue(id, out var completion)) continue;
                if (message.TryGetProperty("error", out var error)) completion.TrySetException(Classify(error));
                else if (message.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
                else completion.TrySetException(new CodexException(FailureKind.Protocol, "Неполный ответ Codex"));
            }
        }
        catch (Exception error) when (error is IOException or JsonException or OperationCanceledException or InvalidOperationException)
        { /* All outstanding operations receive a sanitized error below. */ }
        finally
        {
            var error = new CodexException(FailureKind.Connection, "Соединение с Codex прервано");
            foreach (var request in pending.Values) request.TrySetException(error);
            login?.TrySetException(error);
        }
    }

    private static CodexException Classify(JsonElement error)
    {
        var message = (Text(error, "message") ?? "").ToLowerInvariant();
        if (message.Contains("401") || message.Contains("403") || message.Contains("unauthorized") ||
            message.Contains("not authenticated") || message.Contains("sign in") || message.Contains("refresh token"))
            return new(FailureKind.SignIn, "Нужен повторный вход через ChatGPT");
        if (message.Contains("429") || message.Contains("too many requests"))
            return new(FailureKind.RateLimited, "Слишком частые запросы. Ожидание повтора");
        return new(FailureKind.Connection, "Не удалось получить лимиты Codex");
    }

    private static async Task DrainErrorsAsync(Process current)
    {
        try { while (await current.StandardError.ReadLineAsync() is not null) { } }
        catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException) { }
    }

    private async Task StopAsync()
    {
        if (process is not { } current) return;
        process = null;
        var oldReader = reader;
        reader = null;
        try { if (!current.HasExited) current.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        if (oldReader != null) await oldReader;
        current.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        if (process is { } current)
        {
            try { if (!current.HasExited) current.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            // StopAsync waits for the reader before releasing its streams.
            _ = StopAsync();
        }
    }

    private static string? Text(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
