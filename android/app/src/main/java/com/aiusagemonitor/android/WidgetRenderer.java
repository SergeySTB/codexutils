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
import android.os.Bundle;
import android.util.SizeF;
import android.view.View;
import android.widget.RemoteViews;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

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
            Bundle options = manager.getAppWidgetOptions(id);
            if (Build.VERSION.SDK_INT >= 31 && options != null) {
                ArrayList<SizeF> sizes = options.getParcelableArrayList(AppWidgetManager.OPTION_APPWIDGET_SIZES);
                if (sizes != null && !sizes.isEmpty()) {
                    Map<SizeF, RemoteViews> layouts = new HashMap<>();
                    for (SizeF size : sizes)
                        layouts.put(size, iconViews(context, accounts, unavailable, Math.round(size.getWidth())));
                    manager.updateAppWidget(id, new RemoteViews(layouts));
                    continue;
                }
            }
            int width = options == null ? 120 : options.getInt(AppWidgetManager.OPTION_APPWIDGET_MIN_WIDTH, 120);
            manager.updateAppWidget(id, iconViews(context, accounts, unavailable, width));
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

    private static RemoteViews iconViews(Context context, List<AccountStore.Account> accounts,
                                         boolean unavailable, int widthDp) {
        RemoteViews view = new RemoteViews(context.getPackageName(), R.layout.widget_icons);
        view.removeAllViews(R.id.widget_icons_list);
        view.setViewVisibility(R.id.widget_icons_empty, accounts.isEmpty() ? View.VISIBLE : View.GONE);
        int slots = WidgetIconSlots.capacity(widthDp > 0 ? widthDp : 120);
        view.setTextViewText(R.id.widget_icons_empty, unavailable
            ? (slots == 1 ? "!" : "Нет данных") : (slots == 1 ? "Нет" : "Нет аккаунтов"));
        view.setContentDescription(R.id.widget_icons_empty, unavailable
            ? "Сохранённые данные недоступны" : "Нет аккаунтов. Откройте приложение, чтобы добавить аккаунт");
        int visible = WidgetIconSlots.visible(slots, accounts.size());
        int hidden = accounts.size() - visible;
        boolean overflowTile = hidden > 0 && slots > 1;
        view.setViewVisibility(R.id.widget_icons_overflow, overflowTile ? View.VISIBLE : View.GONE);
        if (overflowTile) {
            view.setTextViewText(R.id.widget_icons_overflow, "+" + hidden);
            view.setContentDescription(R.id.widget_icons_overflow, "Ещё " + hidden + " аккаунтов");
        }
        for (int index = 0; index < visible; index++) {
            AccountStore.Account account = accounts.get(index);
            RemoteViews icon = new RemoteViews(context.getPackageName(), R.layout.widget_icon);
            icon.setImageViewBitmap(R.id.widget_icon_image, iconBitmap(context, account));
            boolean badge = hidden > 0 && !overflowTile;
            icon.setContentDescription(R.id.widget_icon_image, description(account) +
                (badge ? ". Ещё " + hidden + " аккаунтов не показано" : ""));
            if (badge) {
                icon.setViewVisibility(R.id.widget_icon_more, View.VISIBLE);
                icon.setTextViewText(R.id.widget_icon_more, "+" + hidden);
            }
            view.addView(R.id.widget_icons_list, icon);
        }
        view.setOnClickPendingIntent(R.id.widget_icons_root, openApp(context, 1, false));
        return view;
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
        int pixels = Math.max(1, Math.round(48 * density));
        Bitmap bitmap = Bitmap.createBitmap(pixels, pixels, Bitmap.Config.ARGB_8888);
        Canvas canvas = new Canvas(bitmap);
        Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        paint.setStyle(Paint.Style.STROKE);
        paint.setStrokeWidth(2.5f * density);
        paint.setStrokeCap(Paint.Cap.ROUND);
        float center = pixels / 2f;
        Usage usage = account.usage;
        ring(canvas, paint, center, 20 * density, usage == null ? null : usage.fiveHour, MINT, account.error != null);
        ring(canvas, paint, center, 15 * density, usage == null ? null : usage.weekly, PURPLE, account.error != null);
        paint.setStyle(Paint.Style.FILL);
        paint.setColor(TEXT);
        paint.setTextSize(14 * density);
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
