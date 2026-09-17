using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CodexLimits;

if (args.Contains("app-server")) { await FakeServer(); return; }
if (args.Length == 2 && args[0] == "--widget")
{
    try { WidgetChecks.Run(args[1]); }
    catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    return;
}
var checkRoot = Path.GetFullPath(Path.Combine(".build", "checks", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(checkRoot);
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    passed++;
    Console.WriteLine("PASS: " + name);
}
Limits Parse(string json)
{
    using var doc = JsonDocument.Parse(json);
    return Limits.Parse(doc.RootElement);
}
void Reject(Action action, string name)
{
    try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException) { Check(true, name); return; }
    throw new Exception("FAIL: " + name);
}

var weekly = Parse("""{"rateLimits":{"primary":{"usedPercent":62,"windowDurationMins":10080,"resetsAt":2000000000},"secondary":null}}""");
Check(weekly.FiveHour == null && weekly.Weekly?.Remaining == 38, "weekly-only primary is not mislabeled five-hour");
var both = Parse("""{"rateLimits":{"primary":{"usedPercent":110,"windowDurationMins":300},"secondary":{"usedPercent":0,"windowDurationMins":10080}}}""");
Check(both.FiveHour?.Remaining == 0 && both.Weekly?.Remaining == 100, "zero, full and exhausted quotas");
var mixed = Parse("""{"rateLimits":{"primary":{"usedPercent":1,"windowDurationMins":300}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":62,"windowDurationMins":10080}},"codex_bengalfox":{"primary":{"usedPercent":0,"windowDurationMins":300}}}}""");
Check(mixed.FiveHour == null && mixed.Weekly?.Remaining == 38, "main bucket wins over legacy and Spark");
Check(Parse("""{"rateLimitsByLimitId":{"codex_bengalfox":{"primary":{"usedPercent":0,"windowDurationMins":300}}}}""").FiveHour == null, "Spark-only response stays unavailable");
Check(Parse("""{"rateLimits":{"limitId":"codex_bengalfox","primary":{"usedPercent":0,"windowDurationMins":300}}}""").FiveHour == null, "legacy Spark bucket is not main Codex");
var malformed = Parse("""{"rateLimits":{"primary":{"usedPercent":null,"windowDurationMins":"300"},"secondary":{"usedPercent":15,"windowDurationMins":10080,"resetsAt":9999999999999}}}""");
Check(malformed.FiveHour == null && malformed.Weekly is { Remaining: 85, ResetsAt: null }, "malformed sibling and reset preserve valid weekly percentage");
Check(Parse("""{"rateLimits":{"primary":{"usedPercent":4,"windowDurationMins":15}}}""").FiveHour == null, "unknown durations are not guessed");
Check(Parse("""{"rateLimits":{"primary":{"usedPercent":-1,"windowDurationMins":300}}}""").FiveHour == null, "negative usage rejected");
Check(Parse("{}").Weekly == null, "missing data is not zero usage");
var now = DateTimeOffset.UtcNow;
Check(Limits.ResetText(new(10, now.AddSeconds(-1)), now).Contains("Ожидается"), "elapsed reset does not invent a replenished quota");

var area = new PixelRect(-1920, 0, 1920, 1040);
var widget = new WidgetSettings { WidthPx = 360, HeightPx = 144, MarginPx = 8, OffsetPx = 400 };
Check(Placement.Calculate(area, widget) == new PixelRect(-1520, 8, 360, 144), "top edge on negative-coordinate monitor");
Check(Placement.Calculate(area, widget with { Edge = "bottom" }).Y == 888, "bottom edge respects taskbar work area");
Check(Placement.Calculate(area, widget with { Edge = "left" }) == new PixelRect(-1912, 400, 360, 144), "left edge downward offset");
Check(Placement.Calculate(area, widget with { Edge = "right" }) == new PixelRect(-368, 400, 360, 144), "right edge downward offset");
Check(Placement.Calculate(area, widget with { OffsetPx = 99999 }).X == -360, "off-screen offset clamped");
Check(Placement.Calculate(new(0, 0, 200, 100), widget) == new PixelRect(0, 0, 200, 100), "oversized widget fits small screen");
var screen = new PixelRect(-1920, 0, 1920, 1080);
var compact = new WidgetSettings { Edge = "bottom", MarginPx = -20 };
Check(Placement.Calculate(area, compact, screen).Y == 1016, "negative bottom margin overlaps taskbar by 20 pixels");
Check(Placement.Calculate(area, compact with { MarginPx = -50 }, screen).Y == 1036, "user's -50 margin stays within physical screen");
Check(Placement.Calculate(area, compact with { MarginPx = 8 }, screen).Y == 988, "positive margin still respects work area");
Check(Placement.Calculate(screen, compact with { MarginPx = 4, RespectTaskbar = false }, screen).Y == 1032, "full-screen anchor places widget on taskbar");
Check(Placement.Calculate(new(-1920, 40, 1920, 1040), compact with { Edge = "top", MarginPx = -20 }, screen).Y == 20, "negative top margin crosses work-area boundary");
var valid = new Settings { Accounts = [new("One", Path.Combine(checkRoot, "one")), new("Two", Path.Combine(checkRoot, "two"))] };
Check(valid.Validate().Accounts.Length == 2, "two profiles accepted");
Reject(() => (valid with { Accounts = [valid.Accounts[0], valid.Accounts[0] with { CodexHome = valid.Accounts[0].CodexHome.ToUpperInvariant() + "/" }] }).Validate(), "duplicate profile paths rejected");
Reject(() => (valid with { Accounts = [valid.Accounts[0]] }).Validate(), "single profile rejected");
Reject(() => (valid with { RefreshSeconds = 0 }).Validate(), "polling interval validated");
Reject(() => (valid with { Widget = widget with { Edge = "middle" } }).Validate(), "unknown edge rejected");
Reject(() => (valid with { Widget = widget with { WidthPx = 0 } }).Validate(), "zero width rejected");
Check((valid with { Widget = compact with { MarginPx = -50 } }).Validate().Widget.MarginPx == -50, "negative user margin accepted");
Reject(() => (valid with { Widget = compact with { MarginPx = -4097 } }).Validate(), "excessive negative margin rejected");
var configPath = Path.Combine(checkRoot, "config.json");
File.WriteAllText(configPath, "{\"refeshSeconds\":60}");
Reject(() => Settings.Load(configPath), "configuration typos rejected");
File.WriteAllText(configPath, JsonSerializer.Serialize(valid with { Widget = widget }, Settings.JsonOptions));
Check(Settings.Load(configPath).Widget is { WidthPx: 88, HeightPx: 44 }, "old default panel becomes compact without rewriting config");
Check(File.ReadAllText(configPath).Contains("360"), "old configuration file is preserved");
Check((valid with { Widget = new WidgetSettings() }).Validate().Widget is { WidthPx: 88, HeightPx: 44 }, "compact default accepted");
File.WriteAllText(configPath, JsonSerializer.Serialize(valid with { Widget = widget with { WidthPx = 120, HeightPx = 60 } }, Settings.JsonOptions));
Check(Settings.Load(configPath).Widget is { WidthPx: 120, HeightPx: 60 }, "custom icon-panel size preserved");

string exe = Environment.ProcessPath!;
using (var one = new CodexClient(exe, valid.Accounts[0].CodexHome))
using (var two = new CodexClient(exe, valid.Accounts[1].CodexHome))
{
    var readings = await Task.WhenAll(one.ReadAsync(), two.ReadAsync());
    Check(readings[0].Email == "one@example.com" && readings[1].Email == "two@example.com", "parallel processes isolate account identity");
    Check(readings[0].Limits.Weekly?.Remaining == 38 && readings[1].Limits.FiveHour?.Remaining == 75, "parallel quotas stay with their profiles");
    Check((await one.ReadAsync()).Email == "one@example.com", "connection reused after initialization");
    Uri? loginUri = null;
    await one.LoginAsync(url => loginUri = url);
    Check(loginUri?.Host == "auth.openai.com", "browser login handles completion notification");
}
foreach (var profile in valid.Accounts)
{
    int pid = int.Parse(File.ReadAllText(Path.Combine(profile.CodexHome, "pid.txt")));
    try { using var child = Process.GetProcessById(pid); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
    catch (ArgumentException) { }
}
Check(true, "owned child processes stop on disposal");
foreach (var (scenario, expected) in new[] { ("unauth", FailureKind.SignIn), ("throttle", FailureKind.RateLimited), ("exit", FailureKind.Connection) })
{
    using var client = new CodexClient(exe, Path.Combine(checkRoot, scenario));
    try { await client.ReadAsync(); throw new Exception("Expected failure: " + scenario); }
    catch (CodexException error) { Check(error.Kind == expected, "failure classified: " + scenario); }
}
using (var client = new CodexClient(exe, Path.Combine(checkRoot, "timeout"), TimeSpan.FromSeconds(2)))
{
    try { await client.ReadAsync(); throw new Exception("Expected timeout"); }
    catch (TimeoutException) { Check(true, "stalled server has bounded timeout"); }
}
using (var client = new CodexClient(exe, Path.Combine(checkRoot, "badlogin")))
{
    try { await client.LoginAsync(_ => throw new Exception("Unsafe URL opened")); throw new Exception("Expected blocked URL"); }
    catch (CodexException error) { Check(error.Kind == FailureKind.Protocol, "untrusted login URL rejected"); }
}
if (args.Length == 2 && args[0] == "--real-codex")
{
    using var client = new CodexClient(args[1], Path.Combine(checkRoot, "real-empty-profile"));
    try { await client.ReadAsync(); throw new Exception("Empty profile unexpectedly authenticated"); }
    catch (CodexException error) { Check(error.Kind == FailureKind.SignIn, "installed Codex handshake and account/read with isolated empty profile"); }
}
Console.WriteLine($"All {passed} checks passed.");

static async Task FakeServer()
{
    string profile = Environment.GetEnvironmentVariable("CODEX_HOME")!;
    File.WriteAllText(Path.Combine(profile, "pid.txt"), Environment.ProcessId.ToString());
    string scenario = Path.GetFileName(profile);
    bool initialized = false, greeted = false;
    while (await Console.In.ReadLineAsync() is { } line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        string method = root.GetProperty("method").GetString()!;
        if (method == "initialized") { initialized = greeted; continue; }
        int id = root.GetProperty("id").GetInt32();
        object result;
        if (method == "initialize") { greeted = true; result = new { userAgent = "checks" }; }
        else if (!initialized) throw new Exception("Client failed handshake");
        else if (method == "account/read") result = scenario == "unauth" ? new { account = (object?)null } :
            new { account = (object?)new { type = "chatgpt", email = scenario + "@example.com", planType = "pro" } };
        else if (method == "account/rateLimits/read")
        {
            if (scenario == "exit") return;
            if (scenario == "timeout") { await Task.Delay(30000); continue; }
            if (scenario == "throttle")
            {
                Console.WriteLine(JsonSerializer.Serialize(new { id, error = new { code = -32000, message = "HTTP 429 Too many requests" } }));
                continue;
            }
            result = new { rateLimits = new { limitId = "codex", primary = new { usedPercent = scenario == "one" ? 62 : 25, windowDurationMins = scenario == "one" ? 10080 : 300 } } };
        }
        else if (method == "account/login/start")
        {
            result = new { type = "chatgpt", loginId = "test-login", authUrl = scenario == "badlogin" ? "https://example.com/steal" : "https://auth.openai.com/authorize" };
            Console.WriteLine(JsonSerializer.Serialize(new { method = "account/login/completed", @params = new { loginId = "test-login", success = true } }));
        }
        else if (method == "account/login/cancel") result = new { };
        else throw new Exception("Unexpected request: " + method);
        Console.WriteLine(JsonSerializer.Serialize(new { id, result }));
    }
}
