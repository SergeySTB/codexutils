package com.aiusagemonitor.android;

import org.json.JSONObject;

import java.io.IOException;
import java.net.UnknownHostException;
import java.nio.charset.StandardCharsets;
import java.util.Base64;

public final class UsageCheck {
    public static void main(String[] args) throws Exception {
        Usage.demoCheck();
        if (WidgetIconSlots.diameter(264, 102) != 96 || WidgetIconSlots.diameter(56, 48) != 42 ||
            WidgetIconSlots.capacity(264, 96) != 2 || WidgetIconSlots.capacity(120, 42) != 2 ||
            WidgetIconSlots.capacity(56, 42) != 1 || WidgetIconSlots.visible(1, 3) != 1 ||
            WidgetIconSlots.visible(2, 3) != 1 || WidgetIconSlots.visible(3, 3) != 3 ||
            WidgetIconSlots.visible(3, 4) != 2)
            throw new AssertionError("Widget icons must fit the available launcher width");
        if (WidgetSettings.normalized(15) != 15 || WidgetSettings.normalized(30) != 30 ||
            WidgetSettings.normalized(60) != 60 || WidgetSettings.normalized(1) != 30)
            throw new AssertionError("Widget interval must use a supported Android period");
        Usage original = Usage.parse(new JSONObject("{\"rate_limit\":{\"primary_window\":{\"used_percent\":20,\"limit_window_seconds\":18000}}}"));
        Usage saved = Usage.fromSaved(original.toJson());
        if (saved == null || saved.fiveHour == null || saved.fiveHour.remaining != 80 || saved.weekly != null)
            throw new AssertionError("Saved usage must preserve available windows");
        AccountStore.Account account = new AccountStore.Account(new JSONObject()
            .put("accountId", "account-1").put("email", "user@example.com")
            .put("usage", original.toJson()).put("updatedAt", 1234));
        AccountStore.Account restored = new AccountStore.Account(account.toJson());
        if (restored.usage == null || restored.usage.fiveHour == null ||
            restored.usage.fiveHour.remaining != 80 || restored.updatedAt != 1234 || restored.error != null)
            throw new AssertionError("Account snapshot must survive encryption payload roundtrip");
        if (Usage.fromSaved(new JSONObject("{\"fiveHour\":{\"remaining\":101}}")).fiveHour != null)
            throw new AssertionError("Corrupt saved limits must not be displayed");
        Usage invalid = Usage.parse(new JSONObject("{\"rate_limit\":{\"primary_window\":{\"used_percent\":-1,\"limit_window_seconds\":18000}}}"));
        if (invalid.fiveHour != null || invalid.weekly != null)
            throw new AssertionError("Invalid limits must stay unavailable");
        String id = token("{\"email\":\"user@example.com\"}");
        String access = token("{\"exp\":2000000000,\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"account-1\",\"chatgpt_plan_type\":\"plus\"}}");
        TokenClaims claims = TokenClaims.read(id, access);
        if (!claims.accountId.equals("account-1") || !claims.email.equals("user@example.com") ||
            !claims.plan.equals("plus") || claims.expiresAt != 2000000000)
            throw new AssertionError("Account identity must fall back to access token");
        try {
            TokenClaims.read(token("{\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"other\"}}"), access);
            throw new AssertionError("Mismatched account ids must be rejected");
        } catch (IllegalArgumentException expected) { }
        long now = System.currentTimeMillis();
        if (!CodexApi.retryPolling(new UnknownHostException(), now + 60_000) ||
            CodexApi.retryPolling(new IOException(), now + 60_000) ||
            CodexApi.retryPolling(new UnknownHostException(), now - 1))
            throw new AssertionError("Only temporary DNS failures during polling may be retried");
        System.out.println("Android usage checks passed");
    }

    private static String token(String claims) {
        return "e30." + Base64.getUrlEncoder().withoutPadding().encodeToString(claims.getBytes(StandardCharsets.UTF_8)) + ".sig";
    }
}
