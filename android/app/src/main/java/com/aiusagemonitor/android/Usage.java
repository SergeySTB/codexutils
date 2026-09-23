package com.aiusagemonitor.android;

import org.json.JSONObject;

final class Usage {
    static final class Window {
        final int remaining;
        final long resetsAt;
        Window(int remaining, long resetsAt) {
            this.remaining = remaining;
            this.resetsAt = resetsAt;
        }
    }

    final Window fiveHour;
    final Window weekly;

    private Usage(Window fiveHour, Window weekly) {
        this.fiveHour = fiveHour;
        this.weekly = weekly;
    }

    static Usage fromSaved(JSONObject json) {
        if (json == null) return null;
        return new Usage(savedWindow(json.optJSONObject("fiveHour")), savedWindow(json.optJSONObject("weekly")));
    }

    JSONObject toJson() throws Exception {
        return new JSONObject().put("fiveHour", saveWindow(fiveHour)).put("weekly", saveWindow(weekly));
    }

    private static JSONObject saveWindow(Window window) throws Exception {
        return window == null ? null : new JSONObject()
            .put("remaining", window.remaining).put("resetsAt", window.resetsAt);
    }

    private static Window savedWindow(JSONObject json) {
        if (json == null) return null;
        int remaining = json.optInt("remaining", -1);
        long reset = json.optLong("resetsAt", 0);
        return remaining < 0 || remaining > 100 || reset < 0 || reset > 253_402_300_799L
            ? null : new Window(remaining, reset);
    }

    static Usage parse(JSONObject response) {
        JSONObject limit = response.optJSONObject("rate_limit");
        Window fiveHour = null;
        Window weekly = null;
        if (limit != null) {
            for (String key : new String[] {"primary_window", "secondary_window"}) {
                JSONObject value = limit.optJSONObject(key);
                if (value == null || !value.has("used_percent")) continue;
                double used = value.optDouble("used_percent", Double.NaN);
                if (!Double.isFinite(used) || used < 0) continue;
                int seconds = value.optInt("limit_window_seconds", -1);
                long reset = value.optLong("reset_at", 0);
                if (reset < 0 || reset > 253_402_300_799L) reset = 0;
                Window window = new Window((int) Math.floor(Math.max(0, Math.min(100, 100 - used))), reset);
                if (seconds == 18_000) fiveHour = window;
                if (seconds == 604_800) weekly = window;
            }
        }
        return new Usage(fiveHour, weekly);
    }

    static Usage parseClaude(JSONObject response) {
        return new Usage(claudeWindow(response.optJSONObject("five_hour")),
            claudeWindow(response.optJSONObject("seven_day")));
    }

    private static Window claudeWindow(JSONObject value) {
        if (value == null || !value.has("utilization")) return null;
        double used = value.optDouble("utilization", Double.NaN);
        if (!Double.isFinite(used) || used < 0 || used > 100) return null;
        long reset = 0;
        String date = value.optString("resets_at", "");
        if (!date.isEmpty()) {
            try { reset = java.time.OffsetDateTime.parse(date).toEpochSecond(); }
            catch (java.time.format.DateTimeParseException ignored) { }
        }
        if (reset < 0 || reset > 253_402_300_799L) reset = 0;
        return new Window((int) Math.floor(100 - used), reset);
    }

    static void demoCheck() throws Exception {
        JSONObject response = new JSONObject("{\"rate_limit\":{\"primary_window\":{\"used_percent\":25.5,\"limit_window_seconds\":604800,\"reset_at\":2000000000},\"secondary_window\":{\"used_percent\":80,\"limit_window_seconds\":18000,\"reset_at\":2000000100}}}");
        Usage result = parse(response);
        Usage claude = parseClaude(new JSONObject("{\"five_hour\":{\"utilization\":25.5,\"resets_at\":\"2026-09-24T10:00:00Z\"},\"seven_day\":{\"utilization\":80}}"));
        Usage invalidClaude = parseClaude(new JSONObject("{\"five_hour\":{\"utilization\":-1},\"seven_day\":null}"));
        Usage weeklyOnly = parse(new JSONObject("{\"rate_limit\":{\"primary_window\":{\"used_percent\":5,\"limit_window_seconds\":604800}}}"));
        if (result.fiveHour == null || result.fiveHour.remaining != 20 ||
            result.weekly == null || result.weekly.remaining != 74 ||
            weeklyOnly.fiveHour != null || weeklyOnly.weekly == null || weeklyOnly.weekly.remaining != 95 ||
            parse(new JSONObject("{}")).fiveHour != null ||
            claude.fiveHour == null || claude.fiveHour.remaining != 74 || claude.weekly == null ||
            claude.weekly.remaining != 20 || invalidClaude.fiveHour != null || invalidClaude.weekly != null)
            throw new AssertionError("Codex usage parsing failed");
    }
}
