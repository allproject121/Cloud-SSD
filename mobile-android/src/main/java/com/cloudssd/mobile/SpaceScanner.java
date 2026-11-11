package com.cloudssd.mobile;

import android.app.AppOpsManager;
import android.app.usage.StorageStats;
import android.app.usage.StorageStatsManager;
import android.app.usage.UsageStats;
import android.app.usage.UsageStatsManager;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.os.Build;
import android.os.Environment;
import android.os.Process;
import android.os.storage.StorageManager;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Date;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TimeZone;
import java.util.concurrent.ConcurrentHashMap;

/**
 * "Space &amp; RAM" on the phone: where the phone's storage goes (folders with their sizes, apps, what can be
 * cleaned, the biggest files) and its RAM, in the same JSON shapes the PC uses, so one screen shows either device
 * and the PC can show the phone. The storage is read in one walk over the phone's own storage, at most once a minute,
 * and kept; folders are measured on demand when they were too small to remember.
 */
final class SpaceScanner {
    static final String ROOT = Environment.getExternalStorageDirectory().getAbsolutePath();
    /** Folders at least this big are remembered after a walk; smaller ones are measured when opened. */
    private static final long KEEP_DIR = 256 * 1024;
    /** Files at least this big are the "Largest files" list. */
    private static final long BIG_FILE = 50L * 1024 * 1024;

    private static final Map<String, long[]> dirs = new ConcurrentHashMap<>();   // path → {bytes, files, modified}
    private static final List<Object[]> big = Collections.synchronizedList(new ArrayList<>()); // {path, bytes, modified}
    private static volatile long scannedAt;
    private static volatile boolean scanning;

    private SpaceScanner() {
    }

    // ---- the walk -------------------------------------------------------------------------------------------------------

    /** Walks the phone's storage again when the last walk is older than {@code maxAgeMs}. */
    static void ensureFresh(long maxAgeMs) {
        if (scanning || System.currentTimeMillis() - scannedAt < maxAgeMs) return;
        synchronized (SpaceScanner.class) {
            if (scanning) return;
            scanning = true;
        }
        try {
            Map<String, long[]> found = new HashMap<>();
            List<Object[]> bigFound = new ArrayList<>();
            walk(new File(ROOT), found, bigFound, 0);
            dirs.clear();
            dirs.putAll(found);
            synchronized (big) {
                big.clear();
                big.addAll(bigFound);
            }
            scannedAt = System.currentTimeMillis();
        } finally {
            scanning = false;
        }
    }

    static long scannedAt() {
        return scannedAt;
    }

    /** Size and file count of a folder and everything in it; remembers folders big enough to matter. */
    private static long[] walk(File dir, Map<String, long[]> out, List<Object[]> bigOut, int depth) {
        long bytes = 0, files = 0;
        File[] list = depth > 40 ? null : dir.listFiles();
        if (list != null) {
            for (File f : list) {
                if (isLink(f)) continue;
                if (f.isDirectory()) {
                    long[] sub = walk(f, out, bigOut, depth + 1);
                    bytes += sub[0];
                    files += sub[1];
                } else {
                    long len = f.length();
                    bytes += len;
                    files++;
                    if (bigOut != null && len >= BIG_FILE) bigOut.add(new Object[]{f.getAbsolutePath(), len, f.lastModified()});
                }
            }
        }
        long[] r = {bytes, files, dir.lastModified()};
        if (out != null && bytes >= KEEP_DIR) out.put(dir.getAbsolutePath(), r);
        return r;
    }

    private static boolean isLink(File f) {
        try {
            File parent = f.getParentFile();
            File canon = parent == null ? f : new File(parent.getCanonicalFile(), f.getName());
            return !canon.getCanonicalFile().equals(canon.getAbsoluteFile());
        } catch (Exception e) {
            return true;
        }
    }

    /** Size of a folder: remembered from the last walk, else measured now (it was small). */
    private static long[] sizeOf(File dir) {
        long[] known = dirs.get(dir.getAbsolutePath());
        return known != null ? known : walk(dir, null, null, 0);
    }

    // ---- what may be touched ----------------------------------------------------------------------------------------------

    /** The storage root itself and Android's own app folders are never offered for deleting. */
    static boolean isProtected(String path) {
        if (path == null) return true;
        String p = path.endsWith("/") ? path.substring(0, path.length() - 1) : path;
        if (!p.startsWith(ROOT + "/")) return true;
        String rel = p.substring(ROOT.length() + 1);
        return rel.equals("Android") || rel.startsWith("Android/data") || rel.startsWith("Android/obb")
                || rel.equals("DCIM") || rel.equals("Download") || rel.equals("Pictures") || rel.equals("Documents")
                || rel.equals("Music") || rel.equals("Movies");
    }

    static boolean isMedia(String name) {
        String e = ext(name);
        return Arrays_contains(new String[]{"jpg", "jpeg", "png", "gif", "webp", "heic", "heif", "dng", "mp4", "mkv", "webm", "3gp", "mov", "m4v",
                "mp3", "wav", "flac", "aac", "ogg", "m4a", "opus", "amr"}, e);
    }

    private static boolean Arrays_contains(String[] a, String v) {
        for (String s : a) if (s.equals(v)) return true;
        return false;
    }

    private static String ext(String name) {
        int dot = name.lastIndexOf('.');
        return dot < 0 ? "" : name.substring(dot + 1).toLowerCase(Locale.ROOT);
    }

    private static JSONArray actions(String path, boolean isDir, String name) {
        JSONArray a = new JSONArray();
        if (!isDir) a.put("open");
        if (isProtected(path)) return a;
        a.put("cloud");
        if (!isDir && isMedia(name) && Build.VERSION.SDK_INT >= 30) a.put("recycle");
        a.put("delete");
        return a;
    }

    // ---- answers ----------------------------------------------------------------------------------------------------------

    private static String iso(long ms) {
        SimpleDateFormat f = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US);
        f.setTimeZone(TimeZone.getTimeZone("UTC"));
        return f.format(new Date(ms));
    }

    /** What is inside a folder, biggest first (SpaceFolder). */
    static JSONObject folder(String path) throws JSONException {
        File dir = new File(path == null || path.isEmpty() ? ROOT : path);
        JSONObject o = new JSONObject();
        String p = dir.getAbsolutePath();
        o.put("path", p).put("name", p.equals(ROOT) ? "Internal storage" : dir.getName())
                .put("parent", p.equals(ROOT) || dir.getParentFile() == null ? JSONObject.NULL : dir.getParent())
                .put("at", iso(System.currentTimeMillis())).put("protected", isProtected(p)).put("note", "");
        List<JSONObject> items = new ArrayList<>();
        long total = 0;
        File[] list = dir.listFiles();
        if (list != null) {
            for (File f : list) {
                if (isLink(f)) continue;
                boolean isDir = f.isDirectory();
                long[] s = isDir ? sizeOf(f) : new long[]{f.length(), 1, f.lastModified()};
                total += s[0];
                items.add(new JSONObject().put("name", f.getName()).put("path", f.getAbsolutePath()).put("isDir", isDir)
                        .put("bytes", s[0]).put("files", s[1]).put("modified", iso(f.lastModified()))
                        .put("note", noteFor(f)).put("protected", isProtected(f.getAbsolutePath()))
                        .put("actions", actions(f.getAbsolutePath(), isDir, f.getName())));
            }
        }
        Collections.sort(items, (a, b) -> Long.compare(b.optLong("bytes"), a.optLong("bytes")));
        JSONArray arr = new JSONArray();
        for (int i = 0; i < items.size() && i < 300; i++) arr.put(items.get(i));
        o.put("bytes", total).put("items", arr);
        if (list == null) o.put("note", "Android does not let this app read this folder.");
        return o;
    }

    private static String noteFor(File f) {
        String n = f.getName();
        String rel = f.getAbsolutePath().length() > ROOT.length() ? f.getAbsolutePath().substring(ROOT.length() + 1) : "";
        if (rel.equals("Android")) return "Apps' own files. Clear them from App info.";
        if (n.equals(".thumbnails")) return "Small previews of photos. Safe to clean; they are made again.";
        if (n.equals("WhatsApp") || rel.startsWith("Android/media/com.whatsapp")) return "WhatsApp photos, videos and voice notes.";
        if (n.equals("Download")) return "Files you downloaded.";
        if (n.equals("DCIM")) return "Your camera's photos and videos.";
        return "";
    }

    /** The overview (SpaceSummary): the storage by kind of file and the RAM. */
    static JSONObject summary(Context c, JSONObject storage, JSONObject ram, String device) throws JSONException {
        long total = storage.optLong("total"), free = storage.optLong("free"), used = storage.optLong("used");
        JSONObject cats = storage.optJSONObject("cats");
        JSONArray groups = new JSONArray();
        long apps = appsBytes(c);
        String[][] kinds = {
                {"images", "Photos and pictures", "DCIM"}, {"videos", "Videos", "DCIM"}, {"audio", "Music and audio", "Music"},
                {"documents", "Documents", "Documents"}, {"apps", "APKs and archives", "Download"}, {"downloads", "Downloads", "Download"}, {"other", "Other files", ""}};
        long files = 0;
        for (String[] k : kinds) {
            JSONObject s = cats == null ? null : cats.optJSONObject(k[0]);
            if (s == null) continue;
            long b = s.optLong("bytes");
            if (!k[0].equals("downloads")) files += b;
            if (b <= 0) continue;
            groups.put(new JSONObject().put("key", k[0]).put("label", k[1]).put("bytes", b).put("count", s.optLong("count"))
                    .put("note", "").put("path", k[2].isEmpty() ? JSONObject.NULL : ROOT + "/" + k[2]).put("actions", new JSONArray().put("open")));
        }
        // Some Android versions count shared files into apps' data too: never more than what is left.
        apps = Math.min(apps, Math.max(0, used - files));
        if (apps > 0) {
            groups.put(new JSONObject().put("key", "apps-data").put("label", "Apps and their data").put("bytes", apps).put("count", 0)
                    .put("note", "Installed apps, their data and caches.").put("actions", new JSONArray()));
            groups.put(new JSONObject().put("key", "system").put("label", "Android system").put("bytes", Math.max(0, used - files - apps))
                    .put("note", "Android itself and space it keeps.").put("actions", new JSONArray()));
        } else {
            groups.put(new JSONObject().put("key", "system").put("label", "Apps and system").put("bytes", Math.max(0, used - files))
                    .put("note", "Allow \"Usage access\" to see each app's size.").put("actions", new JSONArray()));
        }
        JSONArray volumes = new JSONArray().put(new JSONObject().put("root", ROOT).put("label", "Internal storage").put("total", total).put("free", free));
        long ramTotal = ram.optLong("totalBytes"), avail = ram.optLong("availableBytes");
        JSONArray ramGroups = new JSONArray();
        if (ram.optBoolean("detailed")) {
            ramGroups.put(g("apps", "Apps and services", ram.optLong("appsBytes"), "Memory apps hold right now."));
            ramGroups.put(g("cached", "Cached", ram.optLong("cachedBytes"), "Kept to reopen things fast; freed when needed."));
            ramGroups.put(g("system", "System", ram.optLong("otherBytes") + ram.optLong("kernelBytes"), "Android, graphics and drivers."));
            ramGroups.put(g("free", "Free", ram.optLong("freeBytes"), "Not used by anything."));
        } else {
            ramGroups.put(g("used", "In use", ramTotal - avail, ""));
            ramGroups.put(g("free", "Available", avail, ""));
        }
        return new JSONObject().put("device", device).put("kind", "phone").put("at", iso(System.currentTimeMillis()))
                .put("scannedAt", scannedAt > 0 ? iso(scannedAt) : JSONObject.NULL).put("ready", scannedAt > 0).put("progress", scannedAt > 0 ? 1 : 0)
                .put("volumes", volumes).put("groups", groups)
                .put("ram", new JSONObject().put("total", ramTotal).put("available", avail).put("groups", ramGroups)
                        // Extended RAM: part of the storage Android uses as slow extra memory.
                        .put("swapTotal", ram.optLong("swapTotalBytes")).put("swapUsed", ram.optLong("swapUsedBytes"))
                        .put("low", ram.optBoolean("lowMemory")));
    }

    private static JSONObject g(String key, String label, long bytes, String note) throws JSONException {
        return new JSONObject().put("key", key).put("label", label).put("bytes", Math.max(0, bytes)).put("count", 0).put("note", note);
    }

    // ---- apps -------------------------------------------------------------------------------------------------------------

    /** Usage access lets the app read other apps' sizes and when they were used (Settings → Usage access). */
    static boolean hasUsageAccess(Context c) {
        try {
            AppOpsManager ops = (AppOpsManager) c.getSystemService(Context.APP_OPS_SERVICE);
            int mode = Build.VERSION.SDK_INT >= 29
                    ? ops.unsafeCheckOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), c.getPackageName())
                    : ops.checkOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), c.getPackageName());
            return mode == AppOpsManager.MODE_ALLOWED;
        } catch (Exception e) {
            return false;
        }
    }

    private static long appsBytes(Context c) {
        if (Build.VERSION.SDK_INT < 26 || !hasUsageAccess(c)) return 0;
        long sum = 0;
        try {
            StorageStatsManager ssm = (StorageStatsManager) c.getSystemService(Context.STORAGE_STATS_SERVICE);
            StorageStats s = ssm.queryStatsForUser(StorageManager.UUID_DEFAULT, Process.myUserHandle());
            // The installed apps plus their own data (cache included in it); shared photos and files are not in it.
            sum = s.getAppBytes() + s.getDataBytes();
        } catch (Exception ignored) {
        }
        return sum;
    }

    /** The apps the user can open, biggest first (sizes need usage access). */
    static JSONArray apps(Context c) throws JSONException {
        PackageManager pm = c.getPackageManager();
        Intent launcher = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
        List<ResolveInfo> found = pm.queryIntentActivities(launcher, 0);
        boolean sizes = Build.VERSION.SDK_INT >= 26 && hasUsageAccess(c);
        StorageStatsManager ssm = Build.VERSION.SDK_INT >= 26 ? (StorageStatsManager) c.getSystemService(Context.STORAGE_STATS_SERVICE) : null;
        Set<String> seen = new HashSet<>();
        List<JSONObject> list = new ArrayList<>();
        for (ResolveInfo r : found) {
            String pkg = r.activityInfo.packageName;
            if (!seen.add(pkg)) continue;
            long bytes = 0;
            if (sizes) {
                try {
                    StorageStats s = ssm.queryStatsForPackage(StorageManager.UUID_DEFAULT, pkg, Process.myUserHandle());
                    bytes = s.getAppBytes() + s.getDataBytes() + s.getCacheBytes();
                } catch (Exception ignored) {
                }
            }
            String version = "";
            long installed = 0;
            boolean system = false;
            try {
                PackageInfo pi = pm.getPackageInfo(pkg, 0);
                version = pi.versionName == null ? "" : pi.versionName;
                installed = pi.firstInstallTime;
                system = (pi.applicationInfo.flags & ApplicationInfo.FLAG_SYSTEM) != 0 && (pi.applicationInfo.flags & ApplicationInfo.FLAG_UPDATED_SYSTEM_APP) == 0;
            } catch (Exception ignored) {
            }
            JSONArray actions = new JSONArray();
            if (!system && !pkg.equals(c.getPackageName())) actions.put("uninstall");
            actions.put("app-info");
            list.add(new JSONObject().put("id", pkg).put("name", String.valueOf(r.loadLabel(pm))).put("publisher", system ? "Comes with the phone" : "")
                    // Apps that came with the phone report a made-up install time near 1970: no date then.
                    .put("bytes", bytes).put("version", version).put("installed", installed > 946684800000L ? iso(installed) : JSONObject.NULL).put("actions", actions));
        }
        Collections.sort(list, (a, b) -> b.optLong("bytes") != a.optLong("bytes") ? Long.compare(b.optLong("bytes"), a.optLong("bytes"))
                : a.optString("name").compareToIgnoreCase(b.optString("name")));
        JSONArray out = new JSONArray();
        for (JSONObject o : list) out.put(o);
        return out;
    }

    /**
     * Apps used in the last hour, most recent first (SpaceRamApp). Android tells no app how much RAM another app
     * holds, so bytes are 0 and the action is Android's App info (Force stop).
     */
    static JSONArray ramApps(Context c) throws JSONException {
        JSONArray out = new JSONArray();
        if (Build.VERSION.SDK_INT < 22 || !hasUsageAccess(c)) return out;
        UsageStatsManager usm = (UsageStatsManager) c.getSystemService(Context.USAGE_STATS_SERVICE);
        long now = System.currentTimeMillis();
        List<UsageStats> stats = usm.queryUsageStats(UsageStatsManager.INTERVAL_DAILY, now - 3600_000L, now);
        if (stats == null) return out;
        Collections.sort(stats, (a, b) -> Long.compare(b.getLastTimeUsed(), a.getLastTimeUsed()));
        PackageManager pm = c.getPackageManager();
        Set<String> seen = new HashSet<>();
        for (UsageStats s : stats) {
            if (s.getLastTimeUsed() < now - 3600_000L || !seen.add(s.getPackageName())) continue;
            if (pm.getLaunchIntentForPackage(s.getPackageName()) == null) continue;
            String label;
            try {
                label = String.valueOf(pm.getApplicationLabel(pm.getApplicationInfo(s.getPackageName(), 0)));
            } catch (Exception e) {
                continue;
            }
            long min = (now - s.getLastTimeUsed()) / 60000;
            out.put(new JSONObject().put("id", s.getPackageName()).put("name", s.getPackageName())
                    .put("title", label + (min < 1 ? " · used just now" : " · used " + min + " min ago"))
                    .put("bytes", 0).put("processes", 1).put("actions", new JSONArray().put("app-info")));
            if (out.length() >= 25) break;
        }
        return out;
    }

    // ---- clean-ups and big files ------------------------------------------------------------------------------------------

    static long dirBytes(File d) {
        return d.exists() ? walk(d, null, null, 0)[0] : 0;
    }

    static List<File> apkFiles() {
        List<File> out = new ArrayList<>();
        File[] list = new File(ROOT, "Download").listFiles();
        if (list != null) for (File f : list) if (f.isFile() && ext(f.getName()).matches("apk|apks|xapk|apkm")) out.add(f);
        return out;
    }

    static JSONArray cleanups(Context c) throws JSONException {
        JSONArray a = new JSONArray();
        long cache = dirBytes(c.getCacheDir()) + (c.getExternalCacheDir() != null ? dirBytes(c.getExternalCacheDir()) : 0);
        a.put(cleanup("app-cache", "This app's cache", "Previews and PDF pages Cloud-SSD keeps; made again when needed.", cache, 0, true));
        long apk = 0;
        List<File> apks = apkFiles();
        for (File f : apks) apk += f.length();
        a.put(cleanup("apks", "Install files (APKs) in Download", "Apps installed from these keep working.", apk, apks.size(), true));
        File thumbs = new File(ROOT, "DCIM/.thumbnails");
        a.put(cleanup("thumbnails", "Photo previews", "Small copies of photos the gallery makes again.", dirBytes(thumbs), 0, true));
        long backed = 0;
        List<Object[]> done = CameraBackup.backedUp(c, 30L * 24 * 3600_000L);
        for (Object[] o : done) backed += (Long) o[2];
        a.put(cleanup("backed-up", "Photos already in the cloud", "Photos and videos older than 30 days that camera backup has copied. They stay in \"Phone Backup\" on the cloud drive.",
                backed, done.size(), Build.VERSION.SDK_INT >= 30));
        a.put(cleanup("system", "Android's own clean-up", "Opens Android's Free up space screen (other apps' caches, unused apps).", 0, 0, true));
        return a;
    }

    private static JSONObject cleanup(String id, String label, String desc, long bytes, long count, boolean safe) throws JSONException {
        return new JSONObject().put("id", id).put("label", label).put("description", desc).put("bytes", bytes).put("count", count).put("safe", safe);
    }

    static JSONArray large() throws JSONException {
        List<Object[]> copy;
        synchronized (big) {
            copy = new ArrayList<>(big);
        }
        Collections.sort(copy, (a, b) -> Long.compare((Long) b[1], (Long) a[1]));
        JSONArray out = new JSONArray();
        for (int i = 0; i < copy.size() && i < 60; i++) {
            Object[] f = copy.get(i);
            String path = (String) f[0];
            if (!new File(path).exists()) continue;
            String name = path.substring(path.lastIndexOf('/') + 1);
            out.put(new JSONObject().put("name", name).put("path", path).put("isDir", false).put("bytes", f[1]).put("files", 1)
                    .put("modified", iso((Long) f[2])).put("note", "").put("protected", isProtected(path)).put("actions", actions(path, false, name)));
        }
        return out;
    }

    /** Everything the PC shows about this phone: overview, folders (the top ones and those it asked for), apps, RAM, clean-ups. */
    static JSONObject report(Context c, JSONObject storage, JSONObject ram, String device, Set<String> folders, boolean allowControl) throws JSONException {
        JSONObject o = new JSONObject();
        o.put("allowControl", allowControl);
        o.put("summary", summary(c, storage, ram, device));
        o.put("root", ROOT);
        JSONObject fs = new JSONObject();
        fs.put(ROOT, folder(ROOT));
        JSONArray top = fs.getJSONObject(ROOT).getJSONArray("items");
        for (int i = 0; i < top.length() && i < 12; i++) {
            JSONObject it = top.getJSONObject(i);
            if (it.optBoolean("isDir")) fs.put(it.getString("path"), folder(it.getString("path")));
        }
        for (String f : folders) if (f.startsWith(ROOT) && !fs.has(f) && new File(f).isDirectory()) fs.put(f, folder(f));
        o.put("folders", fs);
        o.put("apps", apps(c));
        o.put("ramApps", ramApps(c));
        o.put("cleanups", cleanups(c));
        o.put("large", large());
        return o;
    }
}
