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
        String email, plan, accountId, accessToken, refreshToken, idToken;
        long expiresAt;
        Usage usage;
        long updatedAt;
        long retryAt;
        String error;

        Account(JSONObject json) {
            email = json.optString("email");
            plan = json.optString("plan");
            accountId = json.optString("accountId");
            accessToken = json.optString("accessToken");
            refreshToken = json.optString("refreshToken");
            idToken = json.optString("idToken");
            expiresAt = json.optLong("expiresAt");
        }

        JSONObject toJson() throws Exception {
            return new JSONObject().put("email", email).put("plan", plan).put("accountId", accountId)
                .put("accessToken", accessToken).put("refreshToken", refreshToken)
                .put("idToken", idToken).put("expiresAt", expiresAt);
        }
    }

    private static final String KEY_ALIAS = "AIUsageMonitorAccounts";
    private final Context context;

    AccountStore(Context context) { this.context = context.getApplicationContext(); }

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

    void save(List<Account> accounts) throws Exception {
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
}
