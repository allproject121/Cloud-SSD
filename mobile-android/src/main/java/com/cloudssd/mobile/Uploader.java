package com.cloudssd.mobile;

import android.content.Context;
import android.os.SystemClock;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Sends one file to the PC so that a broken connection costs only the part that was lost: the PC keeps what
 * arrived under an upload id, and the next try continues from there. Works with an older PC too (it then starts
 * over each time). Used by "Send to PC", "Upload here" and camera backup.
 */
final class Uploader {
    /** Opens the file's bytes starting at {@code offset}. */
    interface Opener {
        InputStream open(long offset) throws IOException;
    }

    interface Progress {
        void on(long done, long total);
    }

    /** The PC said no (with its reason), or could not be reached ({@link #status} -1). */
    static final class Refused extends IOException {
        final int status;

        Refused(int status, String message) {
            super(message);
            this.status = status;
        }
    }

    /** PCs that answered "no such route" for resumable uploads; asked again after an hour. */
    private static final Map<String, Long> noResume = new HashMap<>();

    private Uploader() {
    }

    static String enc(String s) {
        try {
            return URLEncoder.encode(s, "UTF-8").replace("+", "%20");
        } catch (Exception e) {
            return s;
        }
    }

    /** 32 hex characters that stay the same for the same file, so a later try continues the same upload. */
    static String stableId(String... parts) {
        try {
            java.security.MessageDigest md = java.security.MessageDigest.getInstance("MD5");
            for (String p : parts) md.update((p == null ? "" : p).getBytes("UTF-8"));
            StringBuilder sb = new StringBuilder();
            for (byte b : md.digest()) sb.append(String.format("%02x", b));
            return sb.toString();
        } catch (Exception e) {
            return randomId();
        }
    }

    static String randomId() {
        return java.util.UUID.randomUUID().toString().replace("-", "");
    }

    private static HttpURLConnection open(String url, String token, String method, int readMs) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        c.setConnectTimeout(8000);
        c.setReadTimeout(readMs);
        c.setUseCaches(false);
        c.setRequestMethod(method);
        c.setRequestProperty("X-Pairing-Token", token);
        c.setRequestProperty("Connection", "close");
        return c;
    }

    private static String readAll(InputStream in) throws IOException {
        if (in == null) return "";
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[16 * 1024];
        for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
        return out.toString("UTF-8");
    }

    private static String reason(String body, int code) {
        try {
            String e = new JSONObject(body).optString("error", "");
            if (!e.isEmpty()) return e;
        } catch (JSONException ignored) {
        }
        if (code == 401) return "This phone is not paired with the PC any more. Pair again.";
        return "PC answered HTTP " + code;
    }

    /** How much of this upload the PC already has; -1 when the PC does not know resumable uploads. */
    private static long offsetOnPc(String server, String token, String uploadId, long total) throws IOException {
        Long until = noResume.get(server);
        if (until != null && SystemClock.elapsedRealtime() < until) return -1;
        HttpURLConnection c = open(server + "/api/upload/status?uploadId=" + uploadId, token, "GET", 15000);
        try {
            int code = c.getResponseCode();
            String body = readAll(code >= 400 ? c.getErrorStream() : c.getInputStream());
            if (code == 404 || code == 403) {
                noResume.put(server, SystemClock.elapsedRealtime() + 3600_000L);
                return -1;
            }
            if (code != 200) throw new Refused(code, reason(body, code));
            JSONObject o = new JSONObject(body);
            long have = o.optLong("offset", 0);
            long theirTotal = o.optLong("total", total);
            return theirTotal == total && have <= total ? have : 0;
        } catch (JSONException e) {
            return 0;
        } finally {
            c.disconnect();
        }
    }

    /**
     * Sends the file. {@code routeWithQuery} is the PC route with its own parameters, e.g.
     * "/api/upload?filename=a.jpg". Returns the PC's JSON answer. Throws {@link Refused} with the PC's reason, or an
     * IOException when the connection broke (a later call with the same id continues).
     */
    static JSONObject send(Context ctx, String routeWithQuery, Opener src, long size, String uploadId, Progress cb, AtomicBoolean stop) throws IOException {
        String server = PcLink.resolve(ctx);
        String token = TokenStore.pcToken(ctx);
        if (token.isEmpty()) throw new Refused(401, "Not paired with a PC yet. Open the Cloud-SSD app and pair it first.");
        if (server.isEmpty()) throw new Refused(-1, "PC not reachable. At home: same Wi-Fi. Away: Tailscale on in the phone and allowed on the PC.");

        boolean resumable = size > 0 && uploadId != null && !uploadId.isEmpty();
        long offset = 0;
        if (resumable) {
            long have = offsetOnPc(server, token, uploadId, size);
            if (have < 0) resumable = false;
            else offset = have;
        }

        for (int attempt = 0; attempt < 3; attempt++) {
            String url = server + routeWithQuery + (routeWithQuery.contains("?") ? "&" : "?")
                    + (resumable ? "uploadId=" + uploadId + "&offset=" + offset + "&total=" + size : "");
            HttpURLConnection c = open(url, token, "POST", 10 * 60 * 1000);
            try {
                c.setDoOutput(true);
                c.setRequestProperty("Content-Type", "application/octet-stream");
                long length = size > 0 ? size - offset : -1;
                if (length >= 0) c.setFixedLengthStreamingMode(length);
                else c.setChunkedStreamingMode(64 * 1024);
                long sent = 0;
                long lastAt = 0;
                try (InputStream in = src.open(offset); OutputStream out = c.getOutputStream()) {
                    if (in == null) throw new IOException("Cannot read the file");
                    byte[] buf = new byte[128 * 1024];
                    for (int n; (n = in.read(buf)) >= 0; ) {
                        if (stop != null && stop.get()) throw new IOException("cancelled");
                        if (length >= 0 && sent + n > length) n = (int) (length - sent);
                        if (n <= 0) break;
                        out.write(buf, 0, n);
                        sent += n;
                        long now = SystemClock.elapsedRealtime();
                        if (cb != null && now - lastAt >= 250) {
                            lastAt = now;
                            cb.on(offset + sent, size);
                        }
                    }
                }
                int code = c.getResponseCode();
                String body = readAll(code >= 400 ? c.getErrorStream() : c.getInputStream());
                if (code == 409 && resumable) {
                    // The PC has a different amount than we thought: continue from what it really has.
                    try {
                        offset = Math.max(0, new JSONObject(body).optLong("offset", 0));
                        continue;
                    } catch (JSONException ignored) {
                    }
                }
                if (code >= 400) throw new Refused(code, reason(body, code));
                JSONObject answer;
                try {
                    answer = body.isEmpty() ? new JSONObject() : new JSONObject(body);
                } catch (JSONException e) {
                    answer = new JSONObject();
                }
                if (resumable && answer.has("complete") && !answer.optBoolean("complete") && answer.has("offset")) {
                    offset = answer.optLong("offset", offset);
                    continue;
                }
                if (cb != null) cb.on(size > 0 ? size : sent, size);
                return answer;
            } catch (IOException e) {
                if (!(e instanceof Refused) && (stop == null || !stop.get())) PcLink.markDown(server);
                throw e;
            } finally {
                c.disconnect();
            }
        }
        throw new IOException("The PC kept asking for a different part of the file.");
    }
}
