package com.cloudssd.mobile;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.ContentValues;
import android.content.Context;
import android.content.Intent;
import android.content.res.AssetFileDescriptor;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.os.ParcelFileDescriptor;
import android.os.SystemClock;
import android.provider.MediaStore;
import android.webkit.MimeTypeMap;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Every transfer between this phone and the PC (or Google Drive): a queue that keeps going when the app is
 * closed (it runs in {@link TransferService}), two files at a time, retries by itself after a broken connection, and
 * continues from where it stopped instead of starting over. What is queued is kept in a small database, so a
 * transfer that was cut off can still be retried after the app was closed.
 */
final class TransferHub {
    interface Listener {
        void onTransfer(JSONObject t);
    }

    static final String CHANNEL = "cloudssd_transfers";
    static final int SUMMARY_ID = 4999;
    private static final long[] RETRY_DELAYS_MS = {5_000, 20_000, 60_000};

    private static TransferHub instance;

    static synchronized TransferHub get(Context c) {
        if (instance == null) instance = new TransferHub(c.getApplicationContext());
        return instance;
    }

    private final Context ctx;
    private final Store store;
    private final ExecutorService pool;
    private final ScheduledExecutorService timer = Executors.newSingleThreadScheduledExecutor();
    private final List<Listener> listeners = new CopyOnWriteArrayList<>();
    private final Map<Long, AtomicBoolean> cancel = new ConcurrentHashMap<>();
    private final Map<Long, double[]> speed = new ConcurrentHashMap<>();   // {bytesPerSec, lastBytes, lastAt}
    private final Map<Long, Long> retryAt = new ConcurrentHashMap<>();

    private TransferHub(Context c) {
        ctx = c;
        store = new Store(c);
        int parallel = Math.max(1, Math.min(3, c.getSharedPreferences("cloudssd", Context.MODE_PRIVATE).getInt("parallel", 2)));
        pool = Executors.newFixedThreadPool(parallel);
        createChannel();
        // Transfers that were running when the app was killed: nothing is running them now. They can be retried
        // (and continue where they stopped).
        store.interrupted();
    }

    void addListener(Listener l) {
        listeners.add(l);
    }

    void removeListener(Listener l) {
        listeners.remove(l);
    }

    // ---- starting -------------------------------------------------------------------------------------------------

    /**
     * @param srcKind "file" (a path), "uri" (content the app may keep reading) or "uri-temp" (shared by another app:
     *                readable only while this process lives)
     * @param target  "" = the PC's upload folder, else a cloud folder id ("cloudssd:Photos")
     */
    long enqueueUpload(String name, long size, String srcKind, String src, String target) {
        ContentValues v = new ContentValues();
        v.put("type", "upload");
        v.put("name", name);
        v.put("srcKind", srcKind);
        v.put("src", src);
        v.put("target", target == null ? "" : target);
        v.put("size", size);
        v.put("uploadId", Uploader.randomId());
        v.put("state", "queued");
        long id = store.insert(v);
        submit(id);
        return id;
    }

    /**
     * Moves a phone file to the cloud: uploaded like any other, and the file on the phone is deleted only after the
     * upload was confirmed (Space &amp; RAM, "Move to the cloud").
     */
    long enqueueMove(String name, long size, String path, String target) {
        ContentValues v = new ContentValues();
        v.put("type", "upload");
        v.put("name", name);
        v.put("srcKind", "file");
        v.put("src", path);
        v.put("target", target);
        v.put("size", size);
        v.put("uploadId", Uploader.randomId());
        v.put("state", "queued");
        v.put("deleteSrc", 1);
        long id = store.insert(v);
        submit(id);
        return id;
    }

    /** @param route a PC route with its parameters and no key ("/api/download?path=…"), or "cloud:" + an item id */
    long enqueueDownload(String route, String name, long size) {
        ContentValues v = new ContentValues();
        v.put("type", "download");
        v.put("name", name);
        v.put("srcKind", route.startsWith("cloud:") ? "cloud" : "pc");
        v.put("src", route.startsWith("cloud:") ? route.substring(6) : route);
        v.put("size", size);
        v.put("state", "queued");
        long id = store.insert(v);
        submit(id);
        return id;
    }

    void retry(long id) {
        ContentValues v = new ContentValues();
        v.put("state", "queued");
        v.put("error", "");
        v.put("attempts", 0);
        store.update(id, v);
        submit(id);
    }

    void cancel(long id) {
        AtomicBoolean f = cancel.get(id);
        if (f != null) f.set(true);
        JSONObject row = store.row(id);
        if (row != null && ("queued".equals(row.optString("state")) || "waiting".equals(row.optString("state")))) {
            retryAt.remove(id);
            setState(id, "cancelled", "Cancelled");
            dropPartial(row);
        }
    }

    /** Removes a failed or finished transfer from the list (and the half file it left, if any). */
    void dismiss(long id) {
        JSONObject row = store.row(id);
        if (row == null) return;
        String state = row.optString("state");
        if ("active".equals(state) || "queued".equals(state)) {
            cancel(id);
            return;
        }
        dropPartial(row);
        releaseUri(row);
        store.delete(id);
    }

    /** Android ended the background time for today: stop cleanly; each can be retried later. */
    void pauseAll(String why) {
        for (Map.Entry<Long, AtomicBoolean> e : cancel.entrySet()) {
            e.getValue().set(true);
            ContentValues v = new ContentValues();
            v.put("state", "failed");
            v.put("error", why);
            store.update(e.getKey(), v);
        }
    }

    private void submit(final long id) {
        cancel.put(id, new AtomicBoolean(false));
        publish(id);
        TransferService.start(ctx);
        pool.execute(() -> run(id));
    }

    boolean busy() {
        return store.count("state IN ('queued','active','waiting')") > 0;
    }

    JSONArray snapshot() {
        return store.visible();
    }

    // ---- running --------------------------------------------------------------------------------------------------

    private void run(long id) {
        JSONObject row = store.row(id);
        AtomicBoolean stop = cancel.get(id);
        if (row == null || stop == null || stop.get() || !"queued".equals(row.optString("state"))) {
            TransferService.stopIfIdle(ctx);
            return;
        }
        setState(id, "active", "");
        speed.put(id, new double[]{0, row.optLong("bytes", 0), SystemClock.elapsedRealtime()});
        try {
            if ("upload".equals(row.optString("type"))) upload(id, row, stop);
            else download(id, row, stop);
            ContentValues v = new ContentValues();
            v.put("state", "done");
            v.put("error", "");
            v.put("finished", System.currentTimeMillis());
            if (row.optInt("deleteSrc") == 1 && "file".equals(row.optString("srcKind"))) {
                File src = new File(row.optString("src"));
                if (src.delete()) {
                    v.put("note", "Moved to the cloud");
                    android.media.MediaScannerConnection.scanFile(ctx, new String[]{src.getAbsolutePath()}, null, null);
                }
            }
            store.update(id, v);
            releaseUri(store.row(id));
            notifyDone(id);
        } catch (Exception e) {
            boolean cancelled = stop.get();
            JSONObject now = store.row(id);
            if (cancelled && now != null && "failed".equals(now.optString("state"))) {
                // paused by pauseAll: keep its message
            } else if (cancelled) {
                setState(id, "cancelled", "Cancelled");
                dropPartial(now);
            } else {
                failOrRetry(id, e);
            }
        } finally {
            cancel.remove(id);
            speed.remove(id);
            publish(id);
            TransferService.stopIfIdle(ctx);
        }
    }

    /** A dropped connection is tried again by itself (after 5 s, 20 s, 60 s); a refusal by the PC is not. */
    private void failOrRetry(final long id, Exception e) {
        JSONObject row = store.row(id);
        int attempts = row == null ? 0 : row.optInt("attempts", 0);
        boolean refused = e instanceof Uploader.Refused && ((Uploader.Refused) e).status > 0
                || e instanceof CloudBackend.BackendException && ((CloudBackend.BackendException) e).status > 0;
        String msg = friendly(e);
        if (!refused && attempts < RETRY_DELAYS_MS.length && row != null && !"uri-temp-gone".equals(msg)) {
            long delay = RETRY_DELAYS_MS[attempts];
            ContentValues v = new ContentValues();
            v.put("state", "waiting");
            v.put("error", msg);
            v.put("attempts", attempts + 1);
            store.update(id, v);
            retryAt.put(id, System.currentTimeMillis() + delay);
            timer.schedule(() -> {
                retryAt.remove(id);
                JSONObject r = store.row(id);
                if (r == null || !"waiting".equals(r.optString("state"))) return;
                ContentValues q = new ContentValues();
                q.put("state", "queued");
                store.update(id, q);
                submit(id);
            }, delay, TimeUnit.MILLISECONDS);
        } else {
            setState(id, "failed", msg);
            notifyFailed(id, msg);
        }
    }

    static String friendly(Exception e) {
        String m = e.getMessage();
        if (e instanceof java.net.SocketTimeoutException || e instanceof java.net.ConnectException || e instanceof java.net.NoRouteToHostException
                || e instanceof java.net.UnknownHostException) {
            return "PC not reachable. At home: same Wi-Fi. Away: Tailscale on.";
        }
        if (e instanceof java.net.SocketException) return "The connection to the PC was lost.";
        return m == null || m.isEmpty() ? e.getClass().getSimpleName() : m;
    }

    private void progress(long id, long bytes) {
        double[] s = speed.get(id);
        long now = SystemClock.elapsedRealtime();
        if (s != null && now - s[2] >= 300) {
            s[0] = (bytes - s[1]) * 1000.0 / (now - s[2]);
            s[1] = bytes;
            s[2] = now;
        }
        ContentValues v = new ContentValues();
        v.put("bytes", bytes);
        store.updateQuiet(id, v);
        publish(id);
    }

    // ---- uploads --------------------------------------------------------------------------------------------------

    private Uploader.Opener opener(final JSONObject row) {
        final String kind = row.optString("srcKind");
        final String src = row.optString("src");
        return offset -> {
            InputStream in;
            if ("file".equals(kind)) {
                FileInputStream f = new FileInputStream(new File(src));
                if (offset > 0) f.getChannel().position(offset);
                return f;
            }
            try {
                AssetFileDescriptor fd = ctx.getContentResolver().openAssetFileDescriptor(Uri.parse(src), "r");
                if (fd == null) throw new IOException("Cannot read " + row.optString("name"));
                in = fd.createInputStream();
            } catch (SecurityException e) {
                throw new IOException("uri-temp".equals(kind) ? "Share the file again: Android no longer lets the app read it." : "Android no longer lets the app read this file.");
            }
            if (offset > 0) {
                if (in instanceof FileInputStream) ((FileInputStream) in).getChannel().position(offset);
                else for (long left = offset; left > 0; ) {
                    long n = in.skip(left);
                    if (n <= 0) throw new IOException("Cannot continue this file from the middle.");
                    left -= n;
                }
            }
            return in;
        };
    }

    private static String cleanName(String name) {
        String clean = name.replaceAll("[\\\\/:*?\"<>|\\p{Cntrl}]", "_").replaceAll("[. ]+$", "");
        return clean.isEmpty() ? "upload_" + System.currentTimeMillis() : clean;
    }

    private void upload(final long id, JSONObject row, AtomicBoolean stop) throws IOException {
        String name = row.optString("name");
        String target = row.optString("target", "");
        long size = row.optLong("size", -1);
        String route = target.isEmpty()
                ? "/api/upload?filename=" + Uploader.enc(name)
                : "/api/fs/write?overwrite=0&mkdirs=1&path=" + Uploader.enc(target + (target.endsWith(":") ? "" : "/") + cleanName(name));
        try {
            Uploader.send(ctx, route, opener(row), size, row.optString("uploadId"), (done, total) -> progress(id, done), stop);
        } catch (Uploader.Refused r) {
            // PC off, but Google Drive is signed in on this phone: the file goes straight to the cloud instead.
            if (r.status != -1 || !Backends.driveReady(ctx)) throw r;
            String folder = target.isEmpty() ? "cloudssd:From phone" : target;
            CloudBackend drive = Backends.drive(ctx);
            Backends.ensureFolder(drive, folder);
            try (InputStream in = opener(row).open(0)) {
                final long[] sent = {0};
                InputStream counted = new java.io.FilterInputStream(in) {
                    @Override
                    public int read(byte[] b, int off, int len) throws IOException {
                        if (stop.get()) throw new IOException("cancelled");
                        int n = super.read(b, off, len);
                        if (n > 0) progress(id, sent[0] += n);
                        return n;
                    }
                };
                drive.write(folder + (folder.endsWith(":") ? "" : "/") + cleanName(name), counted);
            }
            ContentValues v = new ContentValues();
            v.put("note", "Saved to Google Drive (" + folder.substring(folder.indexOf(':') + 1) + ") — the PC was off");
            store.updateQuiet(id, v);
        }
    }

    // ---- downloads ------------------------------------------------------------------------------------------------

    private static String mimeOf(String name) {
        int dot = name.lastIndexOf('.');
        String m = dot < 0 ? null : MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substring(dot + 1).toLowerCase(Locale.ROOT));
        return m != null ? m : "application/octet-stream";
    }

    private static String readAll(InputStream in) throws IOException {
        if (in == null) return "";
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
        return out.toString("UTF-8");
    }

    /** Where the file is being written: a pending Downloads item (Android 10+) or a hidden part file (older). */
    private OutputStream openDest(long id, JSONObject row, long[] have) throws IOException {
        String name = row.optString("name");
        String dest = row.optString("dest", "");
        if (Build.VERSION.SDK_INT >= 29) {
            Uri uri = dest.isEmpty() ? null : Uri.parse(dest);
            if (uri != null) {
                try (ParcelFileDescriptor pfd = ctx.getContentResolver().openFileDescriptor(uri, "r")) {
                    have[0] = pfd == null ? 0 : pfd.getStatSize();
                } catch (Exception e) {
                    uri = null; // the half file was removed meanwhile
                }
            }
            if (uri == null) {
                ContentValues values = new ContentValues();
                values.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
                values.put(MediaStore.MediaColumns.MIME_TYPE, mimeOf(name));
                values.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS);
                values.put(MediaStore.MediaColumns.IS_PENDING, 1);
                uri = ctx.getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values);
                if (uri == null) throw new IOException("Cannot create the file in Downloads");
                ContentValues v = new ContentValues();
                v.put("dest", uri.toString());
                store.updateQuiet(id, v);
                have[0] = 0;
            }
            return ctx.getContentResolver().openOutputStream(uri, have[0] > 0 ? "wa" : "w");
        }
        File dir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS);
        if (!dir.exists()) dir.mkdirs();
        File part = dest.isEmpty() ? new File(dir, "." + name + ".cssd-part") : new File(dest);
        if (dest.isEmpty()) {
            ContentValues v = new ContentValues();
            v.put("dest", part.getAbsolutePath());
            store.updateQuiet(id, v);
        }
        have[0] = part.exists() ? part.length() : 0;
        return new FileOutputStream(part, have[0] > 0);
    }

    private void truncateDest(JSONObject row) throws IOException {
        String dest = store.row(row.optLong("id")).optString("dest", "");
        if (dest.isEmpty()) return;
        if (Build.VERSION.SDK_INT >= 29) {
            try (OutputStream o = ctx.getContentResolver().openOutputStream(Uri.parse(dest), "wt")) {
                if (o != null) o.flush();
            }
        } else {
            new FileOutputStream(dest, false).close();
        }
    }

    private void download(long id, JSONObject row, AtomicBoolean stop) throws IOException {
        long[] have = {0};
        OutputStream out = openDest(id, row, have);
        if (out == null) throw new IOException("Cannot write to Downloads");
        long total = row.optLong("size", -1);
        InputStream in;
        HttpURLConnection conn = null;
        if ("cloud".equals(row.optString("srcKind"))) {
            in = Backends.files(ctx).openRead(row.optString("src"), have[0]);
        } else {
            String server = PcLink.resolve(ctx);
            if (server.isEmpty()) {
                out.close();
                throw new Uploader.Refused(-1, "PC not reachable. At home: same Wi-Fi. Away: Tailscale on.");
            }
            conn = (HttpURLConnection) new URL(server + row.optString("src")).openConnection();
            conn.setRequestProperty("X-Pairing-Token", TokenStore.pcToken(ctx));
            conn.setConnectTimeout(10000);
            conn.setReadTimeout(60000);
            conn.setRequestProperty("Connection", "close");
            String etag = row.optString("etag", "");
            if (have[0] > 0) {
                conn.setRequestProperty("Range", "bytes=" + have[0] + "-");
                if (!etag.isEmpty()) conn.setRequestProperty("If-Range", etag);
            }
            int code = conn.getResponseCode();
            if (code == 416 && have[0] > 0 && (total <= 0 || have[0] >= total)) {
                out.close();
                finishDownload(id, row);
                conn.disconnect();
                return;
            }
            if (code != 200 && code != 206) {
                out.close();
                String body = readAll(conn.getErrorStream());
                String msg = "PC answered HTTP " + code;
                try {
                    msg = new JSONObject(body).optString("error", msg);
                } catch (Exception ignored) {
                }
                conn.disconnect();
                throw new Uploader.Refused(code, msg);
            }
            if (code == 200 && have[0] > 0) {
                // Older PC, or the file changed: start over.
                out.close();
                truncateDest(row);
                have[0] = 0;
                out = openDest(id, store.row(id), new long[1]);
            }
            String newTag = conn.getHeaderField("ETag");
            long len = conn.getContentLengthLong();
            ContentValues v = new ContentValues();
            if (newTag != null) v.put("etag", newTag);
            if (len > 0) v.put("size", total = have[0] + len);
            store.updateQuiet(id, v);
            in = conn.getInputStream();
        }
        long got = have[0];
        try (InputStream i = in; OutputStream o = out) {
            byte[] buf = new byte[128 * 1024];
            for (int n; (n = i.read(buf)) != -1; ) {
                if (stop.get()) throw new IOException("cancelled");
                o.write(buf, 0, n);
                got += n;
                progress(id, got);
            }
            o.flush();
        } finally {
            if (conn != null) conn.disconnect();
        }
        if (total > 0 && got < total) throw new IOException("The connection dropped before the file was complete");
        finishDownload(id, store.row(id));
    }

    private void finishDownload(long id, JSONObject row) {
        String dest = row.optString("dest", "");
        if (Build.VERSION.SDK_INT >= 29) {
            ContentValues done = new ContentValues();
            done.put(MediaStore.MediaColumns.IS_PENDING, 0);
            ctx.getContentResolver().update(Uri.parse(dest), done, null, null);
        } else {
            File part = new File(dest);
            String name = row.optString("name");
            File target = new File(part.getParentFile(), name);
            for (int n = 1; target.exists(); n++) {
                int dot = name.lastIndexOf('.');
                target = new File(part.getParentFile(), dot > 0 ? name.substring(0, dot) + " (" + n + ")" + name.substring(dot) : name + " (" + n + ")");
            }
            if (part.renameTo(target)) {
                ContentValues v = new ContentValues();
                v.put("dest", target.getAbsolutePath());
                store.updateQuiet(id, v);
                Intent scan = new Intent(Intent.ACTION_MEDIA_SCANNER_SCAN_FILE);
                scan.setData(Uri.fromFile(target));
                ctx.sendBroadcast(scan);
            }
        }
    }

    /** A cancelled or dismissed download: never leave half a file looking like a finished one. */
    private void dropPartial(JSONObject row) {
        if (row == null || !"download".equals(row.optString("type")) || "done".equals(row.optString("state"))) return;
        String dest = row.optString("dest", "");
        if (dest.isEmpty()) return;
        try {
            if (Build.VERSION.SDK_INT >= 29) ctx.getContentResolver().delete(Uri.parse(dest), null, null);
            else new File(dest).delete();
        } catch (Exception ignored) {
        }
    }

    private void releaseUri(JSONObject row) {
        if (row == null || !"uri".equals(row.optString("srcKind"))) return;
        try {
            ctx.getContentResolver().releasePersistableUriPermission(Uri.parse(row.optString("src")), Intent.FLAG_GRANT_READ_URI_PERMISSION);
        } catch (Exception ignored) {
        }
    }

    // ---- telling the page and the notification shade -------------------------------------------------------------

    private void setState(long id, String state, String error) {
        ContentValues v = new ContentValues();
        v.put("state", state);
        v.put("error", error == null ? "" : error);
        store.update(id, v);
        publish(id);
    }

    private JSONObject event(JSONObject row) {
        long id = row.optLong("id");
        JSONObject o = new JSONObject();
        try {
            String state = row.optString("state");
            o.put("id", id);
            o.put("type", row.optString("type"));
            o.put("name", row.optString("name"));
            o.put("state", state);
            o.put("bytes", row.optLong("bytes"));
            o.put("total", row.optLong("size", -1));
            double[] s = speed.get(id);
            o.put("speed", s == null ? 0 : Math.round(s[0]));
            String err = row.optString("error", "");
            if (!err.isEmpty() && !"active".equals(state)) o.put("error", err);
            if (!row.optString("note", "").isEmpty()) o.put("note", row.optString("note"));
            o.put("canRetry", "failed".equals(state) || "cancelled".equals(state));
            o.put("resumable", row.optLong("bytes") > 0 && row.optLong("size", -1) > 0);
            o.put("attempt", row.optInt("attempts", 0));
            Long at = retryAt.get(id);
            if (at != null) o.put("retryIn", Math.max(0, (at - System.currentTimeMillis()) / 1000));
        } catch (Exception ignored) {
        }
        return o;
    }

    private void publish(long id) {
        JSONObject row = store.row(id);
        if (row == null) return;
        JSONObject e = event(row);
        for (Listener l : listeners) l.onTransfer(e);
        notifyProgress(row, e);
    }

    private void createChannel() {
        if (Build.VERSION.SDK_INT < 26) return;
        NotificationChannel ch = new NotificationChannel(CHANNEL, "File Transfers", NotificationManager.IMPORTANCE_LOW);
        ch.setDescription("Shows file transfers between this phone and the PC");
        ch.enableVibration(false);
        NotificationManager nm = (NotificationManager) ctx.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm != null) nm.createNotificationChannel(ch);
    }

    Notification.Builder builder(String title, String text, int icon) {
        Notification.Builder b = Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(ctx, CHANNEL) : new Notification.Builder(ctx);
        Intent open = new Intent(ctx, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP);
        int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 23 ? PendingIntent.FLAG_IMMUTABLE : 0);
        return b.setSmallIcon(icon).setContentTitle(title).setContentText(text).setOnlyAlertOnce(true)
                .setContentIntent(PendingIntent.getActivity(ctx, 0, open, flags));
    }

    private void post(int id, Notification.Builder b) {
        NotificationManager nm = (NotificationManager) ctx.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm == null) return;
        try {
            nm.notify(id, b.build());
        } catch (Exception ignored) {
        }
    }

    private final Map<Long, Long> lastNotified = new ConcurrentHashMap<>();

    private void notifyProgress(JSONObject row, JSONObject e) {
        long id = row.optLong("id");
        if (!"active".equals(e.optString("state"))) return;
        long now = SystemClock.elapsedRealtime();
        Long last = lastNotified.get(id);
        if (last != null && now - last < 700) return;
        lastNotified.put(id, now);
        boolean up = "upload".equals(e.optString("type"));
        long total = e.optLong("total", -1), done = e.optLong("bytes");
        post((int) (5000 + id), builder((up ? "Sending " : "Downloading ") + e.optString("name"),
                fmt(e.optLong("speed")) + "/s • " + fmt(done) + (total > 0 ? " / " + fmt(total) : ""),
                up ? android.R.drawable.stat_sys_upload : android.R.drawable.stat_sys_download)
                .setOngoing(true).setProgress(100, total > 0 ? (int) (done * 100 / total) : 0, total <= 0));
    }

    private void notifyDone(long id) {
        JSONObject row = store.row(id);
        if (row == null) return;
        boolean up = "upload".equals(row.optString("type"));
        String note = row.optString("note", "");
        Notification.Builder b = builder(up ? (note.isEmpty() ? "Sent to PC" : "Saved to Google Drive") : "Saved to Downloads",
                row.optString("name") + " (" + fmt(row.optLong("bytes")) + ")",
                up ? android.R.drawable.stat_sys_upload_done : android.R.drawable.stat_sys_download_done).setAutoCancel(true);
        if (!up) {
            try {
                String dest = row.optString("dest");
                Uri uri = Build.VERSION.SDK_INT >= 29 ? Uri.parse(dest) : Uri.fromFile(new File(dest));
                Intent open = new Intent(Intent.ACTION_VIEW).setDataAndType(uri, mimeOf(row.optString("name")))
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 23 ? PendingIntent.FLAG_IMMUTABLE : 0);
                b.setContentIntent(PendingIntent.getActivity(ctx, (int) (5000 + id), open, flags));
            } catch (Exception ignored) {
            }
        }
        post((int) (5000 + id), b);
    }

    private void notifyFailed(long id, String msg) {
        JSONObject row = store.row(id);
        if (row == null) return;
        post((int) (5000 + id), builder("upload".equals(row.optString("type")) ? "Upload stopped" : "Download stopped",
                row.optString("name") + ": " + msg + " — open the app to retry", android.R.drawable.stat_notify_error).setAutoCancel(true));
    }

    static String fmt(long bytes) {
        if (bytes <= 0) return "0 B";
        final String[] units = {"B", "KB", "MB", "GB", "TB"};
        int g = Math.min(units.length - 1, (int) (Math.log10(bytes) / Math.log10(1024)));
        return String.format(Locale.US, "%.1f %s", bytes / Math.pow(1024, g), units[g]);
    }

    // ---- storage ------------------------------------------------------------------------------------------------

    private static final class Store extends SQLiteOpenHelper {
        Store(Context c) {
            super(c, "transfers.db", null, 2);
        }

        @Override
        public void onCreate(SQLiteDatabase db) {
            db.execSQL("CREATE TABLE t (id INTEGER PRIMARY KEY AUTOINCREMENT, type TEXT, name TEXT, srcKind TEXT, src TEXT, target TEXT, "
                    + "size INTEGER DEFAULT -1, uploadId TEXT, dest TEXT, etag TEXT, bytes INTEGER DEFAULT 0, state TEXT, error TEXT, "
                    + "note TEXT, attempts INTEGER DEFAULT 0, created INTEGER, finished INTEGER DEFAULT 0, deleteSrc INTEGER DEFAULT 0)");
        }

        @Override
        public void onUpgrade(SQLiteDatabase db, int from, int to) {
            if (from < 2) db.execSQL("ALTER TABLE t ADD COLUMN deleteSrc INTEGER DEFAULT 0");
        }

        synchronized long insert(ContentValues v) {
            v.put("created", System.currentTimeMillis());
            return getWritableDatabase().insert("t", null, v);
        }

        synchronized void update(long id, ContentValues v) {
            getWritableDatabase().update("t", v, "id=?", new String[]{String.valueOf(id)});
        }

        void updateQuiet(long id, ContentValues v) {
            update(id, v);
        }

        synchronized void delete(long id) {
            getWritableDatabase().delete("t", "id=?", new String[]{String.valueOf(id)});
        }

        synchronized int count(String where) {
            try (Cursor c = getReadableDatabase().rawQuery("SELECT COUNT(*) FROM t WHERE " + where, null)) {
                return c.moveToFirst() ? c.getInt(0) : 0;
            }
        }

        synchronized void interrupted() {
            ContentValues v = new ContentValues();
            v.put("state", "failed");
            v.put("error", "Stopped when the app was closed — tap Retry to continue");
            getWritableDatabase().update("t", v, "state IN ('queued','active','waiting')", null);
            // Keep the list short: finished ones older than a day go.
            getWritableDatabase().delete("t", "state IN ('done','cancelled') AND created < ?",
                    new String[]{String.valueOf(System.currentTimeMillis() - 86400_000L)});
        }

        private static JSONObject toJson(Cursor c) {
            JSONObject o = new JSONObject();
            try {
                for (int i = 0; i < c.getColumnCount(); i++) {
                    if (c.isNull(i)) continue;
                    if (c.getType(i) == Cursor.FIELD_TYPE_INTEGER) o.put(c.getColumnName(i), c.getLong(i));
                    else o.put(c.getColumnName(i), c.getString(i));
                }
            } catch (Exception ignored) {
            }
            return o;
        }

        synchronized JSONObject row(long id) {
            try (Cursor c = getReadableDatabase().rawQuery("SELECT * FROM t WHERE id=?", new String[]{String.valueOf(id)})) {
                return c.moveToFirst() ? toJson(c) : null;
            }
        }

        /** What the page lists: everything not finished, plus what finished in the last two minutes. */
        synchronized JSONArray visible() {
            JSONArray a = new JSONArray();
            try (Cursor c = getReadableDatabase().rawQuery("SELECT * FROM t WHERE state NOT IN ('done','cancelled') OR finished > ? ORDER BY id",
                    new String[]{String.valueOf(System.currentTimeMillis() - 120_000L)})) {
                while (c.moveToNext()) a.put(toJson(c));
            }
            return a;
        }
    }

    /** The rows as the page wants them (same shape as live events). */
    JSONArray events() {
        JSONArray rows = store.visible();
        JSONArray out = new JSONArray();
        for (int i = 0; i < rows.length(); i++) out.put(event(rows.optJSONObject(i)));
        return out;
    }

    List<Long> ids() {
        JSONArray rows = store.visible();
        List<Long> out = new ArrayList<>();
        for (int i = 0; i < rows.length(); i++) out.add(rows.optJSONObject(i).optLong("id"));
        return out;
    }
}
