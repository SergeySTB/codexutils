using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AIUsageMonitor;

System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ru-RU");

if (args.Length == 2 && args[0] is "--widget" or "--layout" or "--drag" or "--proxy")
{
    try { WidgetChecks.Run(args[1], layoutOnly: args[0] == "--layout", dragOnly: args[0] == "--drag", proxyOnly: args[0] == "--proxy"); }
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
var currentConfig = Path.Combine(checkRoot, "AIUsageMonitor", "config.json");
var legacyConfig = Path.Combine(checkRoot, "CodexLimits", "config.json");
Check(ProductInfo.GetDefaultConfigPath(checkRoot) == currentConfig, "new installs use the renamed configuration path");
Directory.CreateDirectory(Path.GetDirectoryName(legacyConfig)!);
File.WriteAllText(legacyConfig, "{}");
Check(ProductInfo.GetDefaultConfigPath(checkRoot) == legacyConfig, "existing configuration is reused after rename");
Directory.CreateDirectory(Path.GetDirectoryName(currentConfig)!);
File.WriteAllText(currentConfig, "{}");
Check(ProductInfo.GetDefaultConfigPath(checkRoot) == currentConfig, "renamed configuration takes precedence when both exist");

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
using (var claudeResponse = JsonDocument.Parse("""{"five_hour":{"utilization":25.5,"resets_at":"2026-09-24T10:00:00Z"},"seven_day":{"utilization":80,"resets_at":"2026-09-30T10:00:00Z"}}"""))
{
    var claude = ClaudeClient.Parse(claudeResponse.RootElement);
    Check(claude.FiveHour?.Remaining == 74.5 && claude.Weekly?.Remaining == 20 &&
        claude.FiveHour.ResetsAt?.Offset == TimeSpan.Zero, "Claude windows map to remaining quota and UTC reset");
}
using (var badClaude = JsonDocument.Parse("""{"five_hour":{"utilization":-1},"seven_day":{"utilization":null}}"""))
    Check(ClaudeClient.Parse(badClaude.RootElement) is { FiveHour: null, Weekly: null }, "invalid Claude windows stay unavailable");
Check(Limits.WasReset(new(new(99, null), new(100, null)), new(new(100, null), new(100, null))), "reset notification detects a limit reaching 100%");
Check(!Limits.WasReset(new(new(100, null), null), new(new(100, null), new(80, null))), "reset notification ignores unchanged and reduced limits");
using (var fanfare = typeof(WidgetWindow).Assembly.GetManifestResourceStream("AIUsageMonitor.Assets.reset_fanfare.wav"))
{
    Check(fanfare != null, "reset fanfare is embedded");
    using var player = new System.Media.SoundPlayer(fanfare);
    player.Load();
}
var now = DateTimeOffset.UtcNow;
Check(Limits.ResetText(new(10, now.AddSeconds(-1)), now).Contains("Ожидается"), "elapsed reset does not invent a replenished quota");
System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en-US");
Check(Limits.ResetText(new(10, now.AddSeconds(-1)), now).Contains("Waiting"), "English system UI uses English reset text");
System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("de-DE");
Check(Limits.ResetText(new(10, now.AddSeconds(-1)), now).Contains("Waiting"), "other system languages fall back to English");
System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ru-RU");
UiText.SetLanguage("en");
Check(Limits.ResetText(new(10, now.AddSeconds(-1)), now).Contains("Waiting"), "English override works on Russian system");
System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en-US");
UiText.SetLanguage("ru");
Check(Limits.ResetText(new(10, now.AddSeconds(-1)), now).Contains("Ожидается"), "Russian override works on English system");
UiText.SetLanguage("system");
System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ru-RU");

var area = new PixelRect(-1920, 0, 1920, 1040);
var widget = new WidgetSettings { IconWidthPx = 360, IconHeightPx = 144, MarginPx = 8, OffsetPx = 400 };
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
foreach (string edge in new[] { "top", "bottom", "left", "right" })
{
    var target = new PixelRect(-1750, 320, 360, 144);
    var dragged = Placement.FromPosition(screen, target, widget with { Edge = edge }, "secondary");
    Check(Placement.Calculate(screen, dragged, screen) == target && dragged.Edge == edge &&
        dragged.Monitor == "secondary" && !dragged.RespectTaskbar, "drag position round-trips for " + edge);
}
Check(valid.Validate().Accounts.Length == 2, "two profiles accepted");
var claudeProfile = new AccountSettings("Work", Provider: "claude", ClaudeConfigDir: Path.Combine(checkRoot, ".claude-work"));
Check((valid with { Accounts = [valid.Accounts[0], claudeProfile] }).Validate().Accounts[1].Provider == "claude", "Codex and Claude profiles coexist");
var claudeJson = JsonSerializer.Serialize(valid with { Accounts = [claudeProfile] }, Settings.JsonOptions);
File.WriteAllText(Path.Combine(checkRoot, "claude-config.json"), claudeJson);
Check(Settings.Load(Path.Combine(checkRoot, "claude-config.json")).Accounts[0].Provider == "claude", "Claude profile loads from configuration");
Reject(() => (valid with { Accounts = [claudeProfile, claudeProfile] }).Validate(), "duplicate Claude profile rejected");
Check(new Settings().Accounts.Length == 1, "one profile configured by default");
Check((valid with { Accounts = [valid.Accounts[0]] }).Validate().Accounts.Length == 1, "single profile accepted");
var threeAccounts = valid with { Accounts = [valid.Accounts[0], valid.Accounts[1], new("Three", Path.Combine(checkRoot, "three"))] };
Check(threeAccounts.Validate().Accounts.Length == 3, "account count follows configuration");
Check((valid with { Accounts = [] }).Validate().Accounts.Length == 0, "empty account list accepted");
Reject(() => (threeAccounts with { Accounts = [.. threeAccounts.Accounts, threeAccounts.Accounts[0] with { CodexHome = threeAccounts.Accounts[0].CodexHome!.ToUpperInvariant() + "/" }] }).Validate(), "duplicate profile paths rejected across all accounts");
Reject(() => (valid with { RefreshSeconds = 0 }).Validate(), "polling interval validated");
Reject(() => (valid with { Widget = widget with { Edge = "middle" } }).Validate(), "unknown edge rejected");
Reject(() => (valid with { Widget = widget with { Language = "fr" } }).Validate(), "unsupported app language rejected");
Reject(() => (valid with { Widget = widget with { IconWidthPx = 0 } }).Validate(), "zero width rejected");
Check((valid with { Widget = compact with { MarginPx = -50 } }).Validate().Widget.MarginPx == -50, "negative user margin accepted");
Reject(() => (valid with { Widget = compact with { MarginPx = -4097 } }).Validate(), "excessive negative margin rejected");
var configPath = Path.Combine(checkRoot, "config.json");
File.WriteAllText(configPath, "{\"refeshSeconds\":60}");
Reject(() => Settings.Load(configPath), "configuration typos rejected");
var commentedHome = Path.Combine(checkRoot, "commented").Replace("\\", "\\\\");
File.WriteAllText(configPath, $$"""
    {
      // Add another object to accounts for another Codex profile.
      "accounts": [
        { "name": "One", "codexHome": "{{commentedHome}}" } // Keep this account, even with a comma in the comment.
      ]
    }
    """);
Check(Settings.Load(configPath).Accounts.Length == 1, "configuration comments accepted");
var beforeAccounts = Settings.Load(configPath).Accounts;
Settings.EditAccounts(configPath, beforeAccounts, entries => [.. entries, claudeProfile]);
Check(Settings.Load(configPath).Accounts.Length == 2 &&
    File.ReadAllText(configPath).Contains("// Keep this account, even with a comma in the comment."),
    "adding account preserves comments inside the account list");
bool staleRejected = false;
try { Settings.EditAccounts(configPath, beforeAccounts, entries => [.. entries, claudeProfile]); }
catch (IOException) { staleRejected = true; }
Check(staleRejected, "stale account list cannot overwrite new accounts");
var currentAccounts = Settings.Load(configPath).Accounts;
Reject(() => Settings.EditAccounts(configPath, currentAccounts, entries => [.. entries, claudeProfile]),
    "duplicate account cannot overwrite configuration");
Settings.EditAccounts(configPath, currentAccounts, entries => entries[1..]);
Settings.EditAccounts(configPath, Settings.Load(configPath).Accounts, entries => entries[1..]);
Check(Settings.Load(configPath).Accounts.Length == 0 &&
    File.ReadAllText(configPath).Contains("// Keep this account, even with a comma in the comment."),
    "last account can be removed without losing comments");
File.WriteAllText(configPath, JsonSerializer.Serialize(threeAccounts, Settings.JsonOptions));
Settings.EditAccounts(configPath, Settings.Load(configPath).Accounts,
    entries => [entries[0], entries[2]]);
Check(Settings.Load(configPath).Accounts.Select(account => account.Name).SequenceEqual(["One", "Three"]),
    "middle account removal keeps the remaining order");
File.WriteAllText(configPath, $$"""
    {
      "accounts": [{ "name": "One", "codexHome": "%TEMP%/ai-usage-monitor-test" }],
      "widget": { "edge": "left" }
    }
    """);
Settings.EditAccounts(configPath, Settings.Load(configPath).Accounts, entries => [.. entries, claudeProfile]);
Check(File.ReadAllText(configPath).Contains("%TEMP%/ai-usage-monitor-test") &&
    Settings.Load(configPath).Widget.Edge == "left", "adding account retains original path and widget settings");
File.WriteAllText(configPath, $$"""
    {
      // Add another object to accounts for another Codex profile.
      "accounts": [{ "name": "One", "codexHome": "{{commentedHome}}" }]
    }
    """);
Settings.SaveWidgetValues(configPath, new() { ["offsetPx"] = 170, ["marginPx"] = 320 });
Check(Settings.Load(configPath).Widget is { OffsetPx: 170, MarginPx: 320 } &&
    File.ReadAllText(configPath).Contains("// Add another object"), "position saving adds widget without losing comments or accounts");
Settings.SaveWidgetValues(configPath, new() { ["language"] = "en" });
Check(Settings.Load(configPath).Widget.Language == "en" && File.ReadAllText(configPath).Contains("// Add another object"),
    "language selection persists without removing configuration comments");
File.WriteAllText(configPath, """
    { // "offsetPx": 999
      "widget": { "offsetPx": 4, /* retain me */ "edge": "left" }
    }
    """);
Settings.SaveWidgetValues(configPath, new() { ["offsetPx"] = 70, ["marginPx"] = 80, ["monitor"] = "\\\\.\\DISPLAY2" });
Check(Settings.Load(configPath).Widget is { OffsetPx: 70, MarginPx: 80, Edge: "left", Monitor: "\\\\.\\DISPLAY2" } &&
    File.ReadAllText(configPath).Contains("// \"offsetPx\": 999") && File.ReadAllText(configPath).Contains("/* retain me */"),
    "saving edits only actual widget properties and preserves comments");
string beforeInvalidSave = File.ReadAllText(configPath);
Reject(() => Settings.SaveWidgetValues(configPath, new() { ["offsetPx"] = -1 }), "invalid position is not saved");
Check(File.ReadAllText(configPath) == beforeInvalidSave, "failed save preserves original configuration");
var shippedExample = Path.Combine(AppContext.BaseDirectory, "config.example.json");
Check(Settings.Load(shippedExample).Accounts.Length == 1 && File.ReadAllText(shippedExample).Contains("// Add another object"),
    "shipped configuration has one account and an English second-account example");
Check(Settings.Load(shippedExample).Proxy is { Enabled: false, Url: "" }, "shipped proxy address is empty and disabled");
File.WriteAllText(configPath, JsonSerializer.Serialize(valid with { Widget = widget }, Settings.JsonOptions)
    .Replace("iconWidthPx", "widthPx").Replace("iconHeightPx", "heightPx"));
Check(Settings.Load(configPath).Widget is { IconWidthPx: 88, IconHeightPx: 44 }, "old default panel becomes compact without rewriting config");
Check(File.ReadAllText(configPath).Contains("360"), "old configuration file is preserved");
Check((valid with { Widget = new WidgetSettings() }).Validate().Widget is { IconWidthPx: 88, IconHeightPx: 44 }, "compact default accepted");
File.WriteAllText(configPath, JsonSerializer.Serialize(valid with { Widget = widget with { IconWidthPx = 120, IconHeightPx = 60 } }, Settings.JsonOptions));
Check(Settings.Load(configPath).Widget is { IconWidthPx: 120, IconHeightPx: 60 }, "custom icon-panel size preserved");
File.WriteAllText(configPath, """{"widget":{"widthPx":120,"heightPx":60}}""");
Check(Settings.Load(configPath).Widget is { IconWidthPx: 120, IconHeightPx: 60, DisplayMode: "icons" }, "legacy dimensions load without installation");
File.WriteAllText(configPath, """{"widget":{"widthPx":120,"heightPx":60,"iconWidthPx":180,"iconHeightPx":90}}""");
Check(Settings.Load(configPath).Widget is { IconWidthPx: 180, IconHeightPx: 90 }, "new dimension names take precedence");
Reject(() => (valid with { Widget = new() { DisplayMode = "unknown" } }).Validate(), "unknown display mode rejected");
Check((valid with { Widget = new() { DisplayMode = "cards", IconWidthPx = 0, IconHeightPx = -1 } }).Validate().Widget.DisplayMode == "cards", "cards ignore icon dimensions");
Check((valid with { Widget = new() { CardWidthPx = -1, CardHeightPx = -1 } }).Validate().Widget.DisplayMode == "icons", "icons ignore card dimensions");
Reject(() => (valid with { Widget = new() { DisplayMode = "cards", CardWidthPx = -1 } }).Validate(), "invalid card width rejected");
Reject(() => (valid with { Widget = new() { DisplayMode = "cards", CardHeightPx = 31 } }).Validate(), "invalid card height rejected");
Check((valid with { Widget = new() { DisplayMode = "cards", CardWidthPx = 200, CardHeightPx = 200 } }).Validate().Widget.CardWidthPx == 200, "small card dimensions accepted");
Check(Placement.Calculate(area, compact, screen, 700, 320).Width == 700, "placement uses measured card dimensions");

string exe = Environment.ProcessPath!;
File.WriteAllText(configPath, """
    { // Keep this comment and the widget position.
      "widget": { "offsetPx": 321 }
    }
    """);
Settings.EnsureProxySettings(configPath);
Check(Settings.Load(configPath).Proxy is { Enabled: false, Url: "" } &&
    File.ReadAllText(configPath).Contains("// Keep this comment"), "old configuration gets empty disabled proxy without losing comments");
string emptyProxyConfig = File.ReadAllText(configPath);
Settings.EnsureProxySettings(configPath);
Check(File.ReadAllText(configPath) == emptyProxyConfig, "proxy migration is idempotent");
Reject(() => Settings.SaveProxyEnabled(configPath, true), "empty proxy cannot be enabled");
Check(File.ReadAllText(configPath) == emptyProxyConfig, "failed proxy toggle preserves configuration");
var configuredProxy = new ProxySettings { Enabled = true, Url = "http://proxy.example:8080" };
File.WriteAllText(configPath, emptyProxyConfig.Replace("\"url\":\"\"", "\"url\":\"http://proxy.example:8080\""));
Settings.SaveProxyEnabled(configPath, true);
Check(Settings.Load(configPath).Proxy == configuredProxy && Settings.Load(configPath).Widget.OffsetPx == 321 &&
    File.ReadAllText(configPath).Contains("// Keep this comment"), "proxy toggle reads file address and preserves unrelated settings");
Settings.SaveProxyEnabled(configPath, false);
Check(Settings.Load(configPath).Proxy is { Enabled: false, Url: "http://proxy.example:8080" }, "disabling proxy retains its address");
foreach (string url in new[] { "", "ftp://proxy.example", "http://user:secret@proxy.example:8080", "http://proxy.example:8080/path", "http://proxy.example:0", "http://proxy.example:8080?query=1", " http://proxy.example:8080" })
    Reject(() => (valid with { Proxy = configuredProxy with { Url = url } }).Validate(), "invalid proxy rejected: " + url.Replace("secret", "***"));
Reject(() => (valid with { Proxy = null! }).Validate(), "null proxy rejected");
using (var handler = new ProxySettings().CreateHandler())
    Check(!handler.UseProxy && !handler.AllowAutoRedirect, "disabled proxy uses direct Claude connection without redirects");
using (var handler = configuredProxy.CreateHandler())
    Check(handler.UseProxy && handler.Proxy!.GetProxy(new Uri("https://api.anthropic.com")) == new Uri(configuredProxy.Url), "Claude handler routes HTTPS through configured proxy");
using (var handler = (configuredProxy with { Url = "https://proxy.example:8443" }).CreateHandler())
    Check(handler.Proxy!.GetProxy(new Uri("https://api.anthropic.com"))!.Scheme == "https", "HTTPS proxy address accepted");
var childStart = new ProcessStartInfo(exe);
childStart.Environment["HTTPS_PROXY"] = "http://inherited.example:9999";
childStart.Environment["ALL_PROXY"] = "http://inherited.example:9999";
new ProxySettings().ConfigureProcess(childStart);
Check(!childStart.Environment.ContainsKey("HTTPS_PROXY") && !childStart.Environment.ContainsKey("ALL_PROXY") &&
    childStart.Environment["NO_PROXY"] == "*", "disabled proxy clears inherited child proxy and bypasses system proxy");
configuredProxy.ConfigureProcess(childStart);
Check(childStart.Environment["HTTPS_PROXY"] == configuredProxy.Url && childStart.Environment["NO_PROXY"] == "localhost,127.0.0.1,::1",
    "enabled child proxy replaces inherited values and bypasses loopback");
var proxyListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
proxyListener.Start();
try
{
    var endpoint = (System.Net.IPEndPoint)proxyListener.LocalEndpoint;
    string claudeDir = Path.Combine(checkRoot, "claude-proxy");
    Directory.CreateDirectory(claudeDir);
    File.WriteAllText(Path.Combine(claudeDir, ".credentials.json"), JsonSerializer.Serialize(new
        { claudeAiOauth = new { accessToken = "dummy-check-token", expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds() } }));
    using var claudeClient = new ClaudeClient(claudeDir, configuredProxy with { Url = $"http://127.0.0.1:{endpoint.Port}" });
    var request = claudeClient.ReadAsync();
    using var peer = await proxyListener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
    using var stream = peer.GetStream();
    using var reader = new StreamReader(stream, leaveOpen: true);
    string? connect = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
    try { await request.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Expected proxy connection failure"); }
    catch (System.Net.Http.HttpRequestException) { }
    Check(connect == "CONNECT api.anthropic.com:443 HTTP/1.1", "Claude actually uses HTTPS CONNECT through local test proxy");
}
finally { proxyListener.Stop(); }

await CodexChecks.RunAsync(Check, checkRoot);
Console.WriteLine($"All {passed} checks passed.");
