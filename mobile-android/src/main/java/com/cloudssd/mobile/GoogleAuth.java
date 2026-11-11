package com.cloudssd.mobile;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.os.Bundle;
import android.os.SystemClock;
import android.util.Base64;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.util.HashMap;
import java.util.Map;

/**
 * Google sign-in on the phone itself, so the cloud works while the PC is off. Uses Google's page in the browser
 * (never a password typed into this app) with PKCE, and gets the full "drive" permission so the files the PC already
 * put on Drive are visible. Two kinds of Google key are supported, chosen when the app is built:
 * an iOS client (redirect into the app by its reversed client id) or a Desktop client (redirect to this phone's own
 * 127.0.0.1, then back into the app). The refresh key is kept encrypted ({@link TokenStore}); access keys only in memory.
 */
final class GoogleAuth {
    static final String SCOPE = "https://www.googleapis.com/auth/drive";
    private static final String AUTH_URL = "https://accounts.google.com/o/oauth2/v2/auth";
    private static final String TOKEN_URL = "https://oauth2.googleapis.com/token";
    private static final String REVOKE_URL = "https://oauth2.googleapis.com/revoke";
    private static final String PREFS = "cloudssd_google";

    private static final SecureRandom random = new SecureRandom();
    private static final Map<String, String[]> access = new HashMap<>();   // email → {token, expiresAtElapsedMs}
    private static volatile ServerSocket loopback;

    private GoogleAuth() {
    }

    static boolean available() {
        return !GoogleConfig.CLIENT_ID.isEmpty();
    }

    private static boolean desktop() {
        return "desktop".equals(GoogleConfig.MODE);
    }

    private static SharedPreferences prefs(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    // ---- accounts -----------------------------------------------------------------------------------------------

    /** [{email, folder, needsReauth, totalBytes, freeBytes}] of the Google accounts signed in on this phone. */
    static JSONArray accounts(Context c) {
        try {
            return new JSONArray(prefs(c).getString("accounts", "[]"));
        } catch (JSONException e) {
            return new JSONArray();
        }
    }

    private static synchronized void saveAccount(Context c, JSONObject acc) {
        JSONArray all = accounts(c), out = new JSONArray();
        for (int i = 0; i < all.length(); i++) {
            JSONObject o = all.optJSONObject(i);
            if (o != null && !o.optString("email").equalsIgnoreCase(acc.optString("email"))) out.put(o);
        }
        out.put(acc);
        prefs(c).edit().putString("accounts", out.toString()).commit();
    }

    static synchronized void updateAccount(Context c, String email, String key, Object value) {
        JSONArray all = accounts(c);
        for (int i = 0; i < all.length(); i++) {
            JSONObject o = all.optJSONObject(i);
            if (o != null && o.optString("email").equalsIgnoreCase(email)) {
                try {
                    o.put(key, value);
                } catch (JSONException ignored) {
                }
            }
        }
        prefs(c).edit().putString("accounts", all.toString()).commit();
    }

    /** The folder name the PC uses for an account on the "Each Account" drive (same rule as the PC app). */
    static String folderName(String email) {
        StringBuilder sb = new StringBuilder();
        for (char ch : email.toCharArray())
            sb.append(Character.isWhitespace(ch) || "=:/\\\"*?<>|".indexOf(ch) >= 0 ? '_' : ch);
        return sb.toString();
    }

    static void signOut(final Context c, final String email) {
        final String rt = TokenStore.get(prefs(c), "rt_" + email);
        synchronized (GoogleAuth.class) {
            JSONArray all = accounts(c), out = new JSONArray();
            for (int i = 0; i < all.length(); i++) {
                JSONObject o = all.optJSONObject(i);
                if (o != null && !o.optString("email").equalsIgnoreCase(email)) out.put(o);
            }
            prefs(c).edit().putString("accounts", out.toString()).commit();
            TokenStore.put(prefs(c), "rt_" + email, "");
            access.remove(email);
        }
        if (!rt.isEmpty()) new Thread(() -> {
            try {
                post(REVOKE_URL, "token=" + enc(rt));
            } catch (IOException ignored) {
            }
        }).start();
    }

    // ---- signing in -----------------------------------------------------------------------------------------------

    private static String b64url(byte[] b) {
        return Base64.encodeToString(b, Base64.URL_SAFE | Base64.NO_PADDING | Base64.NO_WRAP);
    }

    static String enc(String s) {
        try {
            return java.net.URLEncoder.encode(s, "UTF-8");
        } catch (Exception e) {
            return s;
        }
    }

    private static String redirectUri(int port) {
        return desktop() ? "http://127.0.0.1:" + port : GoogleConfig.REDIRECT;
    }

    /**
     * Opens Google's sign-in page. The answer comes back as a link into the app ({@link #handleRedirect}); on a
     * Desktop key it first reaches this phone's own little web server, which hands it over.
     */
    static void begin(final Activity a, final Callback done) throws IOException {
        if (!available()) throw new IOException("This build of the app has no Google key. See SETUP.md (\"Phone without the PC\").");
        byte[] v = new byte[48];
        random.nextBytes(v);
        String verifier = b64url(v);
        byte[] st = new byte[16];
        random.nextBytes(st);
        String state = b64url(st);
        String challenge;
        try {
            challenge = b64url(MessageDigest.getInstance("SHA-256").digest(verifier.getBytes(StandardCharsets.US_ASCII)));
        } catch (Exception e) {
            throw new IOException(e);
        }
        int port = 0;
        if (desktop()) {
            stopLoopback();
            final ServerSocket server = new ServerSocket(0, 1, InetAddress.getByName("127.0.0.1"));
            loopback = server;
            port = server.getLocalPort();
            new Thread(() -> serveLoopback(a.getApplicationContext(), server, done), "cloudssd-google").start();
        }
        // Kept until the answer arrives, even if Android stops the app while the browser is open.
        TokenStore.put(prefs(a), "pkce", verifier);
        prefs(a).edit().putString("state", state).putInt("port", port).commit();

        String url = AUTH_URL + "?response_type=code&client_id=" + enc(GoogleConfig.CLIENT_ID) + "&redirect_uri=" + enc(redirectUri(port))
                + "&scope=" + enc(SCOPE + " email") + "&code_challenge=" + challenge + "&code_challenge_method=S256&state=" + state
                + "&access_type=offline&prompt=" + enc("select_account consent");
        Intent i = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
        // A Custom Tab when Chrome is there (Google refuses sign-in inside an app's own web view), else the browser.
        Bundle extras = new Bundle();
        extras.putBinder("android.support.customtabs.extra.SESSION", null);
        i.putExtras(extras);
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        a.startActivity(i);
    }

    interface Callback {
        void done(JSONObject result);
    }

    private static void stopLoopback() {
        ServerSocket s = loopback;
        loopback = null;
        if (s != null) try {
            s.close();
        } catch (IOException ignored) {
        }
    }

    /** Desktop key: Google sends the browser to http://127.0.0.1:port/?code=…; answer with a page that returns to the app. */
    private static void serveLoopback(Context c, ServerSocket server, Callback done) {
        try {
            server.setSoTimeout(5 * 60 * 1000);
            try (Socket s = server.accept()) {
                s.setSoTimeout(10000);
                InputStream in = s.getInputStream();
                ByteArrayOutputStream head = new ByteArrayOutputStream();
                int b, last4 = 0;
                while ((b = in.read()) >= 0 && head.size() < 16384) {
                    head.write(b);
                    last4 = (last4 << 8) | b;
                    if (last4 == 0x0D0A0D0A) break;
                }
                String first = head.toString("UTF-8").split("\r\n")[0];
                String target = first.split(" ").length > 1 ? first.split(" ")[1] : "/";
                Uri u = Uri.parse("http://127.0.0.1" + target);
                String page = "<!doctype html><meta name=viewport content='width=device-width'><body style='font-family:sans-serif;padding:32px;text-align:center'>"
                        + "<h2>Cloud-SSD</h2><p>Signed in. Going back to the app…</p>"
                        + "<p><a href='intent://auth#Intent;scheme=cloudssd;package=com.cloudssd.mobile;end' style='font-size:18px'>Back to Cloud-SSD</a></p>"
                        + "<script>location.href='intent://auth#Intent;scheme=cloudssd;package=com.cloudssd.mobile;end'</script>";
                byte[] body = page.getBytes(StandardCharsets.UTF_8);
                OutputStream out = s.getOutputStream();
                out.write(("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.length + "\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.US_ASCII));
                out.write(body);
                out.flush();
                done.done(finish(c, u.getQueryParameter("code"), u.getQueryParameter("state"), u.getQueryParameter("error")));
            }
        } catch (IOException e) {
            if (loopback == server) done.done(error("Sign-in was not finished (" + e.getMessage() + ")."));
        } finally {
            if (loopback == server) stopLoopback();
        }
    }

    /** iOS key: Google sends the browser straight to the app (com.googleusercontent.apps…:/oauth2redirect?code=…). */
    static JSONObject handleRedirect(Context c, Uri data) {
        return finish(c, data.getQueryParameter("code"), data.getQueryParameter("state"), data.getQueryParameter("error"));
    }

    static boolean isRedirect(Uri data) {
        return data != null && !GoogleConfig.REDIRECT.isEmpty() && GoogleConfig.REDIRECT.startsWith(data.getScheme() + ":");
    }

    private static JSONObject error(String msg) {
        try {
            return new JSONObject().put("ok", false).put("error", msg);
        } catch (JSONException e) {
            return new JSONObject();
        }
    }

    private static JSONObject finish(Context c, String code, String state, String err) {
        SharedPreferences p = prefs(c);
        if (err != null) return error("Google: " + ("access_denied".equals(err) ? "you cancelled the sign-in." : err));
        if (code == null || state == null || !state.equals(p.getString("state", ""))) return error("This sign-in answer does not belong to this app. Try again.");
        String verifier = TokenStore.get(p, "pkce");
        int port = p.getInt("port", 0);
        p.edit().remove("state").commit();
        TokenStore.put(p, "pkce", "");
        try {
            String body = "grant_type=authorization_code&code=" + enc(code) + "&code_verifier=" + enc(verifier)
                    + "&client_id=" + enc(GoogleConfig.CLIENT_ID) + "&redirect_uri=" + enc(redirectUri(port))
                    + (GoogleConfig.CLIENT_SECRET.isEmpty() ? "" : "&client_secret=" + enc(GoogleConfig.CLIENT_SECRET));
            JSONObject t = new JSONObject(post(TOKEN_URL, body));
            String at = t.optString("access_token", "");
            String rt = t.optString("refresh_token", "");
            if (at.isEmpty()) return error("Google did not give access: " + t.optString("error_description", t.optString("error", "unknown")));
            JSONObject about = DriveApi.about(at);
            String email = about.optJSONObject("user") != null ? about.optJSONObject("user").optString("emailAddress", "") : "";
            if (email.isEmpty()) return error("Google did not say which account this is.");
            if (rt.isEmpty()) rt = TokenStore.get(p, "rt_" + email); // a second sign-in may not repeat it
            if (rt.isEmpty()) return error("Google gave no lasting access. Remove Cloud-SSD in your Google account's \"Third-party access\" and sign in again.");
            TokenStore.put(p, "rt_" + email, rt);
            synchronized (access) {
                access.put(email, new String[]{at, String.valueOf(SystemClock.elapsedRealtime() + t.optLong("expires_in", 3600) * 1000L - 60_000)});
            }
            JSONObject acc = new JSONObject().put("email", email).put("folder", folderName(email)).put("needsReauth", false);
            JSONObject q = about.optJSONObject("storageQuota");
            if (q != null) {
                long limit = q.optLong("limit", -1), usage = q.optLong("usage", 0);
                acc.put("totalBytes", limit).put("freeBytes", limit > 0 ? Math.max(0, limit - usage) : -1);
            }
            saveAccount(c, acc);
            return new JSONObject().put("ok", true).put("email", email);
        } catch (IOException | JSONException e) {
            return error("Sign-in failed: " + e.getMessage());
        }
    }

    /** A valid access key for the account (renewed with the stored refresh key when old). */
    static String accessToken(Context c, String email) throws IOException {
        synchronized (access) {
            String[] a = access.get(email);
            if (a != null && SystemClock.elapsedRealtime() < Long.parseLong(a[1])) return a[0];
        }
        String rt = TokenStore.get(prefs(c), "rt_" + email);
        if (rt.isEmpty()) throw new CloudBackend.BackendException(401, email + " needs to sign in again on this phone.");
        String body = "grant_type=refresh_token&refresh_token=" + enc(rt) + "&client_id=" + enc(GoogleConfig.CLIENT_ID)
                + (GoogleConfig.CLIENT_SECRET.isEmpty() ? "" : "&client_secret=" + enc(GoogleConfig.CLIENT_SECRET));
        String text;
        try {
            text = post(TOKEN_URL, body);
        } catch (HttpError e) {
            if (e.body.contains("invalid_grant")) {
                updateAccount(c, email, "needsReauth", true);
                throw new CloudBackend.BackendException(401, email + " needs to sign in again on this phone (Google ended the access).");
            }
            throw e;
        }
        try {
            JSONObject t = new JSONObject(text);
            String at = t.getString("access_token");
            synchronized (access) {
                access.put(email, new String[]{at, String.valueOf(SystemClock.elapsedRealtime() + t.optLong("expires_in", 3600) * 1000L - 60_000)});
            }
            return at;
        } catch (JSONException e) {
            throw new IOException("Unexpected answer from Google.");
        }
    }

    static final class HttpError extends IOException {
        final int code;
        final String body;

        HttpError(int code, String body) {
            super("Google answered HTTP " + code);
            this.code = code;
            this.body = body;
        }
    }

    private static String post(String url, String form) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        try {
            c.setConnectTimeout(15000);
            c.setReadTimeout(30000);
            c.setRequestMethod("POST");
            c.setDoOutput(true);
            c.setRequestProperty("Content-Type", "application/x-www-form-urlencoded");
            byte[] b = form.getBytes(StandardCharsets.UTF_8);
            c.setFixedLengthStreamingMode(b.length);
            try (OutputStream o = c.getOutputStream()) {
                o.write(b);
            }
            int code = c.getResponseCode();
            String text = DriveApi.readAll(code >= 400 ? c.getErrorStream() : c.getInputStream());
            if (code >= 400) throw new HttpError(code, text);
            return text;
        } finally {
            c.disconnect();
        }
    }
}
