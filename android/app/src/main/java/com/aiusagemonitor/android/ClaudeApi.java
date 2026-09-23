package com.aiusagemonitor.android;

import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.URL;
import java.nio.charset.StandardCharsets;

import javax.net.ssl.HttpsURLConnection;

final class ClaudeApi {
    static boolean validToken(String token) {
        return token != null && token.length() <= 4096 && token.matches("sk-ant-oat[0-9A-Za-z_-]+");
    }

    static Usage readUsage(String token) throws Exception {
        if (!validToken(token))
            throw new IllegalArgumentException("Invalid Claude OAuth token");
        HttpsURLConnection connection = (HttpsURLConnection) new URL("https://api.anthropic.com/api/oauth/usage").openConnection();
        try {
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(10_000);
            connection.setReadTimeout(20_000);
            connection.setRequestProperty("Accept", "application/json");
            connection.setRequestProperty("Authorization", "Bearer " + token);
            connection.setRequestProperty("anthropic-beta", "oauth-2025-04-20");
            connection.setRequestProperty("User-Agent", "AIUsageMonitorAndroid/6.0");
            int status = connection.getResponseCode();
            if (status < 200 || status >= 300) throw new CodexApi.HttpStatusException(status);
            try (InputStream input = connection.getInputStream(); ByteArrayOutputStream output = new ByteArrayOutputStream()) {
                byte[] bytes = new byte[4096];
                int count;
                while ((count = input.read(bytes)) != -1) {
                    if (output.size() + count > 1_048_576) throw new IOException("Claude response too large");
                    output.write(bytes, 0, count);
                }
                return Usage.parseClaude(new JSONObject(output.toString(StandardCharsets.UTF_8.name())));
            }
        } finally { connection.disconnect(); }
    }
}
