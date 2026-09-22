package com.aiusagemonitor.android;

import android.appwidget.AppWidgetManager;
import android.appwidget.AppWidgetProvider;
import android.content.Context;
import android.os.Bundle;

public final class IconsWidget extends AppWidgetProvider {
    @Override public void onUpdate(Context context, AppWidgetManager manager, int[] ids) {
        WidgetRenderer.showCached(context);
        WidgetRefreshJob.schedulePeriodic(context);
        WidgetRefreshJob.scheduleImmediate(context);
    }

    @Override public void onAppWidgetOptionsChanged(Context context, AppWidgetManager manager,
                                                     int id, Bundle options) {
        WidgetRenderer.showCached(context);
    }

    @Override public void onDisabled(Context context) { WidgetRefreshJob.cancelIfUnused(context); }
}
