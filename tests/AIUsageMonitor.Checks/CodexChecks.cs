using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AIUsageMonitor;

internal static class CodexChecks
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private const string Usage = """{"rate_limit":{"primary_window":{"used_percent":25,"limit_window_seconds":18000,"reset_at":2000000000},"secondary_window":{"used_percent":62,"limit_window_seconds":604800}}}""";
    private static HttpResponseMessage Response(string body = Usage, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static string Jwt(string account, long expires) => "e30." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
    {
        ["email"] = account + "@example.com", ["exp"] = expires,
        ["https://api.openai.com/auth"] = new { chatgpt_account_id = account, chatgpt_plan_type = "pro" }
    })).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".test";
    private static string Tokens(string account, long expires, string refresh = "dummy-refresh")
        => JsonSerializer.Serialize(new { id_token = Jwt(account, expires), access_token = Jwt(account, expires), refresh_token = refresh, account_id = account });
    private static string Legacy(string root, string name, long? expires = null)
    {
        string profile = Path.Combine(root, name);
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "auth.json"), "{\"tokens\":" + Tokens(name, expires ?? DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()) + "}");
        return profile;
    }
    private static async Task Fails(Func<Task> action, FailureKind kind, Action<bool, string> check, string name)
    {
        try { await action(); throw new Exception("Expected failure: " + name); }
        catch (CodexException error) { check(error.Kind == kind, name); }
    }

    internal static async Task RunAsync(Action<bool, string> check, string root)
    {
        long future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        string profile = Legacy(root, "direct");
        string original = File.ReadAllText(Path.Combine(profile, "auth.json"));
        using (var client = new CodexClient(profile, new Handler((request, _) =>
        {
            check(request.RequestUri!.AbsoluteUri == "https://chatgpt.com/backend-api/wham/usage" &&
                request.Method == HttpMethod.Get && request.Headers.Authorization?.Scheme == "Bearer" &&
                request.Headers.GetValues("ChatGPT-Account-Id").Single() == "direct", "direct usage route and account headers");
            return Task.FromResult(Response());
        })))
        {
            var result = await client.ReadAsync();
            check(result.Email == "direct@example.com" && result.Plan == "pro" && result.Limits.FiveHour?.Remaining == 75 &&
                result.Limits.Weekly?.Remaining == 38, "direct usage preserves identity and both windows");
        }
        check(File.ReadAllText(Path.Combine(profile, "auth.json")) == original &&
            !Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(profile, "codex-auth.dat"))).Contains("dummy-refresh"),
            "legacy import preserves source and encrypts new storage");
        File.Delete(Path.Combine(profile, "auth.json"));
        using (var client = new CodexClient(profile, new Handler((_, _) => Task.FromResult(Response()))))
            check((await client.ReadAsync()).Email == "direct@example.com", "encrypted credentials survive client restart without CLI files");

        string refreshProfile = Legacy(root, "refresh", 1);
        int refreshes = 0;
        using (var client = new CodexClient(refreshProfile, new Handler(async (request, token) =>
        {
            if (request.RequestUri!.Host == "auth.openai.com")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                check(body.RootElement.GetProperty("grant_type").GetString() == "refresh_token" && request.Headers.Authorization == null,
                    "expired access uses direct refresh request without usage headers");
                refreshes++;
                return Response(Tokens("refresh", future, "rotated-refresh"));
            }
            check(File.Exists(Path.Combine(refreshProfile, "codex-auth.dat")), "tokens persisted before usage");
            return Response();
        }))) { await client.ReadAsync(); await client.ReadAsync(); }
        check(refreshes == 1, "refreshed credentials prevent repeated exchange");

        int requests = 0;
        using (var client = new CodexClient(Legacy(root, "retry"), new Handler((request, _) =>
        {
            requests++;
            return Task.FromResult(requests == 1 ? Response(status: HttpStatusCode.Unauthorized) :
                request.RequestUri!.Host == "auth.openai.com" ? Response(Tokens("retry", future)) : Response());
        }))) { await client.ReadAsync(); }
        check(requests == 3, "401 refreshes once and retries usage");

        foreach (var (status, kind) in new[] { (HttpStatusCode.Forbidden, FailureKind.SignIn), (HttpStatusCode.TooManyRequests, FailureKind.RateLimited),
            (HttpStatusCode.ProxyAuthenticationRequired, FailureKind.Connection), (HttpStatusCode.Redirect, FailureKind.Connection) })
        {
            int count = 0;
            using var client = new CodexClient(profile, new Handler((_, _) => { count++; return Task.FromResult(Response(status: status)); }));
            await Fails(() => client.ReadAsync(), kind, check, "direct status classified: " + status);
            check(count == 1, "no token refresh or redirect for " + status);
        }
        using (var client = new CodexClient(Path.Combine(root, "missing"), new Handler((_, _) => throw new Exception("Unexpected network"))))
            await Fails(() => client.ReadAsync(), FailureKind.SignIn, check, "empty profile requests sign-in without network");
        using (var client = new CodexClient(profile, new Handler((_, _) => Task.FromResult(Response(new string('x', 1_048_577))))))
            await Fails(() => client.ReadAsync(), FailureKind.Protocol, check, "response size is bounded");

        string changed = Legacy(root, "changed", 1);
        using (var client = new CodexClient(changed, new Handler((_, _) => Task.FromResult(Response(Tokens("other", future))))))
            await Fails(() => client.ReadAsync(), FailureKind.SignIn, check, "refresh cannot replace profile with another account");
        string bad = Legacy(root, "damaged");
        File.WriteAllText(Path.Combine(bad, "codex-auth.dat"), "corrupt");
        using (var client = new CodexClient(bad, new Handler((_, _) => throw new Exception("Unexpected network"))))
            await Fails(() => client.ReadAsync(), FailureKind.SignIn, check, "damaged store cannot fall back to stale CLI tokens");

        string loginProfile = Path.Combine(root, "login");
        Uri? page = null;
        string? shown = null;
        int exchanges = 0;
        string loginAccount = "login";
        using (var client = new CodexClient(loginProfile, new Handler(async (request, token) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("usercode")) return Response("""{"device_auth_id":"dummy-device","user_code":"ABCD-EFGH","interval":"5"}""");
            if (path.EndsWith("deviceauth/token")) return Response("""{"authorization_code":"dummy-code","code_verifier":"dummy-verifier"}""");
            if (path == "/oauth/token")
            {
                string form = await request.Content!.ReadAsStringAsync(token);
                check(form.Contains("grant_type=authorization_code") && form.Contains("code_verifier=dummy-verifier") &&
                    form.Contains("redirect_uri="), "device approval exchanges authorization code with verifier");
                exchanges++;
                return Response(Tokens(loginAccount, future));
            }
            return Response();
        })))
        {
            await client.LoginAsync((url, code) => { page = url; shown = code; });
            check((await client.ReadAsync()).Email == "login@example.com", "device login saves usable credentials");
            loginAccount = "another";
            await Fails(() => client.LoginAsync((_, _) => { }), FailureKind.SignIn, check, "reauthentication cannot change profile account");
            check((await client.ReadAsync()).Email == "login@example.com", "rejected login preserves existing credentials");
        }
        check(page?.AbsoluteUri == "https://auth.openai.com/codex/device" && shown == "ABCD-EFGH" && exchanges == 2,
            "login shows fixed trusted page and exchanges once per attempt");
        using (var cancel = new CancellationTokenSource())
        using (var client = new CodexClient(Path.Combine(root, "cancel"), new Handler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("usercode") ? Response("""{"device_auth_id":"d","usercode":"ABC"}""") : Response(status: HttpStatusCode.Forbidden)))))
        {
            cancel.CancelAfter(150);
            try { await client.LoginAsync((_, _) => { }, cancel.Token); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { check(!File.Exists(Path.Combine(root, "cancel", "codex-auth.dat")), "pending device login cancels without saving tokens"); }
        }
        using (var document = JsonDocument.Parse("""{"rate_limit":{"primary_window":{"used_percent":null,"limit_window_seconds":18000},"secondary_window":{"used_percent":0,"limit_window_seconds":604800}}}"""))
            check(Limits.ParseUsage(document.RootElement) is { FiveHour: null, Weekly.Remaining: 100 }, "direct malformed sibling does not hide valid quota");

        foreach (string mode in new[] { "usage", "login", "refresh" })
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string proxyProfile = mode == "refresh" ? Legacy(root, "proxy-refresh", 1) : profile;
                using var client = new CodexClient(proxyProfile, new ProxySettings { Enabled = true, Url = $"http://127.0.0.1:{port}" });
                Task task = mode == "login" ? client.LoginAsync((_, _) => { }) : client.ReadAsync();
                using var peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                using var stream = peer.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                string? connect = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                await Fails(() => task.WaitAsync(TimeSpan.FromSeconds(5)), FailureKind.Connection, check, "proxy failure classified");
                check(connect == $"CONNECT {(mode == "usage" ? "chatgpt.com" : "auth.openai.com")}:443 HTTP/1.1", "actual HTTPS proxy tunnel for " + mode);
            }
            finally { listener.Stop(); }
        }
    }
}
