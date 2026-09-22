package com.aiusagemonitor.android;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;

import org.json.JSONObject;

import java.text.DateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

public final class MainActivity extends Activity {
    private static final int BACKGROUND = Color.rgb(12, 19, 29);
    private static final int CARD = Color.rgb(27, 39, 54);
    private static final int TEXT = Color.rgb(238, 245, 250);
    private static final int MUTED = Color.rgb(170, 187, 200);
    private static final int FIVE_HOUR = Color.rgb(100, 226, 204);
    private static final int WEEKLY = Color.rgb(177, 151, 250);

    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final List<AccountStore.Account> accounts = new ArrayList<>();
    private AccountStore store;
    private LinearLayout cards;
    private TextView status;
    private Button add;
    private boolean signingIn, refreshing, closed, storageUnavailable;
    private Future<?> loginTask;
    private final Runnable periodicRefresh = new Runnable() {
        @Override public void run() {
            if (closed) return;
            refreshAll();
            handler.postDelayed(this, 60_000);
        }
    };

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        if ((getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) != 0) {
            try { Usage.demoCheck(); }
            catch (Exception error) { throw new AssertionError("Codex usage check failed", error); }
        }
        store = new AccountStore(this);
        buildScreen();
        try { accounts.addAll(store.load()); }
        catch (Exception ignored) {
            storageUnavailable = true;
            add.setEnabled(false);
            status.setText("Не удалось открыть сохранённые аккаунты. Очистите данные приложения в настройках Android.");
        }
        render();
    }

    @Override protected void onStart() {
        super.onStart();
        WidgetRefreshJob.activityVisible = true;
        WidgetRefreshJob.schedulePeriodic(this);
        handler.removeCallbacks(periodicRefresh);
        handler.post(periodicRefresh);
    }

    @Override protected void onStop() {
        handler.removeCallbacks(periodicRefresh);
        WidgetRefreshJob.activityVisible = false;
        super.onStop();
    }

    @Override protected void onDestroy() {
        closed = true;
        worker.shutdownNow();
        super.onDestroy();
    }

    private void buildScreen() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(BACKGROUND);
        scroll.setOnApplyWindowInsetsListener((view, insets) -> {
            view.setPadding(0, insets.getSystemWindowInsetTop(), 0, insets.getSystemWindowInsetBottom());
            return insets;
        });
        LinearLayout content = new LinearLayout(this);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(dp(20), dp(24), dp(20), dp(24));
        scroll.addView(content);
        setContentView(scroll);

        TextView title = text("AI Usage Monitor", 25, TEXT);
        title.setTypeface(null, Typeface.BOLD);
        content.addView(title);
        content.addView(text("Остаток лимитов Codex", 14, MUTED));

        LinearLayout actions = new LinearLayout(this);
        actions.setPadding(0, dp(16), 0, dp(8));
        add = button("Добавить аккаунт", this::startLogin);
        actions.addView(add, new LinearLayout.LayoutParams(0, dp(48), 1));
        Button refresh = button("Обновить", this::refreshAll);
        LinearLayout.LayoutParams refreshSize = new LinearLayout.LayoutParams(dp(112), dp(48));
        refreshSize.leftMargin = dp(8);
        actions.addView(refresh, refreshSize);
        content.addView(actions);
        Button settings = button("Настройки", () -> startActivity(new Intent(this, SettingsActivity.class)));
        content.addView(settings, new LinearLayout.LayoutParams(-2, dp(48)));
        status = text("", 13, MUTED);
        content.addView(status);
        cards = new LinearLayout(this);
        cards.setOrientation(LinearLayout.VERTICAL);
        content.addView(cards);
    }

    private void render() {
        cards.removeAllViews();
        if (accounts.isEmpty()) {
            TextView hint = text(storageUnavailable ? "Сохранённые данные входа недоступны." :
                "Добавьте аккаунт и войдите через ChatGPT в браузере.", 16, MUTED);
            hint.setPadding(0, dp(48), 0, 0);
            cards.addView(hint);
            return;
        }
        for (AccountStore.Account account : accounts) {
            LinearLayout card = new LinearLayout(this);
            card.setOrientation(LinearLayout.VERTICAL);
            card.setPadding(dp(18), dp(16), dp(18), dp(16));
            GradientDrawable background = new GradientDrawable();
            background.setColor(CARD);
            background.setCornerRadius(dp(16));
            card.setBackground(background);
            LinearLayout.LayoutParams cardSize = new LinearLayout.LayoutParams(-1, -2);
            cardSize.topMargin = dp(12);
            cards.addView(card, cardSize);

            TextView email = text(account.email.isEmpty() ? "Аккаунт ChatGPT" : account.email, 18, TEXT);
            email.setTypeface(null, Typeface.BOLD);
            card.addView(email);
            if (!account.plan.isEmpty()) card.addView(text(account.plan.toUpperCase(), 12, MUTED));
            limit(card, "5 часов", account.usage == null ? null : account.usage.fiveHour, FIVE_HOUR);
            limit(card, "Неделя", account.usage == null ? null : account.usage.weekly, WEEKLY);
            if (account.updatedAt > 0)
                card.addView(text("Обновлено " + formatTime(account.updatedAt), 12, MUTED));
            if (account.error != null)
                card.addView(text((account.usage == null ? "" : "Данные устарели · ") + account.error,
                    13, Color.rgb(255, 190, 138)));
            Button remove = button("Убрать аккаунт", () -> removeAccount(account));
            remove.setContentDescription("Убрать аккаунт " + account.email);
            LinearLayout.LayoutParams removeSize = new LinearLayout.LayoutParams(-2, dp(44));
            removeSize.topMargin = dp(8);
            card.addView(remove, removeSize);
        }
    }

    private void limit(LinearLayout card, String name, Usage.Window window, int color) {
        LinearLayout row = new LinearLayout(this);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(0, dp(16), 0, dp(5));
        row.addView(text(name, 15, TEXT), new LinearLayout.LayoutParams(0, -2, 1));
        row.addView(text(window == null ? "—" : window.remaining + "%", 20, color));
        card.addView(row);
        ProgressBar bar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        bar.setMax(100);
        bar.setProgress(window == null ? 0 : window.remaining);
        bar.setProgressTintList(android.content.res.ColorStateList.valueOf(color));
        bar.setProgressBackgroundTintList(android.content.res.ColorStateList.valueOf(BACKGROUND));
        bar.setContentDescription(name + ": " + (window == null ? "нет данных" : window.remaining + "% осталось"));
        card.addView(bar, new LinearLayout.LayoutParams(-1, dp(7)));
        if (window != null && window.resetsAt > 0)
            card.addView(text("Сброс " + formatTime(window.resetsAt * 1000), 12, MUTED));
    }

    private void startLogin() {
        if (signingIn || storageUnavailable) return;
        signingIn = true;
        add.setEnabled(false);
        status.setText("Получаем код входа…");
        loginTask = worker.submit(() -> {
            String stage = "получение кода";
            try {
                CodexApi.DeviceCode device = CodexApi.beginLogin();
                runOnUiThread(() -> {
                    if (!closed) showDeviceCode(device.code);
                });
                stage = "ожидание подтверждения кода";
                JSONObject approved = CodexApi.awaitApproval(device);
                stage = "обмен кода на токены";
                AccountStore.Account account = CodexApi.exchangeCode(approved);
                if (Thread.currentThread().isInterrupted()) throw new InterruptedException();
                stage = "сохранение аккаунта";
                List<AccountStore.Account> copy = store.upsert(account);
                runOnUiThread(() -> {
                    if (!closed) {
                        accounts.clear();
                        accounts.addAll(copy);
                        signingIn = false;
                        add.setEnabled(true);
                        status.setText("Аккаунт подключён");
                        render();
                        WidgetRenderer.showCached(this);
                        refreshAll();
                    }
                });
            } catch (InterruptedException ignored) {
                Thread.currentThread().interrupt();
                runOnUiThread(() -> { if (!closed) status.setText("Вход отменён"); });
            } catch (Exception error) {
                String detail = error instanceof CodexApi.HttpStatusException
                    ? "HTTP " + ((CodexApi.HttpStatusException) error).status
                    : error instanceof IllegalArgumentException ? "данные аккаунта недоступны"
                    : error instanceof java.io.IOException ? error.getClass().getSimpleName()
                    : error instanceof IllegalStateException ? "истекло время ожидания" : "ошибка приложения";
                String message = "Вход не завершён: " + stage + " (" + detail + ")";
                runOnUiThread(() -> { if (!closed) status.setText(message); });
            } finally {
                runOnUiThread(() -> { if (!closed) { signingIn = false; add.setEnabled(true); } });
            }
        });
    }

    private void showDeviceCode(String code) {
        new AlertDialog.Builder(this).setTitle("Вход через ChatGPT")
            .setMessage("Откройте страницу входа и введите код: " + code + "\nКод действует 15 минут.")
            .setPositiveButton("Скопировать и открыть", (dialog, which) -> {
                copyCode(code);
                startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("https://auth.openai.com/codex/device")));
            })
            .setNeutralButton("Скопировать код", (dialog, which) -> {
                copyCode(code);
                status.setText("Код скопирован. Откройте auth.openai.com/codex/device в браузере.");
            })
            .setNegativeButton("Отмена", (dialog, which) -> {
                if (loginTask != null) loginTask.cancel(true);
            }).setOnCancelListener(dialog -> {
                if (loginTask != null) loginTask.cancel(true);
            }).show();
        status.setText("Ожидаем подтверждения входа…");
    }

    private void copyCode(String code) {
        ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        clipboard.setPrimaryClip(ClipData.newPlainText("Код входа Codex", code));
    }

    private void refreshAll() {
        if (refreshing || signingIn || accounts.isEmpty() || storageUnavailable) return;
        refreshing = true;
        status.setText("Обновляем лимиты…");
        worker.execute(() -> {
            for (AccountStore.Account account : accounts) {
                if (Thread.currentThread().isInterrupted()) break;
                try { store.refresh(account); }
                catch (Exception ignored) { account.error = "Не удалось сохранить лимиты на телефоне."; }
            }
            runOnUiThread(() -> {
                if (!closed) {
                    refreshing = false;
                    status.setText("Остаток лимитов · обновление каждую минуту, пока приложение открыто");
                    render();
                    WidgetRenderer.showCached(this);
                }
            });
        });
    }

    private void removeAccount(AccountStore.Account account) {
        new AlertDialog.Builder(this).setTitle("Убрать аккаунт?")
            .setMessage("Данные входа будут удалены с этого телефона.")
            .setNegativeButton("Отмена", null)
            .setPositiveButton("Убрать", (dialog, which) -> worker.execute(() -> {
                try {
                    List<AccountStore.Account> copy = store.remove(account.accountId);
                    runOnUiThread(() -> {
                        if (!closed) {
                            accounts.clear();
                            accounts.addAll(copy);
                            render();
                            WidgetRenderer.showCached(this);
                        }
                    });
                } catch (Exception ignored) {
                    runOnUiThread(() -> { if (!closed) status.setText("Не удалось убрать аккаунт"); });
                }
            })).show();
    }

    private TextView text(String value, int size, int color) {
        TextView text = new TextView(this);
        text.setText(value);
        text.setTextSize(size);
        text.setTextColor(color);
        return text;
    }

    private Button button(String label, Runnable action) {
        Button button = new Button(this);
        button.setText(label);
        button.setAllCaps(false);
        button.setOnClickListener(view -> action.run());
        return button;
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }

    private String formatTime(long millis) {
        return DateFormat.getDateTimeInstance(DateFormat.SHORT, DateFormat.SHORT).format(new Date(millis));
    }
}
