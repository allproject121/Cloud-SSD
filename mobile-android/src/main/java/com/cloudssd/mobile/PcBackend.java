package com.cloudssd.mobile;

import android.content.Context;
import android.content.SharedPreferences;
import android.os.SystemClock;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import org.json.JSONTokener;

import java.io.ByteArrayOutputStream;
import java.io.FilterInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.ConnectException;
import java.net.HttpURLConnection;
import java.net.NoRouteToHostException;
import java.net.SocketTimeoutException;
import java.net.URL;
import java.net.URLEncoder;
import java.net.UnknownHostException;
import java.util.ArrayList;
import java.util.List;

/**
 * The cloud drives as served by the Cloud-SSD app on the PC (its /api/fs/ routes). The PC's address and the pairing
 * token are the ones the app saved when it was paired, so this works without the app being open.
 */
final class PcBackend implements CloudBackend {
    private static final int CONNECT_MS = 2500;
    private static final int READ_MS = 10000;
    /** Deleting a big folder or moving between accounts takes the PC a while. */
    private static final int SLOW_MS = 5 * 60 * 1000;
    /** The first bytes of a file that is not cached on the PC yet come from the internet. */
    private static final int STREAM_MS = 30000;
    /** After the PC did not answer, further requests fail at once for this long instead of each waiting again. */
    private static final long DOWN_MS = 5000;

    private static volatile long downUntil;

    private final Context context;

    PcBackend(Context context) {
        this.context = context.getApplicationContext();
    }

    private SharedPreferences prefs() {
        return context.getSharedPreferences("cloudssd", Context.MODE_PRIVATE);
    }

    @Override
    public boolean isConfigured() {
        return PcLink.isPaired(context);
    }

    @Override
    public String label() {
        return "PC";
    }

    // ---- requests ------------------------------------------------------------------------------------------------

    private static String enc(String s) throws IOException {
        return URLEncoder.encode(s, "UTF-8").replace("+", "%20");
    }

    /**
     * A request to the PC at the address that reaches it now (home Wi-Fi or Tailscale), checked to be our PC before
     * the key goes there (see {@link PcLink}).
     */
    private HttpURLConnection open(String method, String route, String query, int readMs) throws IOException {
        String token = TokenStore.pcToken(context);
        if (!isConfigured() || token.isEmpty())
            throw new BackendException(BackendException.NOT_PAIRED, "Not paired with a PC yet. Open the Cloud-SSD app and pair it first.");
        if (SystemClock.elapsedRealtime() < downUntil) throw unreachable();
        String server = PcLink.resolve(context);
        if (server.isEmpty()) {
            downUntil = SystemClock.elapsedRealtime() + DOWN_MS;
            throw unreachable();
        }
        HttpURLConnection c = (HttpURLConnection) new URL(server + route + (query.isEmpty() ? "" : "?" + query)).openConnection();
        c.setConnectTimeout(CONNECT_MS);
        c.setReadTimeout(readMs);
        c.setUseCaches(false);
        c.setRequestMethod(method);
        c.setRequestProperty("X-Pairing-Token", token);
        c.setRequestProperty("Connection", "close"); // the PC answers one request per connection
        return c;
    }

    private static BackendException unreachable() {
        return new BackendException(BackendException.UNREACHABLE,
                "PC not reachable. At home keep this phone on the same Wi-Fi; away, turn Tailscale on (and allow it on the PC).");
    }

    private static void connect(HttpURLConnection c) throws IOException {
        try {
            c.connect();
        } catch (ConnectException | SocketTimeoutException | NoRouteToHostException | UnknownHostException e) {
            downUntil = SystemClock.elapsedRealtime() + DOWN_MS;
            PcLink.markDown(c.getURL().getProtocol() + "://" + c.getURL().getAuthority());
            c.disconnect();
            throw unreachable();
        }
    }

    /** True when the PC told the app it can do this (see the "features" the page saves after each status check). */
    private boolean pcHas(String feature) {
        return prefs().getString("features", "").contains("\"" + feature + "\"");
    }

    private static String readAll(InputStream in) throws IOException {
        if (in == null) return "";
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[16 * 1024];
        for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
        return out.toString("UTF-8");
    }

    /** Throws the PC's own explanation when it refused. */
    private static void check(HttpURLConnection c) throws IOException {
        int code = c.getResponseCode();
        if (code < 400) return;
        String text = readAll(c.getErrorStream());
        String message = "The PC refused this (" + code + ").";
        try {
            String error = new JSONObject(text).optString("error", "");
            if (!error.isEmpty()) message = error;
        } catch (JSONException ignored) {
        }
        throw new BackendException(code, message);
    }

    private static Object answer(HttpURLConnection c) throws IOException {
        try {
            check(c);
            String text = readAll(c.getInputStream());
            return text.isEmpty() ? null : new JSONTokener(text).nextValue();
        } catch (JSONException e) {
            throw new IOException("Unexpected answer from the PC.", e);
        } finally {
            c.disconnect();
        }
    }

    private Object get(String route, String query) throws IOException {
        HttpURLConnection c = open("GET", route, query, READ_MS);
        connect(c);
        return answer(c);
    }

    private Object post(String route, String query, int readMs) throws IOException {
        HttpURLConnection c = open("POST", route, query, readMs);
        c.setDoOutput(true);
        c.setFixedLengthStreamingMode(0);
        connect(c);
        c.getOutputStream().close();
        return answer(c);
    }

    private static JSONObject object(Object o) throws IOException {
        if (o instanceof JSONObject) return (JSONObject) o;
        throw new IOException("Unexpected answer from the PC.");
    }

    private static DocInfo doc(JSONObject o) {
        DocInfo d = new DocInfo();
        d.id = o.optString("id");
        d.name = o.optString("name");
        d.dir = o.optBoolean("isDirectory");
        d.size = o.optLong("size", 0);
        d.modified = o.optLong("modified", 0);
        d.canEdit = o.optBoolean("canEdit");
        d.canCreate = o.optBoolean("canCreate");
        return d;
    }

    // ---- CloudBackend --------------------------------------------------------------------------------------------

    @Override
    public List<RootInfo> roots() throws IOException {
        Object o = get("/api/fs/roots", "");
        if (!(o instanceof JSONArray)) throw new IOException("Unexpected answer from the PC.");
        JSONArray a = (JSONArray) o;
        List<RootInfo> roots = new ArrayList<>();
        for (int i = 0; i < a.length(); i++) {
            JSONObject r = a.optJSONObject(i);
            if (r == null) continue;
            RootInfo info = new RootInfo();
            info.id = r.optString("id");
            info.title = r.optString("title", info.id);
            info.readOnly = r.optBoolean("readOnly");
            info.canCreate = r.optBoolean("canCreate");
            info.totalBytes = r.optLong("totalBytes", -1);
            info.freeBytes = r.optLong("freeBytes", -1);
            roots.add(info);
        }
        return roots;
    }

    @Override
    public List<DocInfo> list(String folderId) throws IOException {
        JSONArray a = object(get("/api/fs/list", "path=" + enc(folderId))).optJSONArray("entries");
        List<DocInfo> docs = new ArrayList<>();
        for (int i = 0; a != null && i < a.length(); i++) {
            JSONObject e = a.optJSONObject(i);
            if (e != null) docs.add(doc(e));
        }
        return docs;
    }

    @Override
    public DocInfo stat(String id) throws IOException {
        return doc(object(get("/api/fs/stat", "path=" + enc(id))));
    }

    @Override
    public DocInfo create(String parentId, String name, boolean dir) throws IOException {
        return doc(object(post("/api/fs/create", "parent=" + enc(parentId) + "&name=" + enc(name) + "&dir=" + (dir ? 1 : 0), READ_MS)));
    }

    @Override
    public DocInfo rename(String id, String name) throws IOException {
        return doc(object(post("/api/fs/rename", "path=" + enc(id) + "&name=" + enc(name), SLOW_MS)));
    }

    @Override
    public DocInfo move(String id, String toFolderId) throws IOException {
        return doc(object(post("/api/fs/move", "path=" + enc(id) + "&to=" + enc(toFolderId), SLOW_MS)));
    }

    @Override
    public void delete(String id) throws IOException {
        post("/api/fs/delete", "path=" + enc(id), SLOW_MS);
    }

    @Override
    public InputStream openRead(String id, long offset) throws IOException {
        final HttpURLConnection c = open("GET", "/api/fs/read", "path=" + enc(id), STREAM_MS);
        if (offset > 0) c.setRequestProperty("Range", "bytes=" + offset + "-");
        connect(c);
        try {
            if (c.getResponseCode() == 416) { // asked to start at or after the end
                c.disconnect();
                return new java.io.ByteArrayInputStream(new byte[0]);
            }
            check(c);
            return new FilterInputStream(c.getInputStream()) {
                @Override
                public void close() {
                    // Not super.close(): that would first read the rest of the file to reuse the connection.
                    c.disconnect();
                }
            };
        } catch (IOException e) {
            c.disconnect();
            throw e;
        }
    }

    @Override
    public DocInfo write(String id, InputStream data) throws IOException {
        HttpURLConnection c = open("POST", "/api/fs/write", "path=" + enc(id) + "&overwrite=1", SLOW_MS);
        c.setDoOutput(true);
        c.setChunkedStreamingMode(64 * 1024);
        c.setRequestProperty("Content-Type", "application/octet-stream");
        connect(c);
        try {
            OutputStream out = c.getOutputStream();
            byte[] buf = new byte[64 * 1024];
            for (int n; (n = data.read(buf, 0, buf.length)) >= 0; ) out.write(buf, 0, n);
            out.close(); // sends the final chunk: only now does the PC keep the file
        } catch (IOException e) {
            c.disconnect(); // no final chunk, so the PC throws the partial upload away
            throw e;
        }
        return doc(object(answer(c)));
    }

    @Override
    public InputStream thumbnail(String id, int px) throws IOException {
        if (!pcHas("fs-thumb")) return null;
        HttpURLConnection c = open("GET", "/api/fs/thumb", "path=" + enc(id) + "&w=" + px, STREAM_MS);
        connect(c);
        try {
            if (c.getResponseCode() != 200) return null;
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            try (InputStream in = c.getInputStream()) {
                byte[] buf = new byte[16 * 1024];
                for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
            }
            return new java.io.ByteArrayInputStream(out.toByteArray());
        } finally {
            c.disconnect();
        }
    }

    @Override
    public List<DocInfo> search(String folderId, String query, int max) throws IOException {
        if (!pcHas("fs-search")) throw new BackendException(404, "Update Cloud-SSD on the PC to search here.");
        JSONArray a = object(get("/api/fs/search", "path=" + enc(folderId) + "&q=" + enc(query) + "&limit=" + max + "&depth=6")).optJSONArray("entries");
        List<DocInfo> docs = new ArrayList<>();
        for (int i = 0; a != null && i < a.length(); i++) {
            JSONObject e = a.optJSONObject(i);
            if (e != null && e.optJSONObject("entry") != null) docs.add(doc(e.optJSONObject("entry")));
        }
        return docs;
    }
}
