package com.cloudssd.mobile;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.FilterInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.Random;
import java.util.TimeZone;

/**
 * The few Google Drive (REST v3) calls the phone needs, with plain HttpURLConnection. Busy answers from Google
 * (429, 5xx, "rate limit exceeded") are retried with growing pauses; anything else becomes a BackendException with a
 * readable message.
 */
final class DriveApi {
    private static final String API = "https://www.googleapis.com/drive/v3";
    private static final String UPLOAD = "https://www.googleapis.com/upload/drive/v3/files";
    static final String FOLDER_MIME = "application/vnd.google-apps.folder";
    static final String FILE_FIELDS = "id,name,mimeType,size,modifiedTime,createdTime,parents,capabilities(canEdit,canAddChildren),shortcutDetails,thumbnailLink";
    private static final int CHUNK = 8 * 1024 * 1024;   // a multiple of 256 KiB, as Google requires
    private static final Random jitter = new Random();

    private DriveApi() {
    }

    static String readAll(InputStream in) throws IOException {
        if (in == null) return "";
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[16 * 1024];
        for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
        return out.toString("UTF-8");
    }

    static String q(String s) {
        return s.replace("\\", "\\\\").replace("'", "\\'");
    }

    private static String enc(String s) {
        return GoogleAuth.enc(s);
    }

    private static HttpURLConnection open(String token, String method, String url) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        c.setConnectTimeout(15000);
        c.setReadTimeout(60000);
        c.setInstanceFollowRedirects(false);
        c.setUseCaches(false);
        if ("PATCH".equals(method)) {
            // HttpURLConnection has no PATCH; Google accepts it as an override header.
            c.setRequestMethod("POST");
            c.setRequestProperty("X-HTTP-Method-Override", "PATCH");
        } else c.setRequestMethod(method);
        c.setRequestProperty("Authorization", "Bearer " + token);
        return c;
    }

    private static boolean busy(int code, String body) {
        return code == 429 || code >= 500 || (code == 403 && (body.contains("rateLimitExceeded") || body.contains("userRateLimitExceeded")));
    }

    private static CloudBackend.BackendException failure(int code, String body) {
        String msg = "Google Drive answered HTTP " + code;
        try {
            JSONObject e = new JSONObject(body).optJSONObject("error");
            if (e != null) msg = e.optString("message", msg);
        } catch (JSONException ignored) {
        }
        if (body.contains("storageQuotaExceeded")) msg = "This Google account is full.";
        if (code == 404) msg = "Not found on Google Drive.";
        if (code == 401) msg = "Google sign-in expired. Sign in again on this phone.";
        return new CloudBackend.BackendException(code, msg);
    }

    private static void pause(int attempt) {
        try {
            Thread.sleep((long) (Math.min(16000, 500L << attempt)) + jitter.nextInt(400));
        } catch (InterruptedException ignored) {
            Thread.currentThread().interrupt();
        }
    }

    /** A JSON call with retries; {@code body} null for none. */
    static JSONObject call(String token, String method, String url, JSONObject body) throws IOException {
        for (int attempt = 0; ; attempt++) {
            HttpURLConnection c = open(token, method, url);
            try {
                if (body != null) {
                    byte[] b = body.toString().getBytes(StandardCharsets.UTF_8);
                    c.setDoOutput(true);
                    c.setRequestProperty("Content-Type", "application/json; charset=UTF-8");
                    c.setFixedLengthStreamingMode(b.length);
                    try (OutputStream o = c.getOutputStream()) {
                        o.write(b);
                    }
                }
                int code = c.getResponseCode();
                String text = readAll(code >= 400 ? c.getErrorStream() : c.getInputStream());
                if (code < 300) return text.isEmpty() ? new JSONObject() : new JSONObject(text);
                if (busy(code, text) && attempt < 5) {
                    pause(attempt);
                    continue;
                }
                throw failure(code, text);
            } catch (JSONException e) {
                throw new IOException("Unexpected answer from Google Drive.");
            } finally {
                c.disconnect();
            }
        }
    }

    static JSONObject about(String token) throws IOException {
        return call(token, "GET", API + "/about?fields=" + enc("user(emailAddress,displayName),storageQuota(limit,usage)"), null);
    }

    static JSONObject get(String token, String id) throws IOException {
        return call(token, "GET", API + "/files/" + enc(id) + "?supportsAllDrives=true&fields=" + enc(FILE_FIELDS), null);
    }

    /** Everything directly in a folder (all pages). */
    static List<JSONObject> children(String token, String parentId) throws IOException {
        return query(token, "'" + q(parentId) + "' in parents and trashed = false", 0);
    }

    /** Items matching a Drive query; {@code max} 0 = all. */
    static List<JSONObject> query(String token, String query, int max) throws IOException {
        List<JSONObject> out = new ArrayList<>();
        String page = null;
        do {
            String url = API + "/files?pageSize=1000&spaces=drive&q=" + enc(query) + "&fields=" + enc("nextPageToken,files(" + FILE_FIELDS + ")")
                    + (page == null ? "" : "&pageToken=" + enc(page));
            JSONObject r = call(token, "GET", url, null);
            JSONArray files = r.optJSONArray("files");
            for (int i = 0; files != null && i < files.length(); i++) {
                out.add(files.optJSONObject(i));
                if (max > 0 && out.size() >= max) return out;
            }
            page = r.optString("nextPageToken", null);
            if (page != null && page.isEmpty()) page = null;
        } while (page != null);
        return out;
    }

    static JSONObject createFolder(String token, String parentId, String name) throws IOException {
        try {
            JSONObject meta = new JSONObject().put("name", name).put("mimeType", FOLDER_MIME).put("parents", new JSONArray().put(parentId));
            return call(token, "POST", API + "/files?fields=" + enc(FILE_FIELDS), meta);
        } catch (JSONException e) {
            throw new IOException(e);
        }
    }

    /** Rename, move (add/remove parents) or trash, in one PATCH. */
    static JSONObject update(String token, String id, JSONObject meta, String addParent, String removeParent) throws IOException {
        String url = API + "/files/" + enc(id) + "?fields=" + enc(FILE_FIELDS)
                + (addParent == null ? "" : "&addParents=" + enc(addParent)) + (removeParent == null ? "" : "&removeParents=" + enc(removeParent));
        return call(token, "PATCH", url, meta == null ? new JSONObject() : meta);
    }

    /** The bytes of a file from {@code offset}; Google Docs are exported (Word/Excel/PowerPoint). */
    static InputStream read(String token, String id, String exportMime, long offset) throws IOException {
        String url = exportMime == null ? API + "/files/" + enc(id) + "?alt=media&supportsAllDrives=true"
                : API + "/files/" + enc(id) + "/export?mimeType=" + enc(exportMime);
        for (int attempt = 0; ; attempt++) {
            final HttpURLConnection c = open(token, "GET", url);
            c.setReadTimeout(60000);
            if (offset > 0 && exportMime == null) c.setRequestProperty("Range", "bytes=" + offset + "-");
            int code = c.getResponseCode();
            if (code == 200 || code == 206) {
                InputStream in = c.getInputStream();
                // An export cannot start in the middle: skip what was asked to be left out.
                if (offset > 0 && (code == 200)) for (long left = offset; left > 0; ) {
                    long n = in.skip(left);
                    if (n <= 0) break;
                    left -= n;
                }
                return new FilterInputStream(in) {
                    @Override
                    public void close() {
                        c.disconnect();
                    }
                };
            }
            String text = readAll(c.getErrorStream());
            c.disconnect();
            if (code == 416) return new java.io.ByteArrayInputStream(new byte[0]);
            if (busy(code, text) && attempt < 5) {
                pause(attempt);
                continue;
            }
            throw failure(code, text);
        }
    }

    private static String rfc3339(long ms) {
        SimpleDateFormat f = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.US);
        f.setTimeZone(TimeZone.getTimeZone("UTC"));
        return f.format(new Date(ms));
    }

    /**
     * Uploads a new file into a folder, or new content for an existing file ({@code fileId} not null), in 8 MiB
     * pieces. Nothing appears on Drive until the last piece arrives, so a cut-off upload never leaves half a file.
     */
    static JSONObject upload(String token, String parentId, String fileId, String name, long modifiedMs, InputStream data) throws IOException {
        JSONObject meta = new JSONObject();
        try {
            if (fileId == null) {
                meta.put("name", name);
                meta.put("parents", new JSONArray().put(parentId));
            }
            if (modifiedMs > 0) meta.put("modifiedTime", rfc3339(modifiedMs));
        } catch (JSONException e) {
            throw new IOException(e);
        }
        String start = fileId == null ? UPLOAD + "?uploadType=resumable&fields=" + enc(FILE_FIELDS)
                : UPLOAD + "/" + enc(fileId) + "?uploadType=resumable&fields=" + enc(FILE_FIELDS);
        String session = null;
        for (int attempt = 0; session == null; attempt++) {
            HttpURLConnection c = open(token, fileId == null ? "POST" : "PATCH", start);
            try {
                byte[] b = meta.toString().getBytes(StandardCharsets.UTF_8);
                c.setDoOutput(true);
                c.setRequestProperty("Content-Type", "application/json; charset=UTF-8");
                c.setFixedLengthStreamingMode(b.length);
                try (OutputStream o = c.getOutputStream()) {
                    o.write(b);
                }
                int code = c.getResponseCode();
                if (code == 200) session = c.getHeaderField("Location");
                else {
                    String text = readAll(c.getErrorStream());
                    if (busy(code, text) && attempt < 5) pause(attempt);
                    else throw failure(code, text);
                }
            } finally {
                c.disconnect();
            }
        }

        byte[] buf = new byte[CHUNK];
        long sent = 0;
        while (true) {
            int filled = 0;
            for (int n; filled < CHUNK && (n = data.read(buf, filled, CHUNK - filled)) >= 0; ) filled += n;
            boolean last = filled < CHUNK;
            // A file that is an exact multiple of the piece size ends with an empty last piece.
            String range = filled == 0 ? "bytes */" + sent
                    : "bytes " + sent + "-" + (sent + filled - 1) + "/" + (last ? String.valueOf(sent + filled) : "*");
            for (int attempt = 0; ; attempt++) {
                HttpURLConnection c = (HttpURLConnection) new URL(session).openConnection();
                try {
                    c.setConnectTimeout(15000);
                    c.setReadTimeout(120000);
                    c.setRequestMethod("PUT");
                    c.setDoOutput(true);
                    c.setFixedLengthStreamingMode(filled);
                    c.setRequestProperty("Content-Range", range);
                    try (OutputStream o = c.getOutputStream()) {
                        o.write(buf, 0, filled);
                    }
                    int code = c.getResponseCode();
                    if (code == 200 || code == 201) {
                        try {
                            return new JSONObject(readAll(c.getInputStream()));
                        } catch (JSONException e) {
                            throw new IOException("Unexpected answer from Google Drive.");
                        }
                    }
                    if (code == 308) break;   // piece stored, send the next one
                    String text = readAll(c.getErrorStream());
                    if (busy(code, text) && attempt < 5) {
                        pause(attempt);
                        continue;
                    }
                    throw failure(code, text);
                } finally {
                    c.disconnect();
                }
            }
            sent += filled;
            if (last) throw new IOException("Google Drive did not finish the upload.");
        }
    }
}
