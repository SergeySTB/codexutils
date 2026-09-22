package com.aiusagemonitor.android;

import android.app.PendingIntent;
import android.appwidget.AppWidgetManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.net.Uri;
import android.os.Build;
import android.view.View;
import android.widget.RemoteViews;

import java.util.ArrayList;
import java.util.List;

final class WidgetRenderer {
    private static final int MINT = Color.rgb(100, 226, 204);
    private static final int PURPLE = Color.rgb(177, 151, 250);
    private static final int TRACK = Color.rgb(54, 65, 83);
    private static final int TEXT = Color.rgb(238, 245, 250);

    static boolean hasWidgets(Context context) {
        AppWidgetManager manager = AppWidgetManager.getInstance(context);
        return manager.getAppWidgetIds(new ComponentName(context, IconsWidget.class)).length > 0 ||
            manager.getAppWidgetIds(new ComponentName(context, CardsWidget.class)).length > 0;
    }

    static void showCached(Context context) {
        AppWidgetManager manager = AppWidgetManager.getInstance(context);
        int[] icons = manager.getAppWidgetIds(new ComponentName(context, IconsWidget.class));
        int[] cards = manager.getAppWidgetIds(new ComponentName(context, CardsWidget.class));
        if (icons.length == 0 && cards.length == 0) return;
        List<AccountStore.Account> accounts;
        boolean unavailable = false;
        try { accounts = new AccountStore(context).load(); }
        catch (Exception error) { accounts = new ArrayList<>(); unavailable = true; }
        for (int id : icons) {
            RemoteViews view = new RemoteViews(context.getPackageName(), R.layout.widget_icons);
            view.removeAllViews(R.id.widget_icons_list);
            view.setViewVisibility(R.id.widget_icons_empty, accounts.isEmpty() ? View.VISIBLE : View.GONE);
            view.setTextViewText(R.id.widget_icons_empty,
                unavailable ? "Данные недоступны" : "Добавьте аккаунт");
            for (AccountStore.Account account : accounts) {
                RemoteViews icon = new RemoteViews(context.getPackageName(), R.layout.widget_icon);
                icon.setImageViewBitmap(R.id.widget_icon_image, iconBitmap(context, account));
                icon.setContentDescription(R.id.widget_icon_image, description(account));
                view.addView(R.id.widget_icons_list, icon);
            }
            view.setOnClickPendingIntent(R.id.widget_icons_root, openApp(context, 1, false));
            manager.updateAppWidget(id, view);
        }
        for (int id : cards) {
            RemoteViews view = new RemoteViews(context.getPackageName(), R.layout.widget_cards);
            Intent adapter = new Intent(context, WidgetCardsService.class);
            adapter.putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, id);
            adapter.setData(Uri.parse("aiusagemonitor://cards/" + id));
            view.setRemoteAdapter(R.id.widget_cards_list, adapter);
            view.setPendingIntentTemplate(R.id.widget_cards_list, openApp(context, id, true));
            manager.updateAppWidget(id, view);
            manager.notifyAppWidgetViewDataChanged(id, R.id.widget_cards_list);
        }
    }

    private static PendingIntent openApp(Context context, int requestCode, boolean mutable) {
        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (mutable) {
            if (Build.VERSION.SDK_INT >= 31) flags |= PendingIntent.FLAG_MUTABLE;
        } else flags |= PendingIntent.FLAG_IMMUTABLE;
        Intent intent = new Intent(context, MainActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        return PendingIntent.getActivity(context, requestCode, intent, flags);
    }

    static String description(AccountStore.Account account) {
        String name = account.email.isEmpty() ? "Аккаунт ChatGPT" : account.email;
        Usage usage = account.usage;
        return name + ". 5 часов: " + value(usage == null ? null : usage.fiveHour) +
            ". Неделя: " + value(usage == null ? null : usage.weekly) +
            (account.error == null ? "" : usage == null ? ". Ошибка обновления" : ". Данные устарели");
    }

    static String value(Usage.Window window) {
        return window == null ? "нет данных" : window.remaining + "% осталось";
    }

    private static Bitmap iconBitmap(Context context, AccountStore.Account account) {
        float density = context.getResources().getDisplayMetrics().density;
        int pixels = Math.max(1, Math.round(38 * density));
        Bitmap bitmap = Bitmap.createBitmap(pixels, pixels, Bitmap.Config.ARGB_8888);
        Canvas canvas = new Canvas(bitmap);
        Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        paint.setStyle(Paint.Style.STROKE);
        paint.setStrokeWidth(2 * density);
        paint.setStrokeCap(Paint.Cap.ROUND);
        float center = pixels / 2f;
        Usage usage = account.usage;
        ring(canvas, paint, center, 15 * density, usage == null ? null : usage.fiveHour, MINT, account.error != null);
        ring(canvas, paint, center, 11.5f * density, usage == null ? null : usage.weekly, PURPLE, account.error != null);
        paint.setStyle(Paint.Style.FILL);
        paint.setColor(TEXT);
        paint.setTextSize(12 * density);
        paint.setTypeface(android.graphics.Typeface.create("sans-serif-medium", android.graphics.Typeface.NORMAL));
        String label = account.email.trim();
        String name = label.isEmpty() ? "?" :
            new String(Character.toChars(Character.toUpperCase(label.codePointAt(0))));
        canvas.drawText(name, center - paint.measureText(name) / 2, center - (paint.ascent() + paint.descent()) / 2, paint);
        return bitmap;
    }

    private static void ring(Canvas canvas, Paint paint, float center, float radius,
                             Usage.Window window, int color, boolean stale) {
        paint.setColor(TRACK);
        canvas.drawCircle(center, center, radius, paint);
        if (window == null || window.remaining == 0) return;
        paint.setColor(color);
        paint.setAlpha(stale ? 115 : 255);
        canvas.drawArc(center - radius, center - radius, center + radius, center + radius,
            -90, 360f * window.remaining / 100, false, paint);
        paint.setAlpha(255);
    }
}
