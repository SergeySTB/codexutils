package com.aiusagemonitor.android;

import org.json.JSONObject;

import java.nio.charset.StandardCharsets;
import java.util.Base64;

final class TokenClaims {
    final String accountId, email, plan;
    final long expiresAt;

    private TokenClaims(String accountId, String email, String plan, long expiresAt) {
        this.accountId = accountId;
        this.email = email;
        this.plan = plan;
        this.expiresAt = expiresAt;
    }

    static TokenClaims read(String idToken, String accessToken) {
        JSONObject id = payload(idToken);
        JSONObject access;
        try { access = payload(accessToken); }
        catch (IllegalArgumentException ignored) { access = new JSONObject(); }
        JSONObject idAuth = id.optJSONObject("https://api.openai.com/auth");
        JSONObject accessAuth = access.optJSONObject("https://api.openai.com/auth");
        String idAccount = idAuth == null ? "" : idAuth.optString("chatgpt_account_id");
        String accessAccount = accessAuth == null ? "" : accessAuth.optString("chatgpt_account_id");
        if (!idAccount.isEmpty() && !accessAccount.isEmpty() && !idAccount.equals(accessAccount))
            throw new IllegalArgumentException("Token accounts do not match");
        String accountId = idAccount.isEmpty() ? accessAccount : idAccount;
        if (accountId.isEmpty()) throw new IllegalArgumentException("ChatGPT account data unavailable");
        JSONObject profile = id.optJSONObject("https://api.openai.com/profile");
        String email = id.optString("email", profile == null ? "" : profile.optString("email"));
        String plan = idAuth == null ? "" : idAuth.optString("chatgpt_plan_type");
        if (plan.isEmpty() && accessAuth != null) plan = accessAuth.optString("chatgpt_plan_type");
        long expiresAt = access.optLong("exp", 0);
        if (expiresAt <= 0) expiresAt = System.currentTimeMillis() / 1000 + 3600;
        return new TokenClaims(accountId, email, plan, expiresAt);
    }

    private static JSONObject payload(String token) {
        String[] parts = token.split("\\.");
        if (parts.length != 3 || parts[1].isEmpty()) throw new IllegalArgumentException("Invalid token format");
        try {
            return new JSONObject(new String(Base64.getUrlDecoder().decode(parts[1]), StandardCharsets.UTF_8));
        } catch (Exception error) {
            throw new IllegalArgumentException("Invalid token claims", error);
        }
    }
}
