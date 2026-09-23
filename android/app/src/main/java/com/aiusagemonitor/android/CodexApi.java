package com.aiusagemonitor.android;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.UnknownHostException;
import java.net.URL;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;

import javax.net.ssl.HttpsURLConnection;

final class CodexApi {
    // Public client identifier and routes used by the Apache-2.0 Codex CLI source.
    // These service routes are not a published third-party API and may change.
    private static final String CLIENT_ID = "app_EMoamEEZ73f0CkXaXp7hrann";
    private static final String AUTH = "https://auth.openai.com";
    private static final String USAGE = "https://chatgpt.com/backend-api/wham/usage";

    interface TokenSaver { void save() throws Exception; }

    static final class HttpStatusException extends IOException {
        final int status;
        HttpStatusException(int status) {
            super("Service returned HTTP " + status);
            this.status = status;
        }
    }

    static final class DeviceCode {
        final String id, code;
        final int interval;
        DeviceCode(JSONObject json) {
            id = json.optString("device_auth_id");
            code = json.optString("user_code", json.optString("usercode"));
            int parsed;
            try { parsed = Integer.parseInt(json.optString("interval", "5")); }
            catch (NumberFormatException ignored) { parsed = 5; }
            interval = Math.max(5, Math.min(30, parsed));
            if (id.isEmpty() || code.isEmpty()) throw new IllegalArgumentException("Invalid device code response");
        }
    }

    private static final class Response {
        final int status;
        final JSONObject body;
        Response(int status, JSONObject body) { this.status = status; this.body = body; }
    }

    static DeviceCode beginLogin() throws Exception {
        Response response = request("POST", AUTH + "/api/accounts/deviceauth/usercode",
            new JSONObject().put("client_id", CLIENT_ID).toString(), "application/json", null);
        requireSuccess(response);
        return new DeviceCode(response.body);
    }

    static JSONObject awaitApproval(DeviceCode device) throws Exception {
        long deadline = System.currentTimeMillis() + 15 * 60_000L;
        while (System.currentTimeMillis() < deadline) {
            if (Thread.currentThread().isInterrupted()) throw new InterruptedException();
            Response response;
            try {
                response = request("POST", AUTH + "/api/accounts/deviceauth/token",
                    new JSONObject().put("device_auth_id", device.id).put("user_code", device.code).toString(),
                    "application/json", null);
            } catch (IOException error) {
                if (!retryPolling(error, deadline)) throw error;
                Thread.sleep(device.interval * 1000L);
                continue;
            }
            if (Thread.currentThread().isInterrupted()) throw new InterruptedException();
            if (response.status == 200) return response.body;
            if (response.status != 403 && response.status != 404)
                throw new HttpStatusException(response.status);
            Thread.sleep(device.interval * 1000L);
        }
        throw new IllegalStateException("Device login timed out");
    }

    static boolean retryPolling(IOException error, long deadline) {
        return error instanceof UnknownHostException && System.currentTimeMillis() < deadline;
    }

    static AccountStore.Account exchangeCode(JSONObject code) throws Exception {
        String form = form("grant_type", "authorization_code") + "&" + form("client_id", CLIENT_ID)
            + "&" + form("code", code.getString("authorization_code"))
            + "&" + form("redirect_uri", AUTH + "/deviceauth/callback")
            + "&" + form("code_verifier", code.getString("code_verifier"));
        Response tokens = request("POST", AUTH + "/oauth/token", form,
            "application/x-www-form-urlencoded", null);
        if (Thread.currentThread().isInterrupted()) throw new InterruptedException();
        requireSuccess(tokens);
        AccountStore.Account account = new AccountStore.Account(new JSONObject());
        account.idToken = tokens.body.getString("id_token");
        account.accessToken = tokens.body.getString("access_token");
        account.refreshToken = tokens.body.getString("refresh_token");
        updateIdentity(account);
        return account;
    }

    static Usage readUsage(AccountStore.Account account, TokenSaver saveTokens) throws Exception {
        if (account.expiresAt <= System.currentTimeMillis() / 1000 + 300) {
            refresh(account);
            saveTokens.save();
        }
        Response response = usageRequest(account);
        if (response.status == 401) {
            refresh(account);
            saveTokens.save();
            response = usageRequest(account);
        }
        requireSuccess(response);
        return Usage.parse(response.body);
    }

    private static Response usageRequest(AccountStore.Account account) throws Exception {
        Map<String, String> headers = new HashMap<>();
        headers.put("Authorization", "Bearer " + account.accessToken);
        headers.put("ChatGPT-Account-Id", account.accountId);
        return request("GET", USAGE, null, null, headers);
    }

    private static void refresh(AccountStore.Account account) throws Exception {
        Response response = request("POST", AUTH + "/oauth/token",
            new JSONObject().put("grant_type", "refresh_token")
                .put("client_id", CLIENT_ID).put("refresh_token", account.refreshToken).toString(),
            "application/json", null);
        requireSuccess(response);
        String newIdToken = response.body.optString("id_token", account.idToken);
        String newAccessToken = response.body.optString("access_token", account.accessToken);
        String newRefreshToken = response.body.optString("refresh_token", account.refreshToken);
        if (newAccessToken.isEmpty() || newRefreshToken.isEmpty()) throw new IllegalStateException("Incomplete token refresh");
        AccountStore.Account updated = new AccountStore.Account(new JSONObject());
        updated.idToken = newIdToken;
        updated.accessToken = newAccessToken;
        updated.refreshToken = newRefreshToken;
        updateIdentity(updated);
        if (!account.accountId.equals(updated.accountId)) throw new IllegalStateException("Account changed during refresh");
        account.idToken = updated.idToken;
        account.accessToken = updated.accessToken;
        account.refreshToken = updated.refreshToken;
        account.expiresAt = updated.expiresAt;
        account.email = updated.email;
        account.plan = updated.plan;
    }

    private static void updateIdentity(AccountStore.Account account) throws Exception {
        if (account.accessToken.isEmpty() || account.refreshToken.isEmpty())
            throw new IllegalStateException("Incomplete ChatGPT account data");
        TokenClaims claims = TokenClaims.read(account.idToken, account.accessToken);
        account.accountId = claims.accountId;
        account.email = claims.email;
        account.plan = claims.plan;
        account.expiresAt = claims.expiresAt;
    }

    private static String form(String name, String value) throws Exception {
        return URLEncoder.encode(name, "UTF-8") + "=" + URLEncoder.encode(value, "UTF-8");
    }

    private static void requireSuccess(Response response) throws HttpStatusException {
        if (response.status < 200 || response.status >= 300)
            throw new HttpStatusException(response.status);
    }

    private static Response request(String method, String address, String body, String contentType,
                                    Map<String, String> headers) throws Exception {
        URL url = new URL(address);
        if (!url.getProtocol().equals("https") ||
            !(url.getHost().equals("auth.openai.com") || url.getHost().equals("chatgpt.com")))
            throw new IllegalArgumentException("Unexpected service address");
        HttpsURLConnection connection = (HttpsURLConnection) url.openConnection();
        try {
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(10_000);
            connection.setReadTimeout(20_000);
            connection.setRequestMethod(method);
            connection.setRequestProperty("Accept", "application/json");
            connection.setRequestProperty("User-Agent", "AIUsageMonitorAndroid/6.0");
            if (headers != null) headers.forEach(connection::setRequestProperty);
            if (body != null) {
                connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", contentType);
                byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
                // Token exchanges may consume a one-time code; do not let the client replay them.
                if (url.getPath().equals("/oauth/token")) connection.setFixedLengthStreamingMode(bytes.length);
                try (OutputStream output = connection.getOutputStream()) { output.write(bytes); }
            }
            int status = connection.getResponseCode();
            if (status < 200 || status >= 300) return new Response(status, new JSONObject());
            InputStream stream = status >= 400 ? connection.getErrorStream() : connection.getInputStream();
            if (stream == null) return new Response(status, new JSONObject());
            try (InputStream input = stream; ByteArrayOutputStream output = new ByteArrayOutputStream()) {
                byte[] bytes = new byte[4096];
                int count;
                while ((count = input.read(bytes)) != -1) {
                    if (output.size() + count > 1_048_576) throw new IllegalStateException("Service response too large");
                    output.write(bytes, 0, count);
                }
                return new Response(status, output.size() == 0 ? new JSONObject()
                    : new JSONObject(output.toString("UTF-8")));
            }
        } finally { connection.disconnect(); }
    }
}
