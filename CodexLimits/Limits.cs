using System;
using System.Text.Json;

namespace CodexLimits;

public sealed record LimitWindow(double Remaining, DateTimeOffset? ResetsAt);
public sealed record Limits(LimitWindow? FiveHour, LimitWindow? Weekly)
{
    public static bool WasReset(Limits previous, Limits current) =>
        WasReset(previous.FiveHour, current.FiveHour) || WasReset(previous.Weekly, current.Weekly);

    private static bool WasReset(LimitWindow? previous, LimitWindow? current) =>
        previous is { Remaining: < 100 } && current is { Remaining: >= 100 };

    public static Limits Parse(JsonElement result)
    {
        JsonElement bucket;
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
        {
            // Never substitute Spark or another model's quota for the main Codex bucket.
            if (!buckets.TryGetProperty("codex", out bucket)) return new(null, null);
        }
        else if (!result.TryGetProperty("rateLimits", out bucket)) return new(null, null);
        if (bucket.ValueKind != JsonValueKind.Object) return new(null, null);
        if (bucket.TryGetProperty("limitId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() != "codex")
            return new(null, null);

        LimitWindow? fiveHour = null, weekly = null;
        foreach (var key in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object ||
                !window.TryGetProperty("windowDurationMins", out var duration) || duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out var minutes) ||
                !window.TryGetProperty("usedPercent", out var used) || used.ValueKind != JsonValueKind.Number ||
                !used.TryGetDouble(out var percent) || !double.IsFinite(percent) || percent < 0)
                continue;
            DateTimeOffset? reset = null;
            if (window.TryGetProperty("resetsAt", out var time) && time.ValueKind == JsonValueKind.Number &&
                time.TryGetInt64(out var seconds) && seconds >= 0 && seconds <= 253402300799)
                reset = DateTimeOffset.FromUnixTimeSeconds(seconds);
            var parsed = new LimitWindow(Math.Clamp(100 - percent, 0, 100), reset);
            if (minutes == 300) fiveHour = parsed;
            if (minutes == 10080) weekly = parsed;
        }
        return new(fiveHour, weekly);
    }

    public static string ResetText(LimitWindow? window, DateTimeOffset now)
    {
        if (window?.ResetsAt is not { } reset) return "Время сброса неизвестно";
        var delta = reset - now;
        if (delta <= TimeSpan.Zero) return "Ожидается обновление лимита";
        if (delta.TotalDays >= 1) return $"Сброс через {(int)delta.TotalDays} д {delta.Hours} ч";
        if (delta.TotalHours >= 1) return $"Сброс через {(int)delta.TotalHours} ч {delta.Minutes} мин";
        return $"Сброс через {Math.Max(1, (int)Math.Ceiling(delta.TotalMinutes))} мин";
    }
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height);
public static class Placement
{
    public static PixelRect Calculate(PixelRect area, WidgetSettings settings, PixelRect? screen = null)
    {
        // A negative margin may cross the work-area edge, but never the physical screen edge.
        var bounds = settings.MarginPx < 0 ? screen ?? area : area;
        var width = Math.Min(settings.WidthPx, bounds.Width);
        var height = Math.Min(settings.HeightPx, bounds.Height);
        var x = settings.Edge switch
        {
            "right" => area.Width - width - settings.MarginPx,
            "left" => settings.MarginPx,
            _ => settings.OffsetPx
        };
        var y = settings.Edge switch
        {
            "bottom" => area.Height - height - settings.MarginPx,
            "top" => settings.MarginPx,
            _ => settings.OffsetPx
        };
        return new(Math.Clamp(area.X + x, bounds.X, bounds.X + bounds.Width - width),
            Math.Clamp(area.Y + y, bounds.Y, bounds.Y + bounds.Height - height), width, height);
    }
}
