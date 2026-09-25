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
import android.view.Menu;
import android.view.SubMenu;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.PopupMenu;
import android.widget.ProgressBar;
import android.widget.EditText;
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
            status.setText(getString(R.string.localized_001));
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

        LinearLayout header = new LinearLayout(this);
        header.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout heading = new LinearLayout(this);
        heading.setOrientation(LinearLayout.VERTICAL);
        TextView title = text("AI Usage Monitor", 25, TEXT);
        title.setTypeface(null, Typeface.BOLD);
        heading.addView(title);
        heading.addView(text(getString(R.string.localized_002), 14, MUTED));
        header.addView(heading, new LinearLayout.LayoutParams(0, -2, 1));
        Button menu = new Button(this);
        menu.setText("⋮");
        menu.setAllCaps(false);
        menu.setTextSize(24);
        menu.setTextColor(TEXT);
        menu.setContentDescription(getString(R.string.localized_003));
        menu.setPadding(0, 0, 0, 0);
        menu.setBackgroundTintList(android.content.res.ColorStateList.valueOf(CARD));
        menu.setOnClickListener(this::showMenu);
        header.addView(menu, new LinearLayout.LayoutParams(dp(48), dp(48)));
        content.addView(header);
        status = text("", 13, MUTED);
        content.addView(status);
        cards = new LinearLayout(this);
        cards.setOrientation(LinearLayout.VERTICAL);
        content.addView(cards);
    }

    private void showMenu(View anchor) {
        PopupMenu popup = new PopupMenu(this, anchor);
        popup.setGravity(Gravity.END);
        Menu menu = popup.getMenu();
        menu.add(0, 1, 0, getString(R.string.localized_004)).setEnabled(!signingIn && !storageUnavailable);
        menu.add(0, 5, 1, getString(R.string.localized_005)).setEnabled(!signingIn && !storageUnavailable);
        menu.add(0, 2, 2, getString(R.string.localized_006)).setEnabled(!refreshing && !signingIn && !accounts.isEmpty() && !storageUnavailable);
        menu.add(0, 3, 3, getString(R.string.localized_007));
        List<AccountStore.Account> shown = new ArrayList<>(accounts);
        if (!shown.isEmpty()) {
            SubMenu remove = menu.addSubMenu(0, 4, 4, getString(R.string.localized_008));
            for (int i = 0; i < shown.size(); i++) {
                AccountStore.Account account = shown.get(i);
                remove.add(0, 100 + i, i, (account.provider.equals("claude") ? "Claude · " : "Codex · ") +
                    (account.email.isEmpty() ? getString(R.string.localized_009) : account.email));
            }
        }
        popup.setOnMenuItemClickListener(item -> {
            int id = item.getItemId();
            if (id == 1) startLogin();
            else if (id == 5) startClaudeLogin();
            else if (id == 2) refreshAll();
            else if (id == 3) startActivity(new Intent(this, SettingsActivity.class));
            else if (id >= 100 && id < 100 + shown.size()) removeAccount(shown.get(id - 100));
            else return false;
            return true;
        });
        popup.show();
    }

    private void render() {
        cards.removeAllViews();
        if (accounts.isEmpty()) {
            TextView hint = text(storageUnavailable ? getString(R.string.localized_010) :
                getString(R.string.localized_011), 16, MUTED);
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

            card.addView(text(account.provider.equals("claude") ? "CLAUDE" : "GPT / CODEX", 12, MUTED));
            TextView email = text(account.email.isEmpty() ? getString(R.string.localized_012) : account.email, 18, TEXT);
            email.setTypeface(null, Typeface.BOLD);
            card.addView(email);
            if (!account.plan.isEmpty()) card.addView(text(account.plan.toUpperCase(), 12, MUTED));
            limit(card, getString(R.string.localized_013), account.usage == null ? null : account.usage.fiveHour, FIVE_HOUR);
            limit(card, getString(R.string.localized_014), account.usage == null ? null : account.usage.weekly, WEEKLY);
            if (account.updatedAt > 0)
                card.addView(text(getString(R.string.localized_015) + formatTime(account.updatedAt), 12, MUTED));
            if (account.error != null)
                card.addView(text((account.usage == null ? "" : getString(R.string.localized_016)) + AccountStore.errorText(this, account.error),
                    13, Color.rgb(255, 190, 138)));
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
        bar.setContentDescription(name + ": " + (window == null ? getString(R.string.localized_017) : window.remaining + getString(R.string.localized_018)));
        card.addView(bar, new LinearLayout.LayoutParams(-1, dp(7)));
        if (window != null && window.resetsAt > 0)
            card.addView(text(getString(R.string.localized_019) + formatTime(window.resetsAt * 1000), 12, MUTED));
    }

    private void startLogin() {
        if (signingIn || storageUnavailable) return;
        signingIn = true;
        status.setText(getString(R.string.localized_020));
        loginTask = worker.submit(() -> {
            String stage = getString(R.string.localized_021);
            try {
                CodexApi.DeviceCode device = CodexApi.beginLogin();
                runOnUiThread(() -> {
                    if (!closed) showDeviceCode(device.code);
                });
                stage = getString(R.string.localized_022);
                JSONObject approved = CodexApi.awaitApproval(device);
                stage = getString(R.string.localized_023);
                AccountStore.Account account = CodexApi.exchangeCode(approved);
                if (Thread.currentThread().isInterrupted()) throw new InterruptedException();
                stage = getString(R.string.localized_024);
                List<AccountStore.Account> copy = store.upsert(account);
                runOnUiThread(() -> {
                    if (!closed) {
                        accounts.clear();
                        accounts.addAll(copy);
                        signingIn = false;
                        status.setText(getString(R.string.localized_025));
                        render();
                        WidgetRenderer.showCached(this);
                        refreshAll();
                    }
                });
            } catch (InterruptedException ignored) {
                Thread.currentThread().interrupt();
                runOnUiThread(() -> { if (!closed) status.setText(getString(R.string.localized_026)); });
            } catch (Exception error) {
                String detail = error instanceof CodexApi.HttpStatusException
                    ? "HTTP " + ((CodexApi.HttpStatusException) error).status
                    : error instanceof IllegalArgumentException ? getString(R.string.localized_027)
                    : error instanceof java.io.IOException ? error.getClass().getSimpleName()
                    : error instanceof IllegalStateException ? getString(R.string.localized_028) : getString(R.string.localized_029);
                String message = getString(R.string.localized_030) + stage + " (" + detail + ")";
                runOnUiThread(() -> { if (!closed) status.setText(message); });
            } finally {
                runOnUiThread(() -> { if (!closed) signingIn = false; });
            }
        });
    }

    private void startClaudeLogin() {
        LinearLayout fields = new LinearLayout(this);
        fields.setOrientation(LinearLayout.VERTICAL);
        fields.setPadding(dp(20), dp(8), dp(20), 0);
        EditText name = new EditText(this);
        name.setHint(getString(R.string.localized_031));
        name.setSingleLine(true);
        fields.addView(name);
        EditText token = new EditText(this);
        token.setHint(getString(R.string.localized_032));
        token.setInputType(android.text.InputType.TYPE_CLASS_TEXT | android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD);
        token.setSingleLine(true);
        fields.addView(token);
        new AlertDialog.Builder(this).setTitle(getString(R.string.localized_005))
            .setMessage(getString(R.string.localized_033))
            .setView(fields).setNegativeButton(getString(R.string.localized_034), null)
            .setPositiveButton(getString(R.string.localized_035), (dialog, which) -> {
                String label = name.getText().toString().trim();
                String secret = token.getText().toString().trim();
                if (label.isEmpty() || label.length() > 60 || !ClaudeApi.validToken(secret)) {
                    status.setText(getString(R.string.localized_036));
                    return;
                }
                worker.execute(() -> {
                    try {
                        AccountStore.Account account = new AccountStore.Account(new JSONObject());
                        account.provider = "claude";
                        account.accountId = java.util.UUID.randomUUID().toString();
                        account.email = label;
                        account.accessToken = secret;
                        List<AccountStore.Account> copy = store.upsert(account);
                        runOnUiThread(() -> {
                            if (closed) return;
                            accounts.clear();
                            accounts.addAll(copy);
                            render();
                            WidgetRenderer.showCached(this);
                            refreshAll();
                        });
                    } catch (Exception ignored) {
                        runOnUiThread(() -> { if (!closed) status.setText(getString(R.string.localized_037)); });
                    }
                });
            }).show();
    }

    private void showDeviceCode(String code) {
        new AlertDialog.Builder(this).setTitle(getString(R.string.localized_038))
            .setMessage(getString(R.string.localized_039) + code + getString(R.string.localized_040))
            .setPositiveButton(getString(R.string.localized_041), (dialog, which) -> {
                copyCode(code);
                startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("https://auth.openai.com/codex/device")));
            })
            .setNeutralButton(getString(R.string.localized_042), (dialog, which) -> {
                copyCode(code);
                status.setText(getString(R.string.localized_043));
            })
            .setNegativeButton(getString(R.string.localized_034), (dialog, which) -> {
                if (loginTask != null) loginTask.cancel(true);
            }).setOnCancelListener(dialog -> {
                if (loginTask != null) loginTask.cancel(true);
            }).show();
        status.setText(getString(R.string.localized_044));
    }

    private void copyCode(String code) {
        ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        clipboard.setPrimaryClip(ClipData.newPlainText(getString(R.string.localized_045), code));
    }

    private void refreshAll() {
        if (refreshing || signingIn || accounts.isEmpty() || storageUnavailable) return;
        refreshing = true;
        status.setText(getString(R.string.localized_046));
        worker.execute(() -> {
            for (AccountStore.Account account : accounts) {
                if (Thread.currentThread().isInterrupted()) break;
                try { store.refresh(account); }
                catch (Exception ignored) { account.error = "save_failed"; }
            }
            runOnUiThread(() -> {
                if (!closed) {
                    refreshing = false;
                    status.setText(getString(R.string.localized_048));
                    render();
                    WidgetRenderer.showCached(this);
                }
            });
        });
    }

    private void removeAccount(AccountStore.Account account) {
        new AlertDialog.Builder(this).setTitle(getString(R.string.localized_049))
            .setMessage(getString(R.string.localized_050) + (account.email.isEmpty() ? getString(R.string.localized_051) : account.email) +
                getString(R.string.localized_052))
            .setNegativeButton(getString(R.string.localized_034), null)
            .setPositiveButton(getString(R.string.localized_053), (dialog, which) -> worker.execute(() -> {
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
                    runOnUiThread(() -> { if (!closed) status.setText(getString(R.string.localized_054)); });
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
