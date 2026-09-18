using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CodexLimits;

public sealed record AccountSettings(string Name, string CodexHome);

public sealed record WidgetSettings
{
    public string DisplayMode { get; init; } = "icons";
    public int IconWidthPx { get; init; } = 88;
    public int IconHeightPx { get; init; } = 44;
    public int CardWidthPx { get; init; }
    public int CardHeightPx { get; init; }
    public string Monitor { get; init; } = "primary";
    public string Edge { get; init; } = "top";
    public int OffsetPx { get; init; } = 400;
    public int MarginPx { get; init; } = 8;
    public bool AlwaysOnTop { get; init; } = true;
    public bool RespectTaskbar { get; init; } = true;
}

public sealed record Settings
{
    public string CodexExecutable { get; init; } = "";
    public AccountSettings[] Accounts { get; init; } =
    [
        new("Личный", "%LOCALAPPDATA%/CodexLimits/profiles/personal")
    ];
    public WidgetSettings Widget { get; init; } = new();
    public int RefreshSeconds { get; init; } = 60;
    public bool NotifyOnLimitReset { get; init; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static Settings Load(string path)
    {
        var document = JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip });
        if (document is JsonObject root && root["widget"] is JsonObject widget)
        {
            // Rename legacy dimensions in memory; explicit new names take precedence.
            bool legacySize = widget.ContainsKey("widthPx") && widget.ContainsKey("heightPx") &&
                !widget.ContainsKey("iconWidthPx") && !widget.ContainsKey("iconHeightPx");
            foreach (var (oldName, newName) in new[] { ("widthPx", "iconWidthPx"), ("heightPx", "iconHeightPx") })
            {
                var value = widget[oldName];
                if (widget.Remove(oldName) && !widget.ContainsKey(newName)) widget[newName] = value;
            }
            if (legacySize && widget["iconWidthPx"]?.ToJsonString() == "360" && widget["iconHeightPx"]?.ToJsonString() == "144")
            {
                widget["iconWidthPx"] = 88;
                widget["iconHeightPx"] = 44;
            }
        }
        var settings = document.Deserialize<Settings>(JsonOptions)
            ?? throw new InvalidDataException("Пустой конфигурационный файл.");
        return settings.Validate();
    }

    public Settings Validate()
    {
        if (Accounts is null || Accounts.Length == 0 || Accounts.Any(a => a is null ||
            string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 60 || string.IsNullOrWhiteSpace(a.CodexHome)))
            throw new InvalidDataException("Укажите хотя бы один аккаунт с именем и папкой codexHome.");
        var accounts = Accounts.Select(a => a with { CodexHome = ExpandPath(a.CodexHome) }).ToArray();
        if (accounts.Select(a => a.CodexHome).Distinct(StringComparer.OrdinalIgnoreCase).Count() != accounts.Length)
            throw new InvalidDataException("Для аккаунтов нужны разные папки codexHome.");
        if (Widget is null || Widget.DisplayMode is not ("icons" or "cards") ||
            (Widget.DisplayMode == "icons" && (Widget.IconWidthPx < 64 || Widget.IconWidthPx > 4096 ||
                Widget.IconHeightPx < 32 || Widget.IconHeightPx > 2160)) ||
            (Widget.DisplayMode == "cards" &&
                ((Widget.CardWidthPx != 0 && (Widget.CardWidthPx < 240 || Widget.CardWidthPx > 4096)) ||
                 (Widget.CardHeightPx != 0 && (Widget.CardHeightPx < 120 || Widget.CardHeightPx > 2160)))) || Widget.MarginPx < -4096 ||
            Widget.MarginPx > 4096 || Widget.OffsetPx < 0 || Widget.OffsetPx > 100000 ||
            string.IsNullOrWhiteSpace(Widget.Monitor) ||
            Widget.Edge is not ("top" or "bottom" or "left" or "right"))
            throw new InvalidDataException("Проверьте widget: displayMode icons/cards, иконки 64×32–4096×2160, карточки 240×120–4096×2160 (0 — авто), marginPx от -4096 до 4096, offsetPx от 0 до 100000, edge: top/bottom/left/right.");
        if (RefreshSeconds < 30 || RefreshSeconds > 3600)
            throw new InvalidDataException("refreshSeconds должен быть от 30 до 3600.");
        if (CodexExecutable is null) throw new InvalidDataException("codexExecutable должен быть строкой.");
        return this with { Accounts = accounts };
    }

    public static string ExpandPath(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value);
        if (!Path.IsPathFullyQualified(expanded) || expanded.Contains('%'))
            throw new InvalidDataException("Нужен абсолютный путь или существующая переменная окружения: " + value);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
    }

    public string FindCodex()
    {
        if (!string.IsNullOrWhiteSpace(CodexExecutable))
        {
            var path = ExpandPath(CodexExecutable);
            if (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("codexExecutable должен указывать на существующий codex.exe.");
            return path;
        }
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.Combine(p.Trim('"'), "codex.exe"))
            .Prepend(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "OpenAI", "Codex", "bin", "codex.exe"));
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Codex не найден. Укажите полный путь к codex.exe в codexExecutable.");
    }
}
