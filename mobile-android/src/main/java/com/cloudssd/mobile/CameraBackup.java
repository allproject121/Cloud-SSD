package com.cloudssd.mobile;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.job.JobInfo;
import android.app.job.JobScheduler;
import android.content.ComponentName;
import android.content.ContentUris;
import android.content.ContentValues;
import android.content.Context;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.content.res.AssetFileDescriptor;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.os.SystemClock;
import android.provider.MediaStore;
import android.provider.Settings;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Camera backup: copies new photos and videos to the cloud drive ("Phone Backup/&lt;phone&gt;/&lt;folder&gt;/&lt;year-month&gt;").
 * By default the camera folder; the user can add others (Screenshots, WhatsApp…). It runs without the app being
 * open — a few times a day and shortly after a new photo — through the PC (at home, or away with Tailscale), or
 * straight to Google Drive when the PC is off and the phone is signed in. What was already copied is remembered on
 * the phone, and the PC/Drive is asked what it has, so nothing is sent twice. A file that was cut off continues
 * where it stopped next time.
 */
final class CameraBackup {
    static final int JOB_PERIODIC = 7101;
    static final int JOB_NEW_MEDIA = 7102;
    static final int JOB_NOW = 7103;
    static final int JOB_CONTINUE = 7104;

    private static final String PREFS = "cloudssd_backup";
    private static final String DEFAULT_FOLDER = "Camera";
    private static final String CHANNEL = "cloudssd_backup";
    private static final int PROGRESS = 7200;
    private static final int SUMMARY = 7201;
    /** Android stops a background job after about ten minutes; stop a little earlier and continue next time. */
    private static final long MAX_RUN_MS = 9 * 60 * 1000;

    private static final AtomicBoolean running = new AtomicBoolean();

    private CameraBackup() {
    }

    // ---- switch, options and schedule ------------------------------------------------------------------------------

    private static SharedPreferences prefs(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    static boolean enabled(Context c) {
        return prefs(c).getBoolean("enabled", false);
    }

    /** "wifi" (default) or "any" (also on mobile data, which only reaches the PC through Tailscale). */
    private static int networkType(Context c) {
        return "any".equals(prefs(c).getString("network", "wifi")) ? JobInfo.NETWORK_TYPE_ANY : JobInfo.NETWORK_TYPE_UNMETERED;
    }

    /** Battery rules the user chose: only while charging (off by default), not when the battery is low (on). */
    private static JobInfo.Builder constraints(Context c, JobInfo.Builder b) {
        SharedPreferences p = prefs(c);
        b.setRequiredNetworkType(networkType(c));
        b.setRequiresCharging(p.getBoolean("requireCharging", false));
        if (Build.VERSION.SDK_INT >= 26) b.setRequiresBatteryNotLow(p.getBoolean("requireBatteryNotLow", true));
        return b;
    }

    static void setEnabled(Context c, boolean on) {
        prefs(c).edit().putBoolean("enabled", on).apply();
        JobScheduler jobs = (JobScheduler) c.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (jobs == null) return;
        if (!on) {
            jobs.cancel(JOB_PERIODIC);
            jobs.cancel(JOB_NEW_MEDIA);
            jobs.cancel(JOB_NOW);
            jobs.cancel(JOB_CONTINUE);
            return;
        }
        // Survives a restart of the phone.
        jobs.schedule(constraints(c, new JobInfo.Builder(JOB_PERIODIC, service(c)))
                .setPeriodic(6 * 60 * 60 * 1000L)
                .setPersisted(true)
                .build());
        watchForNewMedia(c);
    }

    /** Saves the options ({requireCharging, requireBatteryNotLow, network, deviceName}) and re-plans the runs. */
    static void setOptions(Context c, JSONObject o) {
        SharedPreferences.Editor e = prefs(c).edit();
        if (o.has("requireCharging")) e.putBoolean("requireCharging", o.optBoolean("requireCharging"));
        if (o.has("requireBatteryNotLow")) e.putBoolean("requireBatteryNotLow", o.optBoolean("requireBatteryNotLow"));
        if (o.has("network")) e.putString("network", "any".equals(o.optString("network")) ? "any" : "wifi");
        if (o.has("directToDrive")) e.putBoolean("directToDrive", o.optBoolean("directToDrive"));
        if (o.has("deviceName")) {
            String name = o.optString("deviceName", "").replaceAll("[\\\\/:*?\"<>|\\p{Cntrl}]", "_").trim();
            if (!name.isEmpty()) e.putString("device", name.length() > 60 ? name.substring(0, 60) : name);
        }
        e.commit();
        if (enabled(c)) setEnabled(c, true);
    }

    /** The folders to back up ({bucketId, name}); empty = the camera folder only. */
    static void setFolders(Context c, JSONArray folders) {
        prefs(c).edit().putString("folders", folders == null ? "[]" : folders.toString()).apply();
    }

    private static ComponentName service(Context c) {
        return new ComponentName(c, CameraBackupService.class);
    }

    /** Runs the backup shortly after a photo or video is added. Android forgets this after each run, so it is set again. */
    static void watchForNewMedia(Context c) {
        JobScheduler jobs = (JobScheduler) c.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (jobs == null || !enabled(c)) return;
        jobs.schedule(constraints(c, new JobInfo.Builder(JOB_NEW_MEDIA, service(c)))
                .addTriggerContentUri(new JobInfo.TriggerContentUri(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, JobInfo.TriggerContentUri.FLAG_NOTIFY_FOR_DESCENDANTS))
                .addTriggerContentUri(new JobInfo.TriggerContentUri(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, JobInfo.TriggerContentUri.FLAG_NOTIFY_FOR_DESCENDANTS))
                .setTriggerContentUpdateDelay(10 * 1000L)      // a burst of photos becomes one run
                .setTriggerContentMaxDelay(2 * 60 * 1000L)
                .build());
    }

    /** "Back up now" in the app: any network, no battery rules — the user asked. */
    static void runNow(Context c) {
        JobScheduler jobs = (JobScheduler) c.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (jobs == null) return;
        jobs.schedule(new JobInfo.Builder(JOB_NOW, service(c))
                .setOverrideDeadline(0)
                .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
                .build());
    }

    /**
     * The folder name for this phone on the cloud drive. Existing users keep the model name they always had (so
     * nothing is sent again); a new install adds a short id, so two phones of the same model never share a folder.
     */
    static String deviceFolder(Context c) {
        SharedPreferences p = prefs(c);
        String saved = p.getString("device", "");
        if (!saved.isEmpty()) return saved;
        String model = Build.MODEL == null || Build.MODEL.trim().isEmpty() ? "Phone" : Build.MODEL.trim();
        String name;
        if (p.getInt("total", 0) > 0 || c.getDatabasePath("camera_backup.db").exists()) name = model;
        else {
            String aid = "";
            try {
                aid = Settings.Secure.getString(c.getContentResolver(), Settings.Secure.ANDROID_ID);
            } catch (Exception ignored) {
            }
            if (aid == null || aid.length() < 6) aid = Uploader.randomId();
            name = model + " (" + aid.substring(0, 6) + ")";
        }
        name = name.replaceAll("[\\\\/:*?\"<>|\\p{Cntrl}]", "_");
        p.edit().putString("device", name).apply();
        return name;
    }

    /** What the app shows: {enabled, running, lastRun, lastCount, total, waiting, error, folders, options, device}. */
    static String statusJson(Context c) {
        SharedPreferences p = prefs(c);
        try {
            return new JSONObject()
                    .put("enabled", p.getBoolean("enabled", false))
                    .put("running", running.get())
                    .put("lastRun", p.getLong("lastRun", 0))
                    .put("lastCount", p.getInt("lastCount", 0))
                    .put("total", p.getInt("total", 0))
                    .put("waiting", p.getInt("waiting", 0))
                    .put("error", p.getString("error", ""))
                    .put("via", p.getString("via", ""))
                    .put("folders", new JSONArray(p.getString("folders", "[]")))
                    .put("device", deviceFolder(c))
                    .put("options", new JSONObject()
                            .put("requireCharging", p.getBoolean("requireCharging", false))
                            .put("requireBatteryNotLow", p.getBoolean("requireBatteryNotLow", true))
                            .put("network", p.getString("network", "wifi"))
                            .put("directToDrive", p.getBoolean("directToDrive", true)))
                    .toString();
        } catch (JSONException e) {
            return "{}";
        }
    }

    /** Every photo/video folder on the phone with how many items it holds, for the "choose folders" sheet. */
    static JSONObject mediaFolders(Context c) {
        Map<String, long[]> counts = new LinkedHashMap<>();
        Map<String, String> names = new HashMap<>(), paths = new HashMap<>();
        Uri[] tables = {MediaStore.Images.Media.EXTERNAL_CONTENT_URI, MediaStore.Video.Media.EXTERNAL_CONTENT_URI};
        String[] cols = {"bucket_id", "bucket_display_name", MediaStore.MediaColumns.SIZE, MediaStore.MediaColumns.DATA};
        for (Uri t : tables) {
            try (Cursor cur = c.getContentResolver().query(t, cols, null, null, null)) {
                while (cur != null && cur.moveToNext()) {
                    String id = cur.getString(0);
                    if (id == null) continue;
                    long[] s = counts.get(id);
                    if (s == null) counts.put(id, s = new long[2]);
                    s[0]++;
                    s[1] += cur.getLong(2);
                    if (!names.containsKey(id)) names.put(id, cur.getString(1) == null ? "Folder" : cur.getString(1));
                    if (!paths.containsKey(id) && cur.getString(3) != null) {
                        String d = cur.getString(3);
                        paths.put(id, d.substring(0, Math.max(0, d.lastIndexOf('/'))));
                    }
                }
            } catch (SecurityException ignored) {
            }
        }
        Set<String> chosen = new HashSet<>();
        try {
            JSONArray f = new JSONArray(prefs(c).getString("folders", "[]"));
            for (int i = 0; i < f.length(); i++) chosen.add(f.getJSONObject(i).optString("bucketId"));
        } catch (JSONException ignored) {
        }
        JSONArray out = new JSONArray();
        try {
            for (Map.Entry<String, long[]> e : counts.entrySet()) {
                String path = paths.get(e.getKey());
                boolean camera = path != null && path.endsWith("/DCIM/Camera");
                out.put(new JSONObject().put("bucketId", e.getKey()).put("name", names.get(e.getKey())).put("path", path == null ? "" : path)
                        .put("count", e.getValue()[0]).put("bytes", e.getValue()[1])
                        .put("selected", chosen.isEmpty() ? camera : chosen.contains(e.getKey())));
            }
            return new JSONObject().put("folders", out);
        } catch (JSONException e) {
            return new JSONObject();
        }
    }

    // ---- what is already copied ------------------------------------------------------------------------------------

    private static final class Item {
        String kind;      // "image" or "video"
        long id;
        String name;
        long size;
        long modified;    // when it was taken, ms
        String path;
        Uri uri;
        String folder;    // the folder name on the cloud drive ("Camera", "Screenshots"…)
    }

    /** Remembers which items were copied, by MediaStore id plus size and time (an edited photo counts as new). */
    private static final class Ledger extends SQLiteOpenHelper {
        Ledger(Context c) {
            super(c, "camera_backup.db", null, 1);
        }

        @Override
        public void onCreate(SQLiteDatabase db) {
            db.execSQL("CREATE TABLE done (kind TEXT NOT NULL, id INTEGER NOT NULL, size INTEGER NOT NULL, modified INTEGER NOT NULL, PRIMARY KEY (kind, id))");
        }

        @Override
        public void onUpgrade(SQLiteDatabase db, int from, int to) {
        }

        Map<String, long[]> all() {
            Map<String, long[]> map = new HashMap<>();
            try (Cursor c = getReadableDatabase().rawQuery("SELECT kind, id, size, modified FROM done", null)) {
                while (c.moveToNext()) map.put(c.getString(0) + c.getLong(1), new long[]{c.getLong(2), c.getLong(3)});
            }
            return map;
        }

        void mark(Item it) {
            ContentValues v = new ContentValues();
            v.put("kind", it.kind);
            v.put("id", it.id);
            v.put("size", it.size);
            v.put("modified", it.modified);
            getWritableDatabase().insertWithOnConflict("done", null, v, SQLiteDatabase.CONFLICT_REPLACE);
        }
    }

    /** The chosen folders as {bucketId → name on the cloud}; empty means the camera folder. Two folders with one name get "2". */
    private static Map<String, String> chosenFolders(Context c) {
        Map<String, String> out = new LinkedHashMap<>();
        Set<String> used = new HashSet<>();
        try {
            JSONArray f = new JSONArray(prefs(c).getString("folders", "[]"));
            for (int i = 0; i < f.length(); i++) {
                JSONObject o = f.getJSONObject(i);
                String name = o.optString("name", "Folder").replaceAll("[\\\\/:*?\"<>|\\p{Cntrl}]", "_").trim();
                if (name.isEmpty()) name = "Folder";
                String unique = name;
                for (int n = 2; used.contains(unique.toLowerCase(Locale.ROOT)); n++) unique = name + " " + n;
                used.add(unique.toLowerCase(Locale.ROOT));
                out.put(o.optString("bucketId"), unique);
            }
        } catch (JSONException ignored) {
        }
        return out;
    }

    /** Photos and videos in the chosen folders that are not copied yet, oldest first. */
    private static List<Item> waiting(Context c, Ledger ledger) {
        Map<String, long[]> done = ledger.all();
        Map<String, String> folders = chosenFolders(c);
        List<Item> items = new ArrayList<>();
        Uri[] tables = {MediaStore.Images.Media.EXTERNAL_CONTENT_URI, MediaStore.Video.Media.EXTERNAL_CONTENT_URI};
        String[] columns = {MediaStore.MediaColumns._ID, MediaStore.MediaColumns.DISPLAY_NAME, MediaStore.MediaColumns.SIZE,
                MediaStore.MediaColumns.DATE_MODIFIED, "datetaken", MediaStore.MediaColumns.DATA, "bucket_id"};
        String where;
        String[] args;
        if (folders.isEmpty()) {
            boolean scoped = Build.VERSION.SDK_INT >= 29;
            where = (scoped ? MediaStore.MediaColumns.RELATIVE_PATH : MediaStore.MediaColumns.DATA) + " LIKE ?";
            args = new String[]{scoped ? "DCIM/Camera/%" : "%/DCIM/Camera/%"};
        } else {
            StringBuilder sb = new StringBuilder("bucket_id IN (");
            args = new String[folders.size()];
            int i = 0;
            for (String id : folders.keySet()) {
                sb.append(i == 0 ? "?" : ",?");
                args[i++] = id;
            }
            where = sb.append(")").toString();
        }
        for (int t = 0; t < tables.length; t++) {
            try (Cursor cur = c.getContentResolver().query(tables[t], columns, where, args, MediaStore.MediaColumns.DATE_MODIFIED + " ASC")) {
                while (cur != null && cur.moveToNext()) {
                    Item it = new Item();
                    it.kind = t == 0 ? "image" : "video";
                    it.id = cur.getLong(0);
                    it.name = cur.getString(1);
                    it.size = cur.getLong(2);
                    long taken = cur.isNull(4) ? 0 : cur.getLong(4);
                    it.modified = taken > 0 ? taken : cur.getLong(3) * 1000L;
                    it.path = cur.getString(5);
                    it.uri = ContentUris.withAppendedId(tables[t], it.id);
                    it.folder = folders.isEmpty() ? DEFAULT_FOLDER : folders.get(cur.getString(6));
                    if (it.folder == null) it.folder = DEFAULT_FOLDER;
                    if (it.name == null || it.name.isEmpty() || it.size <= 0) continue; // still being written by the camera
                    long[] was = done.get(it.kind + it.id);
                    if (was == null || was[0] != it.size || was[1] != it.modified) items.add(it);
                }
            } catch (SecurityException e) {
                // No permission to see photos (any more): nothing to do until the user allows it again.
            }
        }
        return items;
    }

    /**
     * Photos and videos camera backup already copied to the cloud (unchanged since) and taken more than
     * {@code minAgeMs} ago: removing them from the phone loses nothing. Each entry is {content Uri, path, size}.
     */
    static List<Object[]> backedUp(Context c, long minAgeMs) {
        List<Object[]> out = new ArrayList<>();
        Map<String, long[]> done = new Ledger(c).all();
        if (done.isEmpty()) return out;
        long before = System.currentTimeMillis() - minAgeMs;
        Uri[] tables = {MediaStore.Images.Media.EXTERNAL_CONTENT_URI, MediaStore.Video.Media.EXTERNAL_CONTENT_URI};
        String[] columns = {MediaStore.MediaColumns._ID, MediaStore.MediaColumns.SIZE, MediaStore.MediaColumns.DATE_MODIFIED, "datetaken", MediaStore.MediaColumns.DATA};
        for (int t = 0; t < tables.length; t++) {
            try (Cursor cur = c.getContentResolver().query(tables[t], columns, null, null, null)) {
                while (cur != null && cur.moveToNext()) {
                    long id = cur.getLong(0), size = cur.getLong(1);
                    long taken = cur.isNull(3) ? 0 : cur.getLong(3);
                    long modified = taken > 0 ? taken : cur.getLong(2) * 1000L;
                    long[] was = done.get((t == 0 ? "image" : "video") + id);
                    if (was == null || was[0] != size || was[1] != modified || modified > before) continue;
                    out.add(new Object[]{ContentUris.withAppendedId(tables[t], id), cur.getString(4), size});
                }
            } catch (SecurityException ignored) {
            }
        }
        return out;
    }

    /** The month folder of an item, in the phone's own time zone (the PC used its own before). */
    static String monthOf(long millis) {
        return new SimpleDateFormat("yyyy-MM", Locale.US).format(new Date(millis));
    }

    // ---- where the copies go -------------------------------------------------------------------------------------

    /** The PC (at home or through Tailscale) or Google Drive directly; both store the same folders. */
    private interface Target {
        /** "month|name|size" of what this folder already holds. */
        Set<String> have(String folder) throws IOException;

        void upload(Item it, AtomicBoolean stop) throws IOException;

        String label();
    }

    private static final class Refused extends IOException {
        final int status;

        Refused(int status, String message) {
            super(message);
            this.status = status;
        }
    }

    private static String readAll(InputStream in) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        if (in != null) {
            byte[] buf = new byte[16 * 1024];
            for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
        }
        return out.toString("UTF-8");
    }

    /** The item's bytes exactly as the camera wrote them (including where the photo was taken), from {@code offset}. */
    private static InputStream source(Context c, Item it, long offset) throws IOException {
        File f = it.path == null ? null : new File(it.path);
        boolean fullAccess = Build.VERSION.SDK_INT < 29 || (Build.VERSION.SDK_INT >= 30 && Environment.isExternalStorageManager());
        InputStream in;
        if (f != null && fullAccess && f.canRead()) in = new FileInputStream(f);
        else {
            Uri uri = it.uri;
            // Without this Android blanks the GPS position inside the photo it hands out.
            if (Build.VERSION.SDK_INT >= 29 && c.checkSelfPermission("android.permission.ACCESS_MEDIA_LOCATION") == PackageManager.PERMISSION_GRANTED)
                uri = MediaStore.setRequireOriginal(uri);
            AssetFileDescriptor fd = c.getContentResolver().openAssetFileDescriptor(uri, "r");
            if (fd == null) throw new IOException("Cannot read " + it.name);
            in = fd.createInputStream();
        }
        if (offset > 0) {
            if (in instanceof FileInputStream) ((FileInputStream) in).getChannel().position(offset);
            else for (long left = offset; left > 0; ) {
                long n = in.skip(left);
                if (n <= 0) throw new IOException("Cannot continue " + it.name);
                left -= n;
            }
        }
        return in;
    }

    private static final class PcTarget implements Target {
        final Context c;
        final String server, token, device;

        PcTarget(Context c, String server, String token, String device) {
            this.c = c;
            this.server = server;
            this.token = token;
            this.device = device;
        }

        @Override
        public Set<String> have(String folder) throws IOException {
            HttpURLConnection conn = (HttpURLConnection) new URL(server + "/api/backup/index?device=" + Uploader.enc(device) + "&folder=" + Uploader.enc(folder)).openConnection();
            conn.setConnectTimeout(3000);
            conn.setReadTimeout(60000);
            conn.setRequestProperty("X-Pairing-Token", token);
            conn.setRequestProperty("Connection", "close");
            Set<String> have = new HashSet<>();
            try {
                int code = conn.getResponseCode();
                String body = readAll(code >= 400 ? conn.getErrorStream() : conn.getInputStream());
                if (code >= 400) {
                    String msg = "The PC refused the backup (" + code + ").";
                    try {
                        msg = new JSONObject(body).optString("error", msg);
                    } catch (JSONException ignored) {
                    }
                    throw new Refused(code, msg);
                }
                JSONArray files = new JSONObject(body).optJSONArray("files");
                for (int i = 0; files != null && i < files.length(); i++) {
                    JSONObject f = files.getJSONObject(i);
                    have.add(f.optString("month") + "|" + f.optString("name") + "|" + f.optLong("size"));
                }
            } catch (JSONException e) {
                throw new IOException("Unexpected answer from the PC.", e);
            } finally {
                conn.disconnect();
            }
            return have;
        }

        @Override
        public void upload(final Item it, AtomicBoolean stop) throws IOException {
            String route = "/api/backup/upload?device=" + Uploader.enc(device) + "&folder=" + Uploader.enc(it.folder) + "&name=" + Uploader.enc(it.name)
                    + "&modified=" + it.modified + "&month=" + monthOf(it.modified) + "&size=" + it.size;
            // The same id every time for the same photo, so a run that was cut off continues the same upload.
            String id = Uploader.stableId(device, it.kind, String.valueOf(it.id), String.valueOf(it.size), String.valueOf(it.modified));
            try {
                Uploader.send(c, route, offset -> source(c, it, offset), it.size, id, null, stop);
            } catch (Uploader.Refused r) {
                throw new Refused(r.status, r.getMessage());
            }
        }

        @Override
        public String label() {
            return "PC";
        }
    }

    private static final class DriveTarget implements Target {
        final Context c;
        final CloudBackend drive;
        final String base;

        DriveTarget(Context c, CloudBackend drive, String device) {
            this.c = c;
            this.drive = drive;
            this.base = "cloudssd:" + "Phone Backup/" + device;
        }

        @Override
        public Set<String> have(String folder) throws IOException {
            Set<String> have = new HashSet<>();
            String dir = base + "/" + folder;
            List<CloudBackend.DocInfo> months;
            try {
                months = drive.list(dir);
            } catch (CloudBackend.BackendException e) {
                if (e.status == 404) return have;
                throw e;
            }
            for (CloudBackend.DocInfo m : months) {
                if (!m.dir || !m.name.matches("\\d{4}-\\d{2}")) continue;
                for (CloudBackend.DocInfo f : drive.list(m.id)) if (!f.dir) have.add(m.name + "|" + f.name + "|" + f.size);
            }
            return have;
        }

        @Override
        public void upload(Item it, AtomicBoolean stop) throws IOException {
            String folder = base + "/" + it.folder + "/" + monthOf(it.modified);
            Backends.ensureFolder(drive, folder);
            String name = it.name;
            // Same name, different photo (two phones, an edited copy): keep both, as the PC does.
            for (int n = 1; ; n++) {
                try {
                    drive.stat(folder + "/" + name);
                } catch (CloudBackend.BackendException e) {
                    if (e.status == 404) break;
                    throw e;
                }
                int dot = it.name.lastIndexOf('.');
                name = dot > 0 ? it.name.substring(0, dot) + " (" + n + ")" + it.name.substring(dot) : it.name + " (" + n + ")";
            }
            try (InputStream in = source(c, it, 0)) {
                drive.write(folder + "/" + name, in);
            }
        }

        @Override
        public String label() {
            return "Google Drive";
        }
    }

    // ---- one run ---------------------------------------------------------------------------------------------------

    /** Copies what is waiting. Returns quietly when neither the PC nor Google Drive is reachable; safe to call at any time. */
    static void run(Context c, AtomicBoolean stop) {
        if (!running.compareAndSet(false, true)) return;
        try {
            copy(c.getApplicationContext(), stop);
        } finally {
            running.set(false);
            cancelProgress(c);
        }
    }

    /** Re-creates the schedule if Android dropped it (after an app update, for example). */
    static void ensureScheduled(Context c) {
        if (!enabled(c)) return;
        JobScheduler jobs = (JobScheduler) c.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (jobs == null) return;
        if (jobs.getPendingJob(JOB_PERIODIC) == null) setEnabled(c, true);
        else if (jobs.getPendingJob(JOB_NEW_MEDIA) == null) watchForNewMedia(c);
    }

    private static void save(Context c, int sent, int left, String error, String via) {
        SharedPreferences p = prefs(c);
        p.edit().putLong("lastRun", System.currentTimeMillis()).putInt("lastCount", sent).putInt("total", p.getInt("total", 0) + sent)
                .putInt("waiting", Math.max(0, left)).putString("error", error).putString("via", via)
                // Ran out of time with the target still there: the rest follows in a moment (see continueIfMore).
                .putBoolean("more", left > 0 && error.isEmpty()).commit();
    }

    /**
     * A first backup can be thousands of photos, more than fits in one ten-minute run: when the last run stopped only
     * because its time was up, the next one starts right away instead of hours later.
     */
    static void continueIfMore(Context c) {
        JobScheduler jobs = (JobScheduler) c.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if (jobs == null || !enabled(c) || !prefs(c).getBoolean("more", false)) return;
        prefs(c).edit().putBoolean("more", false).apply();
        jobs.schedule(constraints(c, new JobInfo.Builder(JOB_CONTINUE, service(c)))
                .setMinimumLatency(3000)
                .setOverrideDeadline(60 * 1000L)
                .build());
    }

    /** The PC when it answers; Google Drive when it does not and the phone may back up there directly; else null. */
    private static Target target(Context c, String device) {
        if (PcLink.isPaired(c)) {
            String server = PcLink.resolve(c);
            if (!server.isEmpty()) return new PcTarget(c, server, TokenStore.pcToken(c), device);
        }
        // A backup folder chosen on the PC is a local folder there: Drive would put the photos somewhere else.
        boolean customOnPc = c.getSharedPreferences("cloudssd", Context.MODE_PRIVATE).getBoolean("backup_custom", false);
        if (prefs(c).getBoolean("directToDrive", true) && !customOnPc && Backends.driveReady(c)) return new DriveTarget(c, Backends.drive(c), device);
        return null;
    }

    /** Returns how many items were sent. */
    private static int copy(Context c, AtomicBoolean stop) {
        String device = deviceFolder(c);
        Ledger ledger = new Ledger(c);
        try {
            List<Item> todo = waiting(c, ledger);
            int left = todo.size();
            if (todo.isEmpty()) {
                save(c, 0, 0, "", "");
                return 0;
            }
            Target target = target(c, device);
            if (target == null) {
                // Not at home, PC off, no Drive sign-in: try again at the next run, without bothering anyone.
                prefs(c).edit().putInt("waiting", left).putString("error", "").apply();
                return 0;
            }
            Map<String, Set<String>> have = new HashMap<>();
            int sent = 0;
            String error = "";
            long deadline = SystemClock.elapsedRealtime() + MAX_RUN_MS;
            int total = todo.size();
            for (Item it : todo) {
                if (stop.get() || SystemClock.elapsedRealtime() > deadline) break;
                try {
                    Set<String> there = have.get(it.folder);
                    if (there == null) have.put(it.folder, there = target.have(it.folder));
                    if (there.contains(monthOf(it.modified) + "|" + it.name + "|" + it.size)) {
                        ledger.mark(it);
                        left--;
                        continue;
                    }
                    notifyProgress(c, total - left + 1, total, it.name, target.label());
                    target.upload(it, stop);
                    ledger.mark(it);
                    left--;
                    sent++;
                } catch (Refused e) {
                    error = e.getMessage();
                    if (e.status != 400) break;   // 400 = this one file cannot be stored; anything else stops the run
                } catch (CloudBackend.BackendException e) {
                    error = e.getMessage();
                    if (e.status <= 0 || e.status == 401 || e.status == 403 || e.status == 507) break;
                } catch (IOException e) {
                    if (!stop.get()) error = "The connection was lost.";
                    break;
                }
            }
            save(c, sent, left, error, target.label());
            if (sent > 0) notifyDone(c, sent, Math.max(0, left), target.label());
            return sent;
        } finally {
            ledger.close();
        }
    }

    // ---- notifications ---------------------------------------------------------------------------------------------

    private static Notification.Builder builder(Context c) {
        NotificationManager nm = (NotificationManager) c.getSystemService(Context.NOTIFICATION_SERVICE);
        if (Build.VERSION.SDK_INT >= 26) {
            if (nm != null) nm.createNotificationChannel(new NotificationChannel(CHANNEL, "Camera backup", NotificationManager.IMPORTANCE_LOW));
            return new Notification.Builder(c, CHANNEL);
        }
        return new Notification.Builder(c);
    }

    private static void show(Context c, int id, Notification.Builder b) {
        try {
            NotificationManager nm = (NotificationManager) c.getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null) nm.notify(id, b.build());
        } catch (RuntimeException ignored) {
            // Notifications switched off for the app: the backup itself is not affected.
        }
    }

    private static void notifyProgress(Context c, int index, int total, String name, String via) {
        show(c, PROGRESS, builder(c).setSmallIcon(android.R.drawable.stat_sys_upload).setContentTitle("Camera backup · " + index + " of " + total)
                .setContentText(name + " → " + via).setOngoing(true).setProgress(total, index - 1, false));
    }

    private static void notifyDone(Context c, int sent, int left, String via) {
        show(c, SUMMARY, builder(c).setSmallIcon(android.R.drawable.stat_sys_upload_done).setContentTitle("Camera backup")
                .setContentText(sent + (sent == 1 ? " new item saved" : " new items saved") + " via " + via + (left > 0 ? " · " + left + " waiting" : ""))
                .setAutoCancel(true));
    }

    private static void cancelProgress(Context c) {
        NotificationManager nm = (NotificationManager) c.getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm != null) nm.cancel(PROGRESS);
    }
}
