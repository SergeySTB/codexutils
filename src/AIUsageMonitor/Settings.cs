using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AIUsageMonitor;

public sealed record AccountSettings(string Name, string? CodexHome = null, string Provider = "codex", string? ClaudeConfigDir = null);

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
        new("Личный", "%LOCALAPPDATA%/AIUsageMonitor/profiles/personal")
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
            string.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 60 ||
            a.Provider is not ("codex" or "claude") ||
            (a.Provider == "codex" && (string.IsNullOrWhiteSpace(a.CodexHome) || a.ClaudeConfigDir != null)) ||
            (a.Provider == "claude" && (a.CodexHome != null || string.IsNullOrWhiteSpace(a.ClaudeConfigDir)))))
            throw new InvalidDataException("Укажите имя, provider (codex/claude) и соответствующий codexHome или claudeConfigDir.");
        var accounts = Accounts.Select(a => a.Provider == "codex"
            ? a with { CodexHome = ExpandPath(a.CodexHome!) }
            : a with { ClaudeConfigDir = ExpandPath(a.ClaudeConfigDir!) }).ToArray();
        if (accounts.Where(a => a.Provider == "codex").Select(a => a.CodexHome).Distinct(StringComparer.OrdinalIgnoreCase).Count() != accounts.Count(a => a.Provider == "codex") ||
            accounts.Where(a => a.Provider == "claude").Select(a => a.ClaudeConfigDir).Distinct(StringComparer.OrdinalIgnoreCase).Count() != accounts.Count(a => a.Provider == "claude"))
            throw new InvalidDataException("Для аккаунтов одного провайдера нужны разные папки профиля.");
        if (Widget is null || Widget.DisplayMode is not ("icons" or "cards") ||
            (Widget.DisplayMode == "icons" && (Widget.IconWidthPx < 64 || Widget.IconWidthPx > 4096 ||
                Widget.IconHeightPx < 32 || Widget.IconHeightPx > 2160)) ||
            (Widget.DisplayMode == "cards" &&
                ((Widget.CardWidthPx != 0 && (Widget.CardWidthPx < 64 || Widget.CardWidthPx > 4096)) ||
                 (Widget.CardHeightPx != 0 && (Widget.CardHeightPx < 32 || Widget.CardHeightPx > 2160)))) || Widget.MarginPx < -4096 ||
            Widget.MarginPx > 100000 || Widget.OffsetPx < 0 || Widget.OffsetPx > 100000 ||
            string.IsNullOrWhiteSpace(Widget.Monitor) ||
            Widget.Edge is not ("top" or "bottom" or "left" or "right"))
            throw new InvalidDataException("Проверьте widget: displayMode icons/cards, иконки и карточки 64×32–4096×2160 (0 — авто только для карточек), marginPx от -4096 до 100000, offsetPx от 0 до 100000, edge: top/bottom/left/right.");
        if (RefreshSeconds < 30 || RefreshSeconds > 3600)
            throw new InvalidDataException("refreshSeconds должен быть от 30 до 3600.");
        if (CodexExecutable is null) throw new InvalidDataException("codexExecutable должен быть строкой.");
        return this with { Accounts = accounts };
    }

    public static void SaveWidgetValues(string path, Dictionary<string, object> values)
    {
        // Edit JSON tokens, preserving comments, formatting and unrelated settings.
        byte[] original = File.ReadAllBytes(path);
        byte[] json = original.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? original[3..] : original;
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        var edits = new List<(int Start, int Length, byte[] Value)>();
        var missing = new Dictionary<string, object>(values);
        int widgetStart = -1, rootStart = -1;
        bool hasWidgetProperties = false, hasRootProperties = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == 0) rootStart = (int)reader.BytesConsumed;
            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1) continue;
            hasRootProperties = true;
            if (!reader.ValueTextEquals("widget")) { reader.Read(); reader.Skip(); continue; }
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject) throw new InvalidDataException("widget должен быть объектом.");
            widgetStart = (int)reader.BytesConsumed;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string key = reader.GetString()!;
                hasWidgetProperties = true;
                reader.Read();
                int start = (int)reader.TokenStartIndex;
                reader.Skip();
                if (values.TryGetValue(key, out var value))
                {
                    edits.Add((start, (int)reader.BytesConsumed - start, JsonSerializer.SerializeToUtf8Bytes(value)));
                    missing.Remove(key);
                }
            }
        }
        if (rootStart < 0) throw new InvalidDataException("Пустой конфигурационный файл.");
        if (missing.Count > 0)
        {
            string members = JsonSerializer.Serialize(missing)[1..^1];
            string insert = widgetStart >= 0 ? members + (hasWidgetProperties ? "," : "")
                : "\"widget\":{" + members + "}" + (hasRootProperties ? "," : "");
            edits.Add((widgetStart >= 0 ? widgetStart : rootStart, 0, Encoding.UTF8.GetBytes(insert)));
        }
        var updated = json.ToList();
        foreach (var edit in edits.OrderByDescending(e => e.Start))
        {
            updated.RemoveRange(edit.Start, edit.Length);
            updated.InsertRange(edit.Start, edit.Value);
        }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, updated.ToArray());
            Load(temporary);
            if (!File.ReadAllBytes(path).SequenceEqual(original)) throw new IOException("Конфигурация изменена другим процессом. Повторите действие.");
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
