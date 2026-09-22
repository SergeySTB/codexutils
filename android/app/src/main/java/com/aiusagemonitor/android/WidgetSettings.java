package com.aiusagemonitor.android;

import android.content.Context;

final class WidgetSettings {
    static final int[] INTERVALS = {15, 30, 60, 120, 360, 720, 1440};
    static final int DEFAULT_MINUTES = 30;
    private static final String PREFERENCES = "widget_settings";
    private static final String INTERVAL = "refresh_minutes";

    static int normalized(int minutes) {
        for (int value : INTERVALS) if (value == minutes) return value;
        return DEFAULT_MINUTES;
    }

    static int read(Context context) {
        return normalized(context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
            .getInt(INTERVAL, DEFAULT_MINUTES));
    }

    static boolean save(Context context, int minutes) {
        if (normalized(minutes) != minutes) return false;
        return context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
            .edit().putInt(INTERVAL, minutes).commit();
    }
}
