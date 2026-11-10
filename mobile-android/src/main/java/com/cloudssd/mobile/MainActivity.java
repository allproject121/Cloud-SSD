package com.cloudssd.mobile;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.ActivityManager;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.usage.StorageStatsManager;
import android.content.ContentUris;
import android.content.ContentValues;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.media.ThumbnailUtils;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Debug;
import android.os.Environment;
import android.os.StatFs;
import android.os.StrictMode;
import android.os.storage.StorageManager;
import android.provider.MediaStore;
import android.provider.OpenableColumns;
import android.provider.Settings;
import android.util.Log;
import android.util.LruCache;
import android.util.Size;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.webkit.ConsoleMessage;
import android.webkit.JavascriptInterface;
import android.webkit.URLUtil;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.FileReader;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.NetworkInterface;
import java.net.Socket;
import java.net.URL;
import java.net.URLConnection;
import java.net.URLEncoder;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.Enumeration;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Cloud-SSD phone app: a WebView over the bundled page (assets/index.html) plus the native pieces a page cannot do:
 * finding the PC on the Wi-Fi, reading phone storage and memory, uploading/downloading with live progress, and
 * thumbnails. The page calls {@link AndroidBridge}; results that take time come back through window.onNative(event, data).
 */
public class MainActivity extends Activity {
    private static final String TAG = "CloudSSD";
    private static final int REQ_WEB_CHOOSER = 1001;
    private static final int REQ_PICK_UPLOAD = 2001;
    private static final int REQ_PERMS = 101;
    private static final String NOTIF_CHANNEL_ID = "cloudssd_transfers";
    private static final String THUMB_HOST = "cloudssd.thumb";
    private static final String PREFS = "cloudssd";
    private static final int MAX_LIST_ITEMS = 3000;

    private WebView webView;
    private ValueCallback<Uri[]> webChooserCallback;
    private volatile String serverBaseUrl = "";
    private volatile String pairingToken = "";
    /** A cloudssd://pair link that arrived before the page could take it (JSON, or ""). */
    private volatile String pendingPairLink = "";

    private final AtomicInteger transferIds = new AtomicInteger(0);
    private final Map<Integer, AtomicBoolean> cancelFlags = new ConcurrentHashMap<>();
    // One upload and one download at a time: a phone's Wi-Fi is the bottleneck, and the order stays what the user chose.
    private final ExecutorService uploadQueue = Executors.newSingleThreadExecutor();
    private final ExecutorService downloadQueue = Executors.newSingleThreadExecutor();
    private final ExecutorService worker = Executors.newFixedThreadPool(3);
    private final AtomicBoolean discovering = new AtomicBoolean(false);
    private final LruCache<String, byte[]> thumbCache = new LruCache<String, byte[]>(16 * 1024 * 1024) {
        @Override
        protected int sizeOf(String key, byte[] value) {
            return value.length;
        }
    };

    // ------------------------------------------------------------------ small helpers

    private static String formatBytes(long bytes) {
        if (bytes <= 0) return "0 B";
        final String[] units = {"B", "KB", "MB", "GB", "TB"};
        int g = Math.min(units.length - 1, (int) (Math.log10(bytes) / Math.log10(1024)));
        return String.format(Locale.US, "%.1f %s", bytes / Math.pow(1024, g), units[g]);
    }

    private static String extOf(String name) {
        int dot = name == null ? -1 : name.lastIndexOf('.');
        return dot < 0 ? "" : name.substring(dot + 1).toLowerCase(Locale.US);
    }

    private static final List<String> IMAGE_EXT = Arrays.asList("jpg", "jpeg", "png", "gif", "webp", "bmp", "heic", "heif", "dng", "svg");
    private static final List<String> VIDEO_EXT = Arrays.asList("mp4", "mkv", "webm", "avi", "mov", "m4v", "3gp", "ts", "flv", "wmv");
    private static final List<String> AUDIO_EXT = Arrays.asList("mp3", "wav", "flac", "aac", "ogg", "m4a", "opus", "amr", "wma", "mid");
    // Real documents only: .xml/.json/.html here would fill "Documents" with files apps write for themselves.
    private static final List<String> DOC_EXT = Arrays.asList("pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "csv", "rtf", "odt", "ods", "odp", "epub");
    private static final List<String> APP_EXT = Arrays.asList("apk", "apks", "xapk", "apkm", "zip", "rar", "7z", "tar", "gz", "iso");

    /** images | videos | audio | documents | apps | other — the same buckets the phone's own Files app uses. */
    private static String kindOf(String name) {
        String e = extOf(name);
        if (IMAGE_EXT.contains(e)) return "images";
        if (VIDEO_EXT.contains(e)) return "videos";
        if (AUDIO_EXT.contains(e)) return "audio";
        if (DOC_EXT.contains(e)) return "documents";
        if (APP_EXT.contains(e)) return "apps";
        return "other";
    }

    private static String mimeOf(String name) {
        String mime = URLConnection.guessContentTypeFromName(name);
        if (mime != null) return mime;
        switch (extOf(name)) {
            case "mkv": return "video/x-matroska";
            case "apk": return "application/vnd.android.package-archive";
            case "flac": return "audio/flac";
            case "opus": return "audio/ogg";
            case "heic": return "image/heic";
            case "7z": return "application/x-7z-compressed";
            case "rar": return "application/vnd.rar";
            default: return "application/octet-stream";
        }
    }

    /** Sends a result to the page: window.onNative(event, payload). The payload is JSON, which is also valid JavaScript. */
    private void emit(final String event, final Object payload) {
        final String js = "window.onNative&&window.onNative('" + event + "'," + payload + ");";
        runOnUiThread(() -> {
            if (webView != null) webView.evaluateJavascript(js, null);
        });
    }

    private void toast(final String text) {
        runOnUiThread(() -> Toast.makeText(MainActivity.this, text, Toast.LENGTH_SHORT).show());
    }

    private boolean hasPermission(String permission) {
        return Build.VERSION.SDK_INT < 23 || checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED;
    }

    /** "all" = every file (All files access), "media" = photos/videos/audio only, "none" = nothing yet. */
    private String storageAccess() {
        if (Build.VERSION.SDK_INT >= 30) {
            if (Environment.isExternalStorageManager()) return "all";
            if (Build.VERSION.SDK_INT >= 33) {
                boolean any = hasPermission("android.permission.READ_MEDIA_IMAGES") || hasPermission("android.permission.READ_MEDIA_VIDEO")
                        || hasPermission("android.permission.READ_MEDIA_AUDIO");
                return any ? "media" : "none";
            }
            return hasPermission("android.permission.READ_EXTERNAL_STORAGE") ? "media" : "none";
        }
        return hasPermission("android.permission.READ_EXTERNAL_STORAGE") ? "all" : "none";
    }

    private static String phoneWifiIp() {
        try {
            for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!ni.isUp() || ni.isLoopback() || !isLanInterface(ni.getName())) continue;
                for (InetAddress a : Collections.list(ni.getInetAddresses())) {
                    if (a instanceof Inet4Address && a.isSiteLocalAddress()) return a.getHostAddress();
                }
            }
        } catch (Exception ignored) { }
        return "";
    }

    /** Wi-Fi, hotspot, USB tethering and Ethernet — never the mobile-data interface (its "local" range is the carrier's). */
    private static boolean isLanInterface(String name) {
        String n = name == null ? "" : name.toLowerCase(Locale.US);
        return n.startsWith("wlan") || n.startsWith("ap") || n.startsWith("swlan") || n.startsWith("softap")
                || n.startsWith("eth") || n.startsWith("usb") || n.startsWith("rndis") || n.startsWith("wifi");
    }

    // ------------------------------------------------------------------ the page's native interface

    public class AndroidBridge {
        @JavascriptInterface
        public String getAppInfo() {
            JSONObject o = new JSONObject();
            try {
                o.put("version", getPackageManager().getPackageInfo(getPackageName(), 0).versionName);
                o.put("model", Build.MODEL);
                o.put("manufacturer", Build.MANUFACTURER);
                o.put("android", Build.VERSION.RELEASE);
                o.put("sdk", Build.VERSION.SDK_INT);
                o.put("phoneIp", phoneWifiIp());
                o.put("storageAccess", storageAccess());
            } catch (Exception ignored) { }
            return o.toString();
        }

        /** The page tells us which PC it is paired with, so uploads and downloads go to the same place. */
        @JavascriptInterface
        public void setServerBase(String url, String token) {
            String clean = url == null ? "" : url.trim();
            while (clean.endsWith("/")) clean = clean.substring(0, clean.length() - 1);
            serverBaseUrl = clean;
            pairingToken = token == null ? "" : token.trim();
            getSharedPreferences(PREFS, MODE_PRIVATE).edit().putString("server", serverBaseUrl).putString("token", pairingToken).apply();
        }

        /** A cloudssd://pair link the app was opened with (JSON {host, token}), handed over once. */
        @JavascriptInterface
        public String consumePairLink() {
            String link = pendingPairLink;
            pendingPairLink = "";
            return link;
        }

        /** Looks for Cloud-SSD PCs on this Wi-Fi/hotspot; results arrive as 'discovery' events. */
        @JavascriptInterface
        public void discoverPcs(final int port) {
            if (!discovering.compareAndSet(false, true)) return;
            worker.execute(() -> {
                try {
                    runDiscovery(port > 0 ? port : 8090);
                } finally {
                    discovering.set(false);
                }
            });
        }

        @JavascriptInterface
        public String getPhoneStats() {
            JSONObject obj = new JSONObject();
            try {
                long[] st = storageTotals();
                obj.put("storageTotal", st[0]);
                obj.put("storageFree", st[1]);
                obj.put("storageUsed", st[0] - st[1]);

                ActivityManager am = (ActivityManager) getSystemService(Context.ACTIVITY_SERVICE);
                ActivityManager.MemoryInfo mi = new ActivityManager.MemoryInfo();
                if (am != null) {
                    am.getMemoryInfo(mi);
                    obj.put("ramTotal", mi.totalMem);
                    obj.put("ramFree", mi.availMem);
                    obj.put("ramUsed", mi.totalMem - mi.availMem);
                    obj.put("lowMemory", mi.lowMemory);
                }
                obj.put("model", Build.MODEL);
                obj.put("manufacturer", Build.MANUFACTURER);
                obj.put("androidVersion", Build.VERSION.RELEASE);
                obj.put("storageAccess", storageAccess());
            } catch (Exception ignored) { }
            return obj.toString();
        }

        /**
         * Where the phone's memory is right now, straight from the kernel (/proc/meminfo). Android does not let one app
         * read another app's memory, so this is by kind (apps, cached files, kernel, free), plus this app's own share.
         */
        @JavascriptInterface
        public String getPhoneRamBreakdown() {
            JSONObject obj = new JSONObject();
            try {
                Map<String, Long> m = new HashMap<>();
                try (BufferedReader br = new BufferedReader(new FileReader("/proc/meminfo"))) {
                    String line;
                    while ((line = br.readLine()) != null) {
                        int colon = line.indexOf(':');
                        if (colon <= 0) continue;
                        String[] v = line.substring(colon + 1).trim().split("\\s+");
                        try {
                            m.put(line.substring(0, colon).trim(), Long.parseLong(v[0]) * 1024L);
                        } catch (NumberFormatException ignored) { }
                    }
                } catch (Exception e) {
                    obj.put("meminfoError", String.valueOf(e.getMessage()));
                }

                ActivityManager am = (ActivityManager) getSystemService(Context.ACTIVITY_SERVICE);
                ActivityManager.MemoryInfo mi = new ActivityManager.MemoryInfo();
                if (am != null) am.getMemoryInfo(mi);

                long total = m.containsKey("MemTotal") ? m.get("MemTotal") : mi.totalMem;
                long available = m.containsKey("MemAvailable") ? m.get("MemAvailable") : mi.availMem;
                obj.put("totalBytes", total);
                obj.put("availableBytes", available);
                obj.put("usedBytes", Math.max(0, total - available));
                obj.put("lowMemory", mi.lowMemory);
                obj.put("detailed", m.containsKey("MemTotal"));

                if (m.containsKey("MemTotal")) {
                    long free = get(m, "MemFree");
                    long apps = get(m, "AnonPages");
                    long cached = get(m, "Cached") + get(m, "Buffers") + get(m, "SwapCached");
                    long kernel = get(m, "Slab") + get(m, "KernelStack") + get(m, "PageTables");
                    long other = Math.max(0, total - free - apps - cached - kernel);
                    obj.put("freeBytes", free);
                    obj.put("appsBytes", apps);
                    obj.put("cachedBytes", cached);
                    obj.put("kernelBytes", kernel);
                    obj.put("otherBytes", other);
                    long swapTotal = get(m, "SwapTotal");
                    obj.put("swapTotalBytes", swapTotal);
                    obj.put("swapUsedBytes", Math.max(0, swapTotal - get(m, "SwapFree")));
                }

                // Processes Android lets this app see (on modern Android: only itself), with their real memory (PSS).
                JSONArray procs = new JSONArray();
                if (am != null) {
                    List<ActivityManager.RunningAppProcessInfo> list = am.getRunningAppProcesses();
                    if (list != null && !list.isEmpty()) {
                        int n = Math.min(list.size(), 30);
                        int[] pids = new int[n];
                        for (int i = 0; i < n; i++) pids[i] = list.get(i).pid;
                        Debug.MemoryInfo[] infos = am.getProcessMemoryInfo(pids);
                        PackageManager pm = getPackageManager();
                        for (int i = 0; i < n; i++) {
                            long pss = infos != null && i < infos.length ? infos[i].getTotalPss() * 1024L : 0;
                            if (pss <= 0) continue;
                            String pkg = list.get(i).processName;
                            String label = pkg;
                            try {
                                label = String.valueOf(pm.getApplicationLabel(pm.getApplicationInfo(pkg.split(":")[0], 0)));
                            } catch (Exception ignored) { }
                            JSONObject p = new JSONObject();
                            p.put("name", label);
                            p.put("pkg", pkg);
                            p.put("bytes", pss);
                            procs.put(p);
                        }
                    }
                }
                obj.put("processes", procs);
            } catch (Exception ex) {
                try { obj.put("error", String.valueOf(ex.getMessage())); } catch (Exception ignored) { }
            }
            return obj.toString();
        }

        @JavascriptInterface
        public String getStorageAccess() {
            return storageAccess();
        }

        /** Photos/videos/audio + notifications (the normal permission dialog). */
        @JavascriptInterface
        public void requestAllPermissions() {
            runOnUiThread(() -> {
                if (Build.VERSION.SDK_INT >= 33) {
                    requestPermissions(new String[]{
                            "android.permission.READ_MEDIA_IMAGES", "android.permission.READ_MEDIA_VIDEO",
                            "android.permission.READ_MEDIA_AUDIO", "android.permission.POST_NOTIFICATIONS"}, REQ_PERMS);
                } else if (Build.VERSION.SDK_INT >= 23) {
                    requestPermissions(new String[]{
                            "android.permission.READ_EXTERNAL_STORAGE", "android.permission.WRITE_EXTERNAL_STORAGE"}, REQ_PERMS);
                }
            });
        }

        /** Opens Android's "All files access" switch for this app: needed to show documents, APKs and every folder. */
        @JavascriptInterface
        public void requestAllFilesAccess() {
            runOnUiThread(() -> {
                if (Build.VERSION.SDK_INT < 30) {
                    requestAllPermissions();
                    return;
                }
                try {
                    startActivity(new Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION, Uri.parse("package:" + getPackageName())));
                } catch (Exception e) {
                    try {
                        startActivity(new Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION));
                    } catch (Exception e2) {
                        toast("Open Settings > Apps > Cloud-SSD > Permissions > Files");
                    }
                }
            });
        }

        @JavascriptInterface
        public boolean hasStoragePermission() {
            return !"none".equals(storageAccess());
        }

        /** Sizes of everything on the phone by kind; the result arrives as a 'storage' event. */
        @JavascriptInterface
        public void scanStorage() {
            worker.execute(() -> emit("storage", buildStorageReport()));
        }

        /** Folder listing; arrives as a 'list' event carrying reqId. */
        @JavascriptInterface
        public void listDir(final int reqId, final String path) {
            worker.execute(() -> emit("list", withReq(reqId, listDirectory(path))));
        }

        /** downloads | images | videos | audio | documents | apps; arrives as a 'list' event carrying reqId. */
        @JavascriptInterface
        public void listCategory(final int reqId, final String category) {
            worker.execute(() -> emit("list", withReq(reqId, listCategoryFiles(category))));
        }

        @JavascriptInterface
        public void searchPhone(final int reqId, final String query) {
            worker.execute(() -> emit("list", withReq(reqId, searchFiles(query))));
        }

        @JavascriptInterface
        public void uploadFileByPath(final String localFilePath) {
            if (localFilePath == null || localFilePath.trim().isEmpty()) return;
            final File file = new File(localFilePath);
            if (!file.isFile()) {
                toast("File not found: " + file.getName());
                return;
            }
            enqueueUpload(file.getName(), file.length(), () -> new FileInputStream(file));
        }

        @JavascriptInterface
        public void pickAndUploadFiles(final String type) {
            runOnUiThread(() -> {
                Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
                intent.addCategory(Intent.CATEGORY_OPENABLE);
                if ("photos".equalsIgnoreCase(type) || "images".equalsIgnoreCase(type)) {
                    intent.setType("*/*");
                    intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[]{"image/*", "video/*"});
                } else {
                    intent.setType("*/*");
                }
                intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
                try {
                    startActivityForResult(Intent.createChooser(intent, "Select files to send to PC"), REQ_PICK_UPLOAD);
                } catch (Exception e) {
                    toast("Cannot open file picker: " + e.getMessage());
                }
            });
        }

        @JavascriptInterface
        public void downloadFile(final String downloadUrl, final String rawFileName) {
            enqueueDownload(downloadUrl, rawFileName);
        }

        @JavascriptInterface
        public void cancelTransfer(int id) {
            AtomicBoolean flag = cancelFlags.get(id);
            if (flag != null) flag.set(true);
        }

        /** Opens a PC file (stream URL) in another app, e.g. a video player. */
        @JavascriptInterface
        public void openWith(final String fileUrl, final String mimeType, final String fileName) {
            runOnUiThread(() -> {
                try {
                    Intent intent = new Intent(Intent.ACTION_VIEW);
                    String type = (mimeType != null && !mimeType.isEmpty() && !mimeType.equals("application/octet-stream")) ? mimeType : "*/*";
                    intent.setDataAndType(Uri.parse(fileUrl), type);
                    intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    startActivity(Intent.createChooser(intent, "Open " + (fileName != null ? fileName : "file") + " with"));
                } catch (Exception e) {
                    toast("Cannot open: " + e.getMessage());
                }
            });
        }

        /** Opens a file that is on the phone in another app. */
        @JavascriptInterface
        public void openLocalFile(final String path) {
            runOnUiThread(() -> {
                try {
                    File f = new File(path);
                    if (!f.exists()) {
                        toast("File not found: " + f.getName());
                        return;
                    }
                    Uri uri = contentUriFor(f);
                    if (uri == null) uri = Uri.fromFile(f);
                    Intent intent = new Intent(Intent.ACTION_VIEW);
                    intent.setDataAndType(uri, mimeOf(f.getName()));
                    intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    startActivity(Intent.createChooser(intent, "Open " + f.getName() + " with"));
                } catch (Exception e) {
                    toast("Cannot open: " + e.getMessage());
                }
            });
        }
    }

    private static long get(Map<String, Long> m, String key) {
        Long v = m.get(key);
        return v == null ? 0 : v;
    }

    private static JSONObject withReq(int reqId, JSONObject o) {
        try { o.put("reqId", reqId); } catch (Exception ignored) { }
        return o;
    }

    // ------------------------------------------------------------------ find the PC

    /** Tries every address of this /24 on the bridge port; a PC answers /api/status with its name even before pairing. */
    private void runDiscovery(final int port) {
        final JSONArray found = new JSONArray();
        final List<String> bases = new ArrayList<>();
        final List<String> own = new ArrayList<>();
        try {
            for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!ni.isUp() || ni.isLoopback() || !isLanInterface(ni.getName())) continue;
                for (InetAddress a : Collections.list(ni.getInetAddresses())) {
                    if (!(a instanceof Inet4Address) || !a.isSiteLocalAddress()) continue;
                    String ip = a.getHostAddress();
                    own.add(ip);
                    String base = ip.substring(0, ip.lastIndexOf('.') + 1);
                    if (!bases.contains(base)) bases.add(base);
                }
            }
        } catch (Exception e) {
            Log.w(TAG, "discovery: cannot list interfaces", e);
        }

        final List<String> hosts = new ArrayList<>();
        // USB debugging with "adb reverse" makes the PC reachable as this phone's own localhost.
        hosts.add("127.0.0.1");
        for (String base : bases) {
            for (int i = 1; i <= 254; i++) {
                String ip = base + i;
                if (!own.contains(ip)) hosts.add(ip);
            }
        }

        ExecutorService pool = Executors.newFixedThreadPool(48);
        final CountDownLatch done = new CountDownLatch(hosts.size());
        for (final String host : hosts) {
            pool.execute(() -> {
                try {
                    JSONObject pc = probePc(host, port);
                    if (pc != null) {
                        synchronized (found) {
                            found.put(pc);
                            emit("discovery", discoveryJson(found, false, bases.isEmpty()));
                        }
                    }
                } finally {
                    done.countDown();
                }
            });
        }
        try { done.await(); } catch (InterruptedException ignored) { }
        pool.shutdown();
        synchronized (found) {
            emit("discovery", discoveryJson(found, true, bases.isEmpty()));
        }
    }

    private static JSONObject discoveryJson(JSONArray found, boolean done, boolean noWifi) {
        JSONObject o = new JSONObject();
        try {
            o.put("found", new JSONArray(found.toString()));
            o.put("done", done);
            o.put("noWifi", noWifi);
        } catch (Exception ignored) { }
        return o;
    }

    private static JSONObject probePc(String host, int port) {
        try (Socket s = new Socket()) {
            s.connect(new InetSocketAddress(host, port), 400);
        } catch (Exception e) {
            return null;
        }
        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL("http://" + host + ":" + port + "/api/status").openConnection();
            conn.setConnectTimeout(1500);
            conn.setReadTimeout(2500);
            if (conn.getResponseCode() != 200) return null;
            JSONObject status = new JSONObject(readAll(conn.getInputStream()));
            if (!status.has("pcName") || !status.has("port")) return null; // some other program on this port
            JSONObject pc = new JSONObject();
            pc.put("url", "http://" + host + ":" + port);
            pc.put("ip", host);
            pc.put("pcName", status.optString("pcName", "PC"));
            return pc;
        } catch (Exception e) {
            return null;
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    private static String readAll(InputStream in) throws IOException {
        if (in == null) return "";
        StringBuilder sb = new StringBuilder();
        try (BufferedReader r = new BufferedReader(new InputStreamReader(in, "UTF-8"))) {
            char[] buf = new char[4096];
            int n;
            while ((n = r.read(buf)) > 0) sb.append(buf, 0, n);
        }
        return sb.toString();
    }

    // ------------------------------------------------------------------ phone storage

    /** {total, free} of the phone's internal storage, the same numbers Android's Settings shows. */
    private long[] storageTotals() {
        if (Build.VERSION.SDK_INT >= 26) {
            try {
                StorageStatsManager ssm = (StorageStatsManager) getSystemService(Context.STORAGE_STATS_SERVICE);
                if (ssm != null) {
                    return new long[]{ssm.getTotalBytes(StorageManager.UUID_DEFAULT), ssm.getFreeBytes(StorageManager.UUID_DEFAULT)};
                }
            } catch (Exception e) {
                Log.w(TAG, "StorageStatsManager failed, falling back to StatFs", e);
            }
        }
        StatFs stat = new StatFs(Environment.getDataDirectory().getPath());
        return new long[]{stat.getBlockCountLong() * stat.getBlockSizeLong(), stat.getAvailableBlocksLong() * stat.getBlockSizeLong()};
    }

    /** One pass over the phone's file index: count and size per kind, plus what is left for apps and the system. */
    private JSONObject buildStorageReport() {
        JSONObject o = new JSONObject();
        try {
            long[] st = storageTotals();
            String access = storageAccess();
            o.put("total", st[0]);
            o.put("free", st[1]);
            o.put("used", st[0] - st[1]);
            o.put("access", access);

            String[] kinds = {"images", "videos", "audio", "documents", "apps", "other"};
            Map<String, long[]> sums = new HashMap<>();
            for (String k : kinds) sums.put(k, new long[2]);
            long[] downloads = new long[2];
            long filesBytes = 0;

            if (!"none".equals(access)) {
                String downloadDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS).getAbsolutePath() + "/";
                String[] proj = {MediaStore.MediaColumns.DATA, MediaStore.MediaColumns.SIZE, MediaStore.Files.FileColumns.MEDIA_TYPE, MediaStore.MediaColumns.MIME_TYPE};
                try (Cursor c = getContentResolver().query(MediaStore.Files.getContentUri("external"), proj, null, null, null)) {
                    if (c != null) {
                        while (c.moveToNext()) {
                            String path = c.getString(0);
                            long size = c.getLong(1);
                            int mediaType = c.getInt(2);
                            if (path == null || size <= 0) continue; // folders have no size
                            String kind;
                            if (mediaType == MediaStore.Files.FileColumns.MEDIA_TYPE_IMAGE) kind = "images";
                            else if (mediaType == MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO) kind = "videos";
                            else if (mediaType == MediaStore.Files.FileColumns.MEDIA_TYPE_AUDIO) kind = "audio";
                            else {
                                // Not in the gallery (e.g. inside a .nomedia folder): count pictures and clips there as
                                // "other", so each number equals what its category list shows.
                                String k = kindOf(path);
                                kind = "documents".equals(k) || "apps".equals(k) ? k : "other";
                            }
                            long[] s = sums.get(kind);
                            s[0]++;
                            s[1] += size;
                            filesBytes += size;
                            if (path.startsWith(downloadDir)) {
                                downloads[0]++;
                                downloads[1] += size;
                            }
                        }
                    }
                }
                // With full access the Download folder itself is the truth (the index can miss fresh files).
                List<File> direct = downloadFiles();
                if (direct != null) {
                    downloads[0] = direct.size();
                    downloads[1] = 0;
                    for (File f : direct) downloads[1] += f.length();
                }
            }

            JSONObject cats = new JSONObject();
            for (String k : kinds) {
                cats.put(k, new JSONObject().put("count", sums.get(k)[0]).put("bytes", sums.get(k)[1]));
            }
            cats.put("downloads", new JSONObject().put("count", downloads[0]).put("bytes", downloads[1]));
            o.put("cats", cats);
            o.put("filesBytes", filesBytes);
            // Installed apps, their data and Android itself: everything used that is not one of the files above.
            o.put("appsSystemBytes", Math.max(0, (st[0] - st[1]) - filesBytes));
        } catch (Exception e) {
            Log.w(TAG, "storage scan failed", e);
            try { o.put("error", String.valueOf(e.getMessage())); } catch (Exception ignored) { }
        }
        return o;
    }

    /** Files directly in Download, newest first; null when Android does not let this app read the folder itself. */
    private List<File> downloadFiles() {
        if (!"all".equals(storageAccess())) return null;
        File[] items = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS).listFiles();
        if (items == null) return null;
        List<File> out = new ArrayList<>();
        for (File f : items) if (f.isFile() && !f.getName().startsWith(".")) out.add(f);
        Collections.sort(out, (a, b) -> Long.compare(b.lastModified(), a.lastModified()));
        return out;
    }

    private static JSONObject fileJson(File f) throws Exception {
        JSONObject fi = new JSONObject();
        fi.put("name", f.getName());
        fi.put("path", f.getAbsolutePath());
        fi.put("size", f.length());
        fi.put("modified", f.lastModified());
        return fi;
    }

    private JSONObject listDirectory(String requestedPath) {
        JSONObject result = new JSONObject();
        JSONArray folders = new JSONArray();
        JSONArray files = new JSONArray();
        try {
            File root = Environment.getExternalStorageDirectory();
            File dir = (requestedPath == null || requestedPath.trim().isEmpty()) ? root : new File(requestedPath.trim());
            result.put("path", dir.getAbsolutePath());
            result.put("root", root.getAbsolutePath());
            result.put("access", storageAccess());
            if (!dir.isDirectory()) {
                result.put("error", "Folder not found");
            } else {
                File[] items = dir.listFiles();
                if (items == null) {
                    result.put("error", "Android does not allow reading this folder");
                } else {
                    Arrays.sort(items, (a, b) -> a.getName().compareToIgnoreCase(b.getName()));
                    int n = 0;
                    for (File f : items) {
                        if (f.getName().startsWith(".")) continue;
                        if (++n > MAX_LIST_ITEMS) {
                            result.put("truncated", true);
                            break;
                        }
                        if (f.isDirectory()) {
                            JSONObject fo = new JSONObject();
                            fo.put("name", f.getName());
                            fo.put("path", f.getAbsolutePath());
                            fo.put("modified", f.lastModified());
                            String[] children = f.list();
                            if (children != null) fo.put("itemCount", children.length);
                            folders.put(fo);
                        } else {
                            files.put(fileJson(f));
                        }
                    }
                }
            }
        } catch (Exception ex) {
            try { result.put("error", String.valueOf(ex.getMessage())); } catch (Exception ignored) { }
        }
        try {
            result.put("folders", folders);
            result.put("files", files);
        } catch (Exception ignored) { }
        return result;
    }

    private static String likeAny(List<String> exts) {
        StringBuilder sb = new StringBuilder();
        for (String e : exts) {
            if (sb.length() > 0) sb.append(" OR ");
            sb.append(MediaStore.MediaColumns.DATA).append(" LIKE '%.").append(e).append("'");
        }
        return "(" + sb + ")";
    }

    private JSONObject listCategoryFiles(String category) {
        JSONObject result = new JSONObject();
        JSONArray files = new JSONArray();
        String cat = category == null ? "" : category.toLowerCase(Locale.US);
        try {
            result.put("category", cat);
            result.put("access", storageAccess());
            String downloadDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS).getAbsolutePath();
            String selection;
            String[] args = null;
            switch (cat) {
                case "images": selection = MediaStore.Files.FileColumns.MEDIA_TYPE + "=" + MediaStore.Files.FileColumns.MEDIA_TYPE_IMAGE; break;
                case "videos": selection = MediaStore.Files.FileColumns.MEDIA_TYPE + "=" + MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO; break;
                case "audio": selection = MediaStore.Files.FileColumns.MEDIA_TYPE + "=" + MediaStore.Files.FileColumns.MEDIA_TYPE_AUDIO; break;
                case "documents": case "docs": selection = likeAny(DOC_EXT); break;
                case "apps": selection = likeAny(APP_EXT); break;
                case "downloads": selection = MediaStore.MediaColumns.DATA + " LIKE ?"; args = new String[]{downloadDir + "/%"}; break;
                default: selection = null;
            }
            // The index can lag behind for a folder that was just written to; with full access read Download directly.
            List<File> direct = "downloads".equals(cat) ? downloadFiles() : null;
            if (direct != null) {
                for (File f : direct) files.put(fileJson(f));
            } else if (queryFiles(selection, args, files, 1000)) {
                result.put("truncated", true);
            }
        } catch (Exception ex) {
            try { result.put("error", String.valueOf(ex.getMessage())); } catch (Exception ignored) { }
        }
        try {
            result.put("folders", new JSONArray());
            result.put("files", files);
        } catch (Exception ignored) { }
        return result;
    }

    private JSONObject searchFiles(String query) {
        JSONObject result = new JSONObject();
        JSONArray files = new JSONArray();
        try {
            result.put("query", query);
            String q = query == null ? "" : query.trim();
            if (!q.isEmpty()) {
                String like = "%" + q.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_") + "%";
                if (queryFiles(MediaStore.MediaColumns.DISPLAY_NAME + " LIKE ? ESCAPE '\\'", new String[]{like}, files, 200)) result.put("truncated", true);
            }
        } catch (Exception ex) {
            try { result.put("error", String.valueOf(ex.getMessage())); } catch (Exception ignored) { }
        }
        try {
            result.put("folders", new JSONArray());
            result.put("files", files);
        } catch (Exception ignored) { }
        return result;
    }

    /** Newest first from the phone's file index; true when there were more than max. */
    private boolean queryFiles(String selection, String[] args, JSONArray out, int max) throws Exception {
        String[] proj = {MediaStore.MediaColumns.DATA, MediaStore.MediaColumns.SIZE, MediaStore.MediaColumns.DATE_MODIFIED};
        try (Cursor c = getContentResolver().query(MediaStore.Files.getContentUri("external"), proj, selection, args,
                MediaStore.MediaColumns.DATE_MODIFIED + " DESC")) {
            if (c == null) return false;
            while (c.moveToNext()) {
                String path = c.getString(0);
                long size = c.getLong(1);
                if (path == null || size <= 0) continue;
                if (out.length() >= max) return true;
                JSONObject fi = new JSONObject();
                fi.put("name", path.substring(path.lastIndexOf('/') + 1));
                fi.put("path", path);
                fi.put("size", size);
                fi.put("modified", c.getLong(2) * 1000L);
                out.put(fi);
            }
        }
        return false;
    }

    /** content:// address of a phone file, which other apps are allowed to open (a raw file path often is not). */
    private Uri contentUriFor(File f) {
        Uri base = MediaStore.Files.getContentUri("external");
        try (Cursor c = getContentResolver().query(base, new String[]{MediaStore.MediaColumns._ID},
                MediaStore.MediaColumns.DATA + "=?", new String[]{f.getAbsolutePath()}, null)) {
            if (c != null && c.moveToFirst()) return ContentUris.withAppendedId(base, c.getLong(0));
        } catch (Exception ignored) { }
        return null;
    }

    // ------------------------------------------------------------------ thumbnails

    private WebResourceResponse thumbResponse(String path) {
        byte[] data = path == null ? null : thumbCache.get(path);
        if (data == null && path != null) {
            data = makeThumb(new File(path));
            if (data != null) thumbCache.put(path, data);
        }
        Map<String, String> headers = new HashMap<>();
        headers.put("Access-Control-Allow-Origin", "*");
        if (data == null) {
            return new WebResourceResponse("image/jpeg", null, 404, "Not Found", headers, new ByteArrayInputStream(new byte[0]));
        }
        headers.put("Cache-Control", "max-age=86400");
        return new WebResourceResponse("image/jpeg", null, 200, "OK", headers, new ByteArrayInputStream(data));
    }

    private byte[] makeThumb(File f) {
        if (!f.isFile()) return null;
        Bitmap bmp = null;
        try {
            String kind = kindOf(f.getName());
            if (Build.VERSION.SDK_INT >= 29) {
                Size size = new Size(320, 320);
                if ("images".equals(kind)) bmp = ThumbnailUtils.createImageThumbnail(f, size, null);
                else if ("videos".equals(kind)) bmp = ThumbnailUtils.createVideoThumbnail(f, size, null);
                else if ("audio".equals(kind)) bmp = ThumbnailUtils.createAudioThumbnail(f, size, null);
            } else if ("images".equals(kind)) {
                BitmapFactory.Options opts = new BitmapFactory.Options();
                opts.inJustDecodeBounds = true;
                BitmapFactory.decodeFile(f.getPath(), opts);
                int sample = 1;
                while (opts.outWidth / sample > 640 || opts.outHeight / sample > 640) sample *= 2;
                opts = new BitmapFactory.Options();
                opts.inSampleSize = sample;
                bmp = BitmapFactory.decodeFile(f.getPath(), opts);
            } else if ("videos".equals(kind)) {
                bmp = ThumbnailUtils.createVideoThumbnail(f.getPath(), MediaStore.Images.Thumbnails.MINI_KIND);
            }
            if (bmp == null) return null;
            ByteArrayOutputStream out = new ByteArrayOutputStream(24 * 1024);
            bmp.compress(Bitmap.CompressFormat.JPEG, 78, out);
            return out.toByteArray();
        } catch (Exception | OutOfMemoryError e) {
            return null; // no embedded art, broken file, …: the page shows its icon instead
        } finally {
            if (bmp != null) bmp.recycle();
        }
    }

    // ------------------------------------------------------------------ transfers (live progress for the page)

    private interface Source {
        InputStream open() throws IOException;
    }

    private void emitTransfer(int id, String type, String name, String state, long bytes, long total, double bytesPerSec, String error) {
        JSONObject o = new JSONObject();
        try {
            o.put("id", id);
            o.put("type", type);
            o.put("name", name);
            o.put("state", state); // queued | active | done | failed | cancelled
            o.put("bytes", bytes);
            o.put("total", total);
            o.put("speed", Math.round(bytesPerSec));
            if (error != null) o.put("error", error);
        } catch (Exception ignored) { }
        emit("transfer", o);
    }

    private Notification.Builder notification(String title, String text, int icon) {
        Notification.Builder b = Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(this, NOTIF_CHANNEL_ID) : new Notification.Builder(this);
        return b.setSmallIcon(icon).setContentTitle(title).setContentText(text).setOnlyAlertOnce(true);
    }

    private void post(int id, Notification.Builder b) {
        NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        if (nm == null) return;
        try { nm.notify(id, b.build()); } catch (Exception ignored) { }
    }

    private String serverError(HttpURLConnection conn, int code) {
        try {
            String body = readAll(conn.getErrorStream());
            String msg = new JSONObject(body).optString("error", "");
            if (!msg.isEmpty()) return msg;
        } catch (Exception ignored) { }
        if (code == 401) return "This phone is not paired with the PC any more. Pair again.";
        return "PC answered HTTP " + code;
    }

    private void enqueueUpload(final String name, final long size, final Source source) {
        final int id = transferIds.incrementAndGet();
        final AtomicBoolean cancel = new AtomicBoolean(false);
        cancelFlags.put(id, cancel);
        emitTransfer(id, "upload", name, "queued", 0, size, 0, null);
        uploadQueue.execute(() -> runUpload(id, name, size, source, cancel));
    }

    private void runUpload(int id, String name, long size, Source source, AtomicBoolean cancel) {
        final int notifId = 5000 + id;
        Notification.Builder nb = notification("Sending " + name, "Connecting to PC…", android.R.drawable.stat_sys_upload)
                .setOngoing(true).setProgress(100, 0, size <= 0);
        HttpURLConnection conn = null;
        long sent = 0;
        try {
            if (cancel.get()) throw new IOException("cancelled");
            if (serverBaseUrl.isEmpty() || pairingToken.isEmpty()) throw new IOException("Not connected to a PC yet");
            post(notifId, nb);
            emitTransfer(id, "upload", name, "active", 0, size, 0, null);

            // URLEncoder writes a space as "+", which the PC would keep as a plus sign in the file name.
            String q = URLEncoder.encode(name, "UTF-8").replace("+", "%20");
            conn = (HttpURLConnection) new URL(serverBaseUrl + "/api/upload?filename=" + q).openConnection();
            conn.setRequestMethod("POST");
            conn.setDoOutput(true);
            conn.setConnectTimeout(10000);
            conn.setReadTimeout(120000);
            conn.setRequestProperty("Content-Type", "application/octet-stream");
            conn.setRequestProperty("X-Pairing-Token", pairingToken);
            if (size > 0) conn.setFixedLengthStreamingMode(size);
            else conn.setChunkedStreamingMode(64 * 1024);

            long lastAt = System.currentTimeMillis(), lastBytes = 0;
            double speed = 0;
            try (InputStream in = source.open(); OutputStream out = conn.getOutputStream()) {
                if (in == null) throw new IOException("Cannot read the file");
                byte[] buffer = new byte[128 * 1024];
                int n;
                while ((n = in.read(buffer)) != -1) {
                    if (cancel.get()) throw new IOException("cancelled");
                    out.write(buffer, 0, n);
                    sent += n;
                    long now = System.currentTimeMillis();
                    if (now - lastAt >= 300) {
                        speed = (sent - lastBytes) * 1000.0 / (now - lastAt);
                        lastAt = now;
                        lastBytes = sent;
                        emitTransfer(id, "upload", name, "active", sent, size, speed, null);
                        int pct = size > 0 ? (int) (sent * 100 / size) : 0;
                        nb.setProgress(100, pct, size <= 0).setContentText(formatBytes((long) speed) + "/s • " + formatBytes(sent) + (size > 0 ? " / " + formatBytes(size) : ""));
                        post(notifId, nb);
                    }
                }
                out.flush();
            }

            int code = conn.getResponseCode();
            if (code != 200) throw new IOException(serverError(conn, code));
            emitTransfer(id, "upload", name, "done", sent, sent, 0, null);
            post(notifId, notification("Sent to PC", name + " (" + formatBytes(sent) + ")", android.R.drawable.stat_sys_upload_done).setAutoCancel(true));
        } catch (Exception e) {
            boolean cancelled = cancel.get();
            String msg = cancelled ? "Cancelled" : friendly(e);
            emitTransfer(id, "upload", name, cancelled ? "cancelled" : "failed", sent, size, 0, msg);
            post(notifId, notification(cancelled ? "Upload cancelled" : "Upload failed", name + ": " + msg, android.R.drawable.stat_notify_error).setAutoCancel(true));
        } finally {
            if (conn != null) conn.disconnect();
            cancelFlags.remove(id);
        }
    }

    private static String friendly(Exception e) {
        String m = e.getMessage();
        if (e instanceof java.net.SocketTimeoutException || e instanceof java.net.ConnectException || e instanceof java.net.NoRouteToHostException) {
            return "PC not reachable. Is it on the same Wi-Fi and is Cloud-SSD running?";
        }
        return m == null || m.isEmpty() ? e.getClass().getSimpleName() : m;
    }

    private void enqueueDownload(final String downloadUrl, final String rawName) {
        final String name = (rawName == null || rawName.trim().isEmpty()) ? "download_" + System.currentTimeMillis() : rawName.trim();
        final int id = transferIds.incrementAndGet();
        final AtomicBoolean cancel = new AtomicBoolean(false);
        cancelFlags.put(id, cancel);
        emitTransfer(id, "download", name, "queued", 0, -1, 0, null);
        downloadQueue.execute(() -> runDownload(id, downloadUrl, name, cancel));
    }

    private void runDownload(int id, String downloadUrl, String name, AtomicBoolean cancel) {
        final int notifId = 5000 + id;
        Notification.Builder nb = notification("Downloading " + name, "Connecting to PC…", android.R.drawable.stat_sys_download)
                .setOngoing(true).setProgress(100, 0, true);
        HttpURLConnection conn = null;
        Uri pending = null;
        File target = null;
        long got = 0, total = -1;
        try {
            if (cancel.get()) throw new IOException("cancelled");
            post(notifId, nb);
            emitTransfer(id, "download", name, "active", 0, -1, 0, null);

            conn = (HttpURLConnection) new URL(downloadUrl).openConnection();
            conn.setRequestProperty("X-Pairing-Token", pairingToken);
            conn.setConnectTimeout(10000);
            conn.setReadTimeout(60000);
            int code = conn.getResponseCode();
            if (code != 200) throw new IOException(serverError(conn, code));
            total = conn.getContentLengthLong();
            String mime = mimeOf(name);

            OutputStream out;
            if (Build.VERSION.SDK_INT >= 29) {
                ContentValues values = new ContentValues();
                values.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
                values.put(MediaStore.MediaColumns.MIME_TYPE, mime);
                values.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS);
                values.put(MediaStore.MediaColumns.IS_PENDING, 1);
                pending = getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values);
                if (pending == null) throw new IOException("Cannot create the file in Downloads");
                out = getContentResolver().openOutputStream(pending);
            } else {
                File dir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS);
                if (!dir.exists()) dir.mkdirs();
                target = new File(dir, name);
                out = new FileOutputStream(target);
            }
            if (out == null) throw new IOException("Cannot write to Downloads");

            long lastAt = System.currentTimeMillis(), lastBytes = 0;
            try (InputStream in = conn.getInputStream(); OutputStream o = out) {
                byte[] buf = new byte[128 * 1024];
                int n;
                while ((n = in.read(buf)) != -1) {
                    if (cancel.get()) throw new IOException("cancelled");
                    o.write(buf, 0, n);
                    got += n;
                    long now = System.currentTimeMillis();
                    if (now - lastAt >= 300) {
                        double speed = (got - lastBytes) * 1000.0 / (now - lastAt);
                        lastAt = now;
                        lastBytes = got;
                        emitTransfer(id, "download", name, "active", got, total, speed, null);
                        nb.setProgress(100, total > 0 ? (int) (got * 100 / total) : 0, total <= 0)
                                .setContentText(formatBytes((long) speed) + "/s • " + formatBytes(got) + (total > 0 ? " / " + formatBytes(total) : ""));
                        post(notifId, nb);
                    }
                }
                o.flush();
            }
            if (total > 0 && got < total) throw new IOException("The connection dropped before the file was complete");

            Uri openUri;
            if (pending != null) {
                ContentValues done = new ContentValues();
                done.put(MediaStore.MediaColumns.IS_PENDING, 0);
                getContentResolver().update(pending, done, null, null);
                openUri = pending;
                pending = null;
            } else {
                Intent scan = new Intent(Intent.ACTION_MEDIA_SCANNER_SCAN_FILE);
                scan.setData(Uri.fromFile(target));
                sendBroadcast(scan);
                openUri = Uri.fromFile(target);
            }

            emitTransfer(id, "download", name, "done", got, got, 0, null);
            Notification.Builder doneNb = notification("Saved to Downloads", name + " (" + formatBytes(got) + ")", android.R.drawable.stat_sys_download_done).setAutoCancel(true);
            try {
                Intent open = new Intent(Intent.ACTION_VIEW);
                open.setDataAndType(openUri, mime);
                open.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 23 ? PendingIntent.FLAG_IMMUTABLE : 0);
                doneNb.setContentIntent(PendingIntent.getActivity(this, notifId, open, flags));
            } catch (Exception ignored) { }
            post(notifId, doneNb);
        } catch (Exception e) {
            boolean cancelled = cancel.get();
            String msg = cancelled ? "Cancelled" : friendly(e);
            // Never leave half a file behind looking like a finished download.
            if (pending != null) {
                try { getContentResolver().delete(pending, null, null); } catch (Exception ignored) { }
            }
            if (target != null && target.exists()) target.delete();
            emitTransfer(id, "download", name, cancelled ? "cancelled" : "failed", got, total, 0, msg);
            post(notifId, notification(cancelled ? "Download cancelled" : "Download failed", name + ": " + msg, android.R.drawable.stat_notify_error).setAutoCancel(true));
        } finally {
            if (conn != null) conn.disconnect();
            cancelFlags.remove(id);
        }
    }

    /** Queues files chosen in a picker or shared from another app. */
    private void uploadUris(List<Uri> uris) {
        for (final Uri uri : uris) {
            String name = "upload_" + System.currentTimeMillis();
            long size = -1;
            try (Cursor cursor = getContentResolver().query(uri, null, null, null, null)) {
                if (cursor != null && cursor.moveToFirst()) {
                    int nameIdx = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                    int sizeIdx = cursor.getColumnIndex(OpenableColumns.SIZE);
                    if (nameIdx >= 0 && cursor.getString(nameIdx) != null) name = cursor.getString(nameIdx);
                    if (sizeIdx >= 0 && !cursor.isNull(sizeIdx)) size = cursor.getLong(sizeIdx);
                }
            } catch (Exception ignored) { }
            if ("file".equals(uri.getScheme()) && uri.getPath() != null) {
                File f = new File(uri.getPath());
                name = f.getName();
                size = f.length();
            }
            enqueueUpload(name, size, () -> getContentResolver().openInputStream(uri));
        }
    }

    // ------------------------------------------------------------------ activity

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel channel = new NotificationChannel(NOTIF_CHANNEL_ID, "File Transfers", NotificationManager.IMPORTANCE_LOW);
            channel.setDescription("Shows file download, speed and transfer progress");
            channel.enableVibration(false);
            NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null) nm.createNotificationChannel(channel);
        }
    }

    /** Pairing links (cloudssd://pair?host=…&token=…) and files shared to this app from other apps. */
    private void handleIntent(Intent intent) {
        if (intent == null || intent.getAction() == null) return;
        String action = intent.getAction();
        if (Intent.ACTION_VIEW.equals(action) && intent.getData() != null && "cloudssd".equals(intent.getData().getScheme())) {
            Uri d = intent.getData();
            try {
                JSONObject o = new JSONObject();
                o.put("host", d.getQueryParameter("host"));
                o.put("token", d.getQueryParameter("token"));
                pendingPairLink = o.toString();
                emit("pair", o);
            } catch (Exception ignored) { }
        } else if (Intent.ACTION_SEND.equals(action) || Intent.ACTION_SEND_MULTIPLE.equals(action)) {
            final List<Uri> uris = new ArrayList<>();
            if (Intent.ACTION_SEND.equals(action)) {
                Uri u = intent.getParcelableExtra(Intent.EXTRA_STREAM);
                if (u != null) uris.add(u);
            } else {
                ArrayList<Uri> list = intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM);
                if (list != null) uris.addAll(list);
            }
            if (!uris.isEmpty()) {
                toast("Sending " + uris.size() + (uris.size() == 1 ? " file" : " files") + " to PC…");
                worker.execute(() -> uploadUris(uris));
            }
        }
    }

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        // Lets openLocalFile fall back to a file:// address for files the phone's index does not know yet.
        try {
            StrictMode.setVmPolicy(new StrictMode.VmPolicy.Builder().build());
        } catch (Exception ignored) { }

        SharedPreferences prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        serverBaseUrl = prefs.getString("server", "");
        pairingToken = prefs.getString("token", "");

        createNotificationChannel();
        if (Build.VERSION.SDK_INT >= 33 && !hasPermission("android.permission.POST_NOTIFICATIONS")) {
            requestPermissions(new String[]{"android.permission.POST_NOTIFICATIONS"}, 102);
        }

        webView = new WebView(this);
        final FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(0xFF121316);
        root.addView(webView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        setContentView(root);
        // Android 15+ draws apps under the status and navigation bars; keep the page clear of them and of the keyboard.
        root.setOnApplyWindowInsetsListener((v, insets) -> {
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
                android.graphics.Insets ime = insets.getInsets(WindowInsets.Type.ime());
                v.setPadding(bars.left, bars.top, bars.right, Math.max(bars.bottom, ime.bottom));
                // Consumed: the page must not add the same space a second time through CSS safe-area insets.
                return WindowInsets.CONSUMED;
            }
            v.setPadding(insets.getSystemWindowInsetLeft(), insets.getSystemWindowInsetTop(),
                    insets.getSystemWindowInsetRight(), insets.getSystemWindowInsetBottom());
            return insets.consumeSystemWindowInsets();
        });

        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(true);
        settings.setAllowFileAccess(true);
        settings.setAllowContentAccess(true);
        settings.setAllowFileAccessFromFileURLs(true);
        settings.setAllowUniversalAccessFromFileURLs(true);
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setCacheMode(WebSettings.LOAD_DEFAULT);
        settings.setUserAgentString(settings.getUserAgentString() + " CloudSSD-App/0.8.0");
        WebView.setWebContentsDebuggingEnabled(true); // inspect over USB debugging only

        webView.setBackgroundColor(0xFF121316);
        webView.addJavascriptInterface(new AndroidBridge(), "AndroidBridge");

        webView.setWebChromeClient(new WebChromeClient() {
            @Override
            public boolean onConsoleMessage(ConsoleMessage m) {
                Log.i(TAG, "js " + m.messageLevel() + ": " + m.message() + " (" + m.sourceId() + ":" + m.lineNumber() + ")");
                return true;
            }

            @Override
            public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
                if (webChooserCallback != null) webChooserCallback.onReceiveValue(null);
                webChooserCallback = callback;
                Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
                intent.addCategory(Intent.CATEGORY_OPENABLE);
                intent.setType("*/*");
                intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
                try {
                    startActivityForResult(Intent.createChooser(intent, "Select files to send"), REQ_WEB_CHOOSER);
                } catch (Exception e) {
                    webChooserCallback.onReceiveValue(null);
                    webChooserCallback = null;
                    return false;
                }
                return true;
            }
        });

        webView.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                return false;
            }

            @Override
            public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                Uri u = request.getUrl();
                if (THUMB_HOST.equals(u.getHost())) return thumbResponse(u.getQueryParameter("p"));
                return null;
            }
        });

        webView.setDownloadListener((url, userAgent, contentDisposition, mimetype, contentLength) ->
                enqueueDownload(url, URLUtil.guessFileName(url, contentDisposition, mimetype)));

        handleIntent(getIntent());
        webView.loadUrl("file:///android_asset/index.html");
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        handleIntent(intent);
    }

    @Override
    protected void onResume() {
        super.onResume();
        // Coming back from Android's permission screens: let the page re-read what it may now see.
        JSONObject o = new JSONObject();
        try { o.put("storageAccess", storageAccess()); } catch (Exception ignored) { }
        emit("resume", o);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        JSONObject o = new JSONObject();
        try { o.put("storageAccess", storageAccess()); } catch (Exception ignored) { }
        emit("resume", o);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent intent) {
        if (requestCode != REQ_PICK_UPLOAD && requestCode != REQ_WEB_CHOOSER) {
            super.onActivityResult(requestCode, resultCode, intent);
            return;
        }
        final List<Uri> uris = new ArrayList<>();
        if (resultCode == Activity.RESULT_OK && intent != null) {
            if (intent.getClipData() != null) {
                for (int i = 0; i < intent.getClipData().getItemCount(); i++) uris.add(intent.getClipData().getItemAt(i).getUri());
            } else if (intent.getData() != null) {
                uris.add(intent.getData());
            }
        }
        if (requestCode == REQ_WEB_CHOOSER) {
            if (webChooserCallback != null) webChooserCallback.onReceiveValue(uris.isEmpty() ? null : uris.toArray(new Uri[0]));
            webChooserCallback = null;
        } else if (!uris.isEmpty()) {
            worker.execute(() -> uploadUris(uris));
        }
    }

    @Override
    public void onBackPressed() {
        if (webView == null) {
            super.onBackPressed();
            return;
        }
        webView.evaluateJavascript("window.onAndroidBack ? window.onAndroidBack() : false;", value -> {
            if (!"true".equalsIgnoreCase(value)) MainActivity.super.onBackPressed();
        });
    }
}
