package com.aiusagemonitor.android;

import android.content.Context;
import android.content.Intent;
import android.widget.RemoteViews;
import android.widget.RemoteViewsService;

import java.text.DateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;

public final class WidgetCardsService extends RemoteViewsService {
    @Override public RemoteViewsFactory onGetViewFactory(Intent intent) {
        return new CardsFactory(getApplicationContext());
    }

    private static final class CardsFactory implements RemoteViewsFactory {
        private final Context context;
        private List<AccountStore.Account> accounts = new ArrayList<>();
        private boolean unavailable;

        CardsFactory(Context context) { this.context = context; }

        @Override public void onCreate() { }

        @Override public void onDataSetChanged() {
            try { accounts = new AccountStore(context).load(); unavailable = false; }
            catch (Exception error) { accounts = new ArrayList<>(); unavailable = true; }
        }

        @Override public void onDestroy() { accounts.clear(); }
        @Override public int getCount() { return Math.max(1, accounts.size()); }
        @Override public int getViewTypeCount() { return 2; }
        @Override public long getItemId(int position) { return position; }
        @Override public boolean hasStableIds() { return false; }
        @Override public RemoteViews getLoadingView() { return null; }

        @Override public RemoteViews getViewAt(int position) {
            if (accounts.isEmpty()) {
                RemoteViews empty = new RemoteViews(context.getPackageName(), R.layout.widget_empty_card);
                empty.setTextViewText(R.id.widget_empty_text, unavailable ?
                    context.getString(R.string.localized_055) : context.getString(R.string.localized_056));
                empty.setOnClickFillInIntent(R.id.widget_empty_text, new Intent());
                return empty;
            }
            if (position < 0 || position >= accounts.size()) return null;
            AccountStore.Account account = accounts.get(position);
            RemoteViews card = new RemoteViews(context.getPackageName(), R.layout.widget_card);
            card.setTextViewText(R.id.widget_card_email,
                (account.provider.equals("claude") ? "Claude · " : "GPT/Codex · ") +
                (account.email.isEmpty() ? context.getString(R.string.localized_009) : account.email));
            card.setTextViewText(R.id.widget_card_plan, account.plan.toUpperCase(Locale.ROOT));
            Usage usage = account.usage;
            Usage.Window five = usage == null ? null : usage.fiveHour;
            Usage.Window week = usage == null ? null : usage.weekly;
            card.setTextViewText(R.id.widget_card_five, five == null ? "—" : five.remaining + "%");
            card.setTextViewText(R.id.widget_card_week, week == null ? "—" : week.remaining + "%");
            card.setProgressBar(R.id.widget_card_five_bar, 100, five == null ? 0 : five.remaining, false);
            card.setProgressBar(R.id.widget_card_week_bar, 100, week == null ? 0 : week.remaining, false);
            card.setTextViewText(R.id.widget_card_five_reset, resetText(five));
            card.setTextViewText(R.id.widget_card_week_reset, resetText(week));
            String status = account.updatedAt == 0 ? context.getString(R.string.localized_057) : context.getString(R.string.localized_015) +
                DateFormat.getDateTimeInstance(DateFormat.SHORT, DateFormat.SHORT)
                    .format(new Date(account.updatedAt));
            if (account.error != null) status += " · " + AccountStore.errorText(context, account.error);
            card.setTextViewText(R.id.widget_card_status, status);
            if (account.error != null) card.setTextColor(R.id.widget_card_status, 0xFFFFBE8A);
            card.setContentDescription(R.id.widget_card_root, WidgetRenderer.description(context, account) + ". " + status);
            card.setOnClickFillInIntent(R.id.widget_card_root, new Intent());
            return card;
        }

        private String resetText(Usage.Window window) {
            return window == null ? context.getString(R.string.localized_058) : window.resetsAt <= 0 ?
                context.getString(R.string.localized_059) : context.getString(R.string.localized_019) +
                DateFormat.getDateTimeInstance(DateFormat.SHORT, DateFormat.SHORT)
                    .format(new Date(window.resetsAt * 1000));
        }
    }
}
