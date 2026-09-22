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

    static void demoCheck() throws Exception {
        JSONObject response = new JSONObject("{\"rate_limit\":{\"primary_window\":{\"used_percent\":25.5,\"limit_window_seconds\":604800,\"reset_at\":2000000000},\"secondary_window\":{\"used_percent\":80,\"limit_window_seconds\":18000,\"reset_at\":2000000100}}}");
        Usage result = parse(response);
        Usage weeklyOnly = parse(new JSONObject("{\"rate_limit\":{\"primary_window\":{\"used_percent\":5,\"limit_window_seconds\":604800}}}"));
        if (result.fiveHour == null || result.fiveHour.remaining != 20 ||
            result.weekly == null || result.weekly.remaining != 74 ||
            weeklyOnly.fiveHour != null || weeklyOnly.weekly == null || weeklyOnly.weekly.remaining != 95 ||
            parse(new JSONObject("{}")).fiveHour != null)
            throw new AssertionError("Codex usage parsing failed");
    }
}
