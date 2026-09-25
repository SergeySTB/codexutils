package com.aiusagemonitor.android;

import android.content.Context;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import org.json.JSONArray;
import org.json.JSONObject;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.ArrayList;
import java.util.List;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

final class AccountStore {
    static final class Account {
        String email, plan, accountId, accessToken, refreshToken, idToken, provider;
        long expiresAt;
        Usage usage;
        long updatedAt;
        long retryAt;
        String error;

        Account(JSONObject json) {
            email = json.optString("email");
            plan = json.optString("plan");
            accountId = json.optString("accountId");
            provider = json.optString("provider", "codex");
            accessToken = json.optString("accessToken");
            refreshToken = json.optString("refreshToken");
            idToken = json.optString("idToken");
            expiresAt = json.optLong("expiresAt");
            usage = Usage.fromSaved(json.optJSONObject("usage"));
            updatedAt = json.optLong("updatedAt");
            retryAt = json.optLong("retryAt");
            error = json.optString("error", null);
        }

        JSONObject toJson() throws Exception {
            return new JSONObject().put("email", email).put("plan", plan).put("accountId", accountId).put("provider", provider)
                .put("accessToken", accessToken).put("refreshToken", refreshToken)
                .put("idToken", idToken).put("expiresAt", expiresAt)
                .put("usage", usage == null ? null : usage.toJson())
                .put("updatedAt", updatedAt).put("retryAt", retryAt).put("error", error);
        }

        void copyFrom(Account other) {
            email = other.email;
            provider = other.provider;
            plan = other.plan;
            accessToken = other.accessToken;
            refreshToken = other.refreshToken;
            idToken = other.idToken;
            expiresAt = other.expiresAt;
            usage = other.usage;
            updatedAt = other.updatedAt;
            retryAt = other.retryAt;
            error = other.error;
        }
    }

    private static final String KEY_ALIAS = "AIUsageMonitorAccounts";
    private static final Object LOCK = new Object();
    private static final Object REFRESH_LOCK = new Object();
    private final Context context;

    AccountStore(Context context) { this.context = context.getApplicationContext(); }

    static String errorText(Context context, String error) {
        if (error == null) return "";
        switch (error) {
            case "rate_limited": case "Слишком частые запросы. Повтор через 15 минут.":
                return context.getString(R.string.localized_087);
            case "claude_sign_in": case "Нужен новый токен Claude.":
                return context.getString(R.string.localized_088);
            case "codex_sign_in": case "Нужен повторный вход в ChatGPT.":
                return context.getString(R.string.localized_089);
            case "save_failed": case "Не удалось сохранить лимиты на телефоне.":
                return context.getString(R.string.localized_047);
            default: return context.getString(R.string.localized_090);
        }
    }

    private SecretKey key() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        if (store.containsAlias(KEY_ALIAS)) return ((KeyStore.SecretKeyEntry) store.getEntry(KEY_ALIAS, null)).getSecretKey();
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(KEY_ALIAS,
            KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build());
        return generator.generateKey();
    }

    List<Account> load() throws Exception {
        synchronized (LOCK) { return loadUnlocked(); }
    }

    private List<Account> loadUnlocked() throws Exception {
        String encoded = context.getSharedPreferences("accounts", Context.MODE_PRIVATE).getString("data", null);
        if (encoded == null) return new ArrayList<>();
        byte[] all = Base64.decode(encoded, Base64.NO_WRAP);
        if (all.length < 29) throw new IllegalStateException("Invalid account data");
        byte[] iv = new byte[12];
        System.arraycopy(all, 0, iv, 0, iv.length);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, iv));
        String plaintext = new String(cipher.doFinal(all, iv.length, all.length - iv.length), StandardCharsets.UTF_8);
        JSONArray json = new JSONArray(plaintext);
        List<Account> accounts = new ArrayList<>();
        for (int i = 0; i < json.length(); i++) accounts.add(new Account(json.getJSONObject(i)));
        return accounts;
    }

    private void saveUnlocked(List<Account> accounts) throws Exception {
        JSONArray json = new JSONArray();
        for (Account account : accounts) json.put(account.toJson());
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key());
        byte[] iv = cipher.getIV();
        byte[] encrypted = cipher.doFinal(json.toString().getBytes(StandardCharsets.UTF_8));
        byte[] all = new byte[iv.length + encrypted.length];
        System.arraycopy(iv, 0, all, 0, iv.length);
        System.arraycopy(encrypted, 0, all, iv.length, encrypted.length);
        if (!context.getSharedPreferences("accounts", Context.MODE_PRIVATE).edit()
            .putString("data", Base64.encodeToString(all, Base64.NO_WRAP)).commit())
            throw new IllegalStateException("Could not save accounts");
    }

    List<Account> upsert(Account account) throws Exception {
        synchronized (REFRESH_LOCK) { synchronized (LOCK) {
            List<Account> current = loadUnlocked();
            for (int i = 0; i < current.size(); i++) {
                if (current.get(i).accountId.equals(account.accountId)) {
                    account.usage = current.get(i).usage;
                    account.updatedAt = current.get(i).updatedAt;
                    current.set(i, account);
                    saveUnlocked(current);
                    return current;
                }
            }
            current.add(account);
            saveUnlocked(current);
            return current;
        } }
    }

    List<Account> remove(String accountId) throws Exception {
        synchronized (REFRESH_LOCK) { synchronized (LOCK) {
            List<Account> current = loadUnlocked();
            current.removeIf(account -> account.accountId.equals(accountId));
            saveUnlocked(current);
            return current;
        } }
    }

    void refresh(Account target) throws Exception {
        synchronized (REFRESH_LOCK) {
            Account current = null;
            for (Account account : load())
                if (account.accountId.equals(target.accountId)) { current = account; break; }
            if (current == null) return;
            if (System.currentTimeMillis() >= current.retryAt) {
                Account saved = current;
                try {
                    current.usage = current.provider.equals("claude") ? ClaudeApi.readUsage(current.accessToken)
                        : CodexApi.readUsage(current, () -> saveRefreshed(saved));
                    current.updatedAt = System.currentTimeMillis();
                    current.error = null;
                    current.retryAt = current.provider.equals("claude") ? System.currentTimeMillis() + 5 * 60_000L : 0;
                } catch (Exception error) {
                    boolean limited = error instanceof CodexApi.HttpStatusException &&
                        ((CodexApi.HttpStatusException) error).status == 429;
                    current.retryAt = limited ? System.currentTimeMillis() + 15 * 60_000L :
                        current.provider.equals("claude") ? System.currentTimeMillis() + 5 * 60_000L : 0;
                    boolean signIn = error instanceof CodexApi.HttpStatusException &&
                        (((CodexApi.HttpStatusException) error).status == 401 ||
                         ((CodexApi.HttpStatusException) error).status == 403);
                    current.error = limited ? "rate_limited" :
                        signIn ? (current.provider.equals("claude") ? "claude_sign_in" : "codex_sign_in") :
                        "limits_unavailable";
                }
                saveRefreshed(current);
            }
            target.copyFrom(current);
        }
    }

    void saveRefreshed(Account refreshed) throws Exception {
        synchronized (LOCK) {
            List<Account> current = loadUnlocked();
            for (Account account : current) {
                if (!account.accountId.equals(refreshed.accountId)) continue;
                account.email = refreshed.email;
                account.plan = refreshed.plan;
                account.accessToken = refreshed.accessToken;
                account.refreshToken = refreshed.refreshToken;
                account.idToken = refreshed.idToken;
                account.expiresAt = refreshed.expiresAt;
                account.usage = refreshed.usage;
                account.updatedAt = refreshed.updatedAt;
                account.retryAt = refreshed.retryAt;
                account.error = refreshed.error;
                saveUnlocked(current);
                return;
            }
        }
    }
}
