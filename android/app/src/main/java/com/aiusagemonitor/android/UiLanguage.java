package com.aiusagemonitor.android;

import android.content.Context;
import android.content.res.Configuration;

import java.util.Locale;

final class UiLanguage {
    private static final String KEY = "language";

    static String selected(Context context) {
        return context.getSharedPreferences("ui", Context.MODE_PRIVATE).getString(KEY, "system");
    }

    static boolean save(Context context, String language) {
        if (!language.equals("system") && !language.equals("ru") && !language.equals("en"))
            throw new IllegalArgumentException("Unsupported UI language");
        return context.getSharedPreferences("ui", Context.MODE_PRIVATE).edit().putString(KEY, language).commit();
    }

    static Context wrap(Context context) {
        String language = selected(context);
        if (language.equals("system")) return context;
        Configuration configuration = new Configuration(context.getResources().getConfiguration());
        configuration.setLocale(Locale.forLanguageTag(language));
        return context.createConfigurationContext(configuration);
    }
}
