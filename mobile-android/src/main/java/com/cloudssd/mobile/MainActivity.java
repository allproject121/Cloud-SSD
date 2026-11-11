package com.cloudssd.mobile;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.ActivityManager;
import android.app.usage.StorageStatsManager;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.ContentUris;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.content.res.Configuration;
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
import android.os.storage.StorageManager;
import android.provider.DocumentsContract;
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
import java.io.FileReader;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.NetworkInterface;
import java.net.Socket;
import java.net.URL;
import java.net.URLConnection;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

import com.cloudssd.mobile.CloudBackend.BackendException;
import com.cloudssd.mobile.CloudBackend.DocInfo;
import com.cloudssd.mobile.CloudBackend.RootInfo;

/**
 * Cloud-SSD phone app: a WebView over the bundled page (assets/index.html) plus the native pieces a page cannot do:
 * finding the PC on the Wi-Fi, reading phone storage and memory, transfers that keep going in the background,
 * thumbnails, PDFs, Google Drive without the PC. The page calls {@link AndroidBridge}; results that take time come
 * back through window.onNative(event, data).
 */
public class MainActivity extends Activity {
    private static final String TAG = "CloudSSD";
    private static final int REQ_WEB_CHOOSER = 1001;
    private static final int REQ_PICK_UPLOAD = 2001;
    private static final int REQ_PERMS = 101;
    private static final String THUMB_HOST = "cloudssd.thumb";
    private static final String PDF_HOST = "cloudssd.pdf";
    private static final String CLOUD_HOST = "cloudssd.cloud";
    private static final String PREFS = "cloudssd";
    private static final int MAX_LIST_ITEMS = 3000;

    private WebView webView;
    private ValueCallback<Uri[]> webChooserCallback;
    private volatile String serverBaseUrl = "";
    private volatile String uploadTarget = "";
    /** A cloudssd://pair link that arrived before the page could take it (JSON, or ""). */
    private volatile String pendingPairLink = "";

    private final ExecutorService worker = Executors.newFixedThreadPool(3);
    private final AtomicBoolean discovering = new AtomicBoolean(false);
    private final LruCache<String, byte[]> thumbCache = new LruCache<String, byte[]>(16 * 1024 * 1024) {
        @Override
        protected int sizeOf(String key, byte[] value) {
            return value.length;
        }
    };
    private final TransferHub.Listener transferListener = t -> emit("transfer", t);

    // ------------------------------------------------------------------ small helpers

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

    private boolean debuggable() {
        return (getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) != 0;
    }

    private boolean nightMode() {
        return (getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES;
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

    /** True for an address on the paired PC (the only place the pairing key may be sent). */
    private boolean isOurServer(String url) {
        if (url == null) return false;
        String server = getSharedPreferences(PREFS, MODE_PRIVATE).getString("server", "");
        if (!server.isEmpty() && url.startsWith(server + "/")) return true;
        for (String c : PcLink.candidates(this, true)) if (url.startsWith(c + "/")) return true;
        return false;
    }

    /** "/api/download?path=…" from a full PC address, without the server part and without the key. */
    private static String routeOf(String url) {
        String r = url;
        int scheme = r.indexOf("://");
        if (scheme > 0) {
            int slash = r.indexOf('/', scheme + 3);
            r = slash < 0 ? "/" : r.substring(slash);
        }
        return r.replaceAll("([?&])token=[^&]*&?", "$1").replaceAll("[?&]$", "");
    }

    private static JSONObject docJson(DocInfo d) {
        JSONObject o = new JSONObject();
        try {
            o.put("id", d.id).put("name", d.name).put("isDirectory", d.dir).put("size", d.size).put("modified", d.modified)
                    .put("canEdit", d.canEdit).put("canCreate", d.canCreate).put("exported", d.exported);
        } catch (Exception ignored) { }
        return o;
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
                o.put("network", PcLink.netInfo(MainActivity.this));
                o.put("installId", PcLink.installId(MainActivity.this));
                o.put("googleAvailable", GoogleAuth.available());
                o.put("night", nightMode());
                o.put("usageAccess", SpaceScanner.hasUsageAccess(MainActivity.this));
                o.put("tailscale", new JSONObject().put("installed", tailscaleInstalled()).put("vpn", vpnActive()));
            } catch (Exception ignored) { }
            return o.toString();
        }

        /** The paired PC and this phone's key, for the page (the key lives encrypted on the phone, not in the page). */
        @JavascriptInterface
        public String getPairing() {
            try {
                return new JSONObject().put("server", getSharedPreferences(PREFS, MODE_PRIVATE).getString("server_lan",
                        getSharedPreferences(PREFS, MODE_PRIVATE).getString("server", ""))).put("token", TokenStore.pcToken(MainActivity.this)).toString();
            } catch (Exception e) {
                return "{}";
            }
        }

        /** The page tells us which PC it is paired with, so uploads and downloads go to the same place. */
        @JavascriptInterface
        public void setServerBase(String url, String token) {
            String clean = url == null ? "" : url.trim();
            while (clean.endsWith("/")) clean = clean.substring(0, clean.length() - 1);
            SharedPreferences p = getSharedPreferences(PREFS, MODE_PRIVATE);
            boolean changed = !p.getString("server_lan", p.getString("server", "")).equals(clean);
            serverBaseUrl = clean;
            p.edit().putString("server", clean).putString("server_lan", clean).commit();
            TokenStore.setPcToken(MainActivity.this, token == null ? "" : token.trim());
            PcLink.setCandidates(MainActivity.this, clean, null, null);
            // The phone's Files app lists the cloud drives of the paired PC.
            if (changed) CloudDocumentsProvider.notifyRootsChanged(MainActivity.this);
        }

        /** Away addresses (Tailscale) and the PC's id, from the PC's status: used when the home address does not answer. */
        @JavascriptInterface
        public void setServerCandidates(String json) {
            try {
                JSONObject o = new JSONObject(json);
                PcLink.setCandidates(MainActivity.this, o.optString("lan", ""), o.optJSONArray("away"), o.optString("pcId", ""));
            } catch (Exception ignored) { }
        }

        /** What the PC can do and how it stores backups (saved for the Files app and camera backup). */
        @JavascriptInterface
        public void setPcInfo(String json) {
            try {
                JSONObject o = new JSONObject(json);
                SharedPreferences.Editor e = getSharedPreferences(PREFS, MODE_PRIVATE).edit();
                if (o.has("features")) e.putString("features", o.optJSONArray("features").toString());
                if (o.has("backupCustom")) e.putBoolean("backup_custom", o.optBoolean("backupCustom"));
                e.apply();
            } catch (Exception ignored) { }
        }

        /** A cloudssd://pair link the app was opened with (JSON {host, token|pair}), handed over once. */
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

        // ---- transfers (TransferHub keeps them going in the background) ----------------------------------------

        @JavascriptInterface
        public void uploadFileByPath(final String localFilePath) {
            if (localFilePath == null || localFilePath.trim().isEmpty()) return;
            final File file = new File(localFilePath);
            if (!file.isFile()) {
                toast("File not found: " + file.getName());
                return;
            }
            TransferHub.get(MainActivity.this).enqueueUpload(file.getName(), file.length(), "file", file.getAbsolutePath(), "");
        }

        /** "Upload here": the files picked next go into this cloud folder ("&lt;drive&gt;:&lt;folder&gt;") instead of the PC's upload folder. */
        @JavascriptInterface
        public void setUploadTarget(String folderId) {
            uploadTarget = folderId == null ? "" : folderId.trim();
        }

        @JavascriptInterface
        public void pickAndUploadFiles(final String type) {
            runOnUiThread(() -> {
                // OPEN_DOCUMENT (not GET_CONTENT): the app may keep reading the files, so Retry works after it was closed.
                Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
                intent.addCategory(Intent.CATEGORY_OPENABLE);
                intent.setType("*/*");
                if ("photos".equalsIgnoreCase(type) || "images".equalsIgnoreCase(type))
                    intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[]{"image/*", "video/*"});
                intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
                try {
                    startActivityForResult(intent, REQ_PICK_UPLOAD);
                } catch (Exception e) {
                    toast("Cannot open file picker: " + e.getMessage());
                }
            });
        }

        /** Saves a PC file into Downloads. The URL may carry the key; only its route is kept, the hub adds the key itself. */
        @JavascriptInterface
        public void downloadFile(final String downloadUrl, final String rawFileName) {
            String name = rawFileName == null || rawFileName.trim().isEmpty() ? "download_" + System.currentTimeMillis() : rawFileName.trim();
            if (downloadUrl != null && downloadUrl.startsWith("cloud:")) TransferHub.get(MainActivity.this).enqueueDownload(downloadUrl, name, -1);
            else TransferHub.get(MainActivity.this).enqueueDownload(routeOf(downloadUrl), name, -1);
        }

        /** Several downloads at once: [{url, name, size}]. */
        @JavascriptInterface
        public void downloadFiles(String json) {
            try {
                JSONArray a = new JSONArray(json);
                for (int i = 0; i < a.length(); i++) {
                    JSONObject o = a.getJSONObject(i);
                    String url = o.optString("url");
                    TransferHub.get(MainActivity.this).enqueueDownload(url.startsWith("cloud:") ? url : routeOf(url), o.optString("name", "download"), o.optLong("size", -1));
                }
            } catch (Exception e) {
                toast("Download could not start");
            }
        }

        @JavascriptInterface
        public void cancelTransfer(int id) {
            TransferHub.get(MainActivity.this).cancel(id);
        }

        @JavascriptInterface
        public void retryTransfer(int id) {
            TransferHub.get(MainActivity.this).retry(id);
        }

        @JavascriptInterface
        public void dismissTransfer(int id) {
            TransferHub.get(MainActivity.this).dismiss(id);
        }

        /** Everything queued, running, waiting or failed (and what just finished), so the page can show it after a restart. */
        @JavascriptInterface
        public String listTransfers() {
            return TransferHub.get(MainActivity.this).events().toString();
        }

        @JavascriptInterface
        public void copyText(String text) {
            runOnUiThread(() -> {
                ClipboardManager cm = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
                if (cm != null) cm.setPrimaryClip(ClipData.newPlainText("Cloud-SSD", text));
            });
        }

        // ---- camera backup -----------------------------------------------------------------------------------

        /** Camera backup status for the Home card (JSON, see CameraBackup.statusJson). */
        @JavascriptInterface
        public String getCameraBackup() {
            return CameraBackup.statusJson(MainActivity.this);
        }

        /** Turns camera backup on (and starts it) or off. False when the app may not see photos yet. */
        @JavascriptInterface
        public boolean setCameraBackup(boolean on) {
            if (on && "none".equals(storageAccess())) return false;
            // Lets the backup keep the place a photo was taken; Android grants it together with photo access.
            if (on && Build.VERSION.SDK_INT >= 29 && !hasPermission("android.permission.ACCESS_MEDIA_LOCATION"))
                runOnUiThread(() -> requestPermissions(new String[]{"android.permission.ACCESS_MEDIA_LOCATION"}, 4321));
            CameraBackup.setEnabled(MainActivity.this, on);
            if (on) CameraBackup.runNow(MainActivity.this);
            return true;
        }

        @JavascriptInterface
        public void backupNow() {
            CameraBackup.runNow(MainActivity.this);
        }

        /** Photo/video folders on the phone; arrives as a 'mediaFolders' event. */
        @JavascriptInterface
        public void listMediaFolders() {
            worker.execute(() -> emit("mediaFolders", CameraBackup.mediaFolders(MainActivity.this)));
        }

        @JavascriptInterface
        public void setBackupFolders(String json) {
            try {
                CameraBackup.setFolders(MainActivity.this, new JSONArray(json));
            } catch (Exception ignored) { }
        }

        /** {requireCharging, requireBatteryNotLow, network: "wifi"|"any", directToDrive, deviceName} */
        @JavascriptInterface
        public void setBackupOptions(String json) {
            try {
                CameraBackup.setOptions(MainActivity.this, new JSONObject(json));
            } catch (Exception ignored) { }
        }

        // ---- opening files -------------------------------------------------------------------------------------

        /** Opens a PC file in another app, e.g. a video player. The URL is a signed link (no pairing key in it). */
        @JavascriptInterface
        public void openWith(final String fileUrl, final String mimeType, final String fileName) {
            runOnUiThread(() -> {
                try {
                    if (fileUrl != null && fileUrl.contains("token=")) {
                        toast("Update Cloud-SSD on the PC to open files in other apps safely.");
                        return;
                    }
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

        /** Opens a cloud item (id) in another app through the Files-app provider, which streams it. */
        @JavascriptInterface
        public void cloudOpenWith(final String id, final String fileName) {
            runOnUiThread(() -> {
                try {
                    Uri uri = DocumentsContract.buildDocumentUri(CloudDocumentsProvider.AUTHORITY, id);
                    Intent intent = new Intent(Intent.ACTION_VIEW);
                    intent.setDataAndType(uri, mimeOf(fileName));
                    intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    startActivity(Intent.createChooser(intent, "Open " + fileName + " with"));
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
                    if (uri == null) {
                        toast("Android has not indexed this file yet; try again in a moment.");
                        return;
                    }
                    Intent intent = new Intent(Intent.ACTION_VIEW);
                    intent.setDataAndType(uri, mimeOf(f.getName()));
                    intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    startActivity(Intent.createChooser(intent, "Open " + f.getName() + " with"));
                } catch (Exception e) {
                    toast("Cannot open: " + e.getMessage());
                }
            });
        }

        /** source: a phone path, "pc:" + route, or "cloud:" + id; arrives as 'pdf' events. */
        @JavascriptInterface
        public void openPdf(final int reqId, final String source, final String name, final long size) {
            final String src = source != null && source.startsWith("pc:") ? "pc:" + routeOf(source.substring(3)) : source;
            worker.execute(() -> PdfPages.openPdf(MainActivity.this, reqId, src, size, ev -> emit("pdf", ev)));
        }

        @JavascriptInterface
        public void closePdf(String key) {
            PdfPages.close(key);
        }

        // ---- Google Drive without the PC -----------------------------------------------------------------------

        /** {available, direct, accounts: [{email, folder, needsReauth, totalBytes, freeBytes}], source} */
        @JavascriptInterface
        public String driveStatus() {
            try {
                return new JSONObject().put("available", GoogleAuth.available())
                        .put("direct", getSharedPreferences(PREFS, MODE_PRIVATE).getBoolean("drive_direct", true))
                        .put("accounts", GoogleAuth.accounts(MainActivity.this)).toString();
            } catch (Exception e) {
                return "{}";
            }
        }

        @JavascriptInterface
        public void driveAddAccount() {
            runOnUiThread(() -> {
                try {
                    GoogleAuth.begin(MainActivity.this, r -> emit("drive", r));
                } catch (IOException e) {
                    try {
                        emit("drive", new JSONObject().put("ok", false).put("error", e.getMessage()));
                    } catch (Exception ignored) { }
                }
            });
        }

        @JavascriptInterface
        public void driveRemoveAccount(String email) {
            GoogleAuth.signOut(MainActivity.this, email);
            CloudDocumentsProvider.notifyRootsChanged(MainActivity.this);
            try {
                emit("drive", new JSONObject().put("ok", true).put("removed", email));
            } catch (Exception ignored) { }
        }

        @JavascriptInterface
        public void setDriveDirect(boolean on) {
            getSharedPreferences(PREFS, MODE_PRIVATE).edit().putBoolean("drive_direct", on).apply();
            CloudDocumentsProvider.notifyRootsChanged(MainActivity.this);
        }

        /** Files on the cloud by drive id, from the PC or Google Drive (whichever answers); arrives as a 'cloud' event. */
        @JavascriptInterface
        public void cloudOp(final int reqId, final String op, final String argsJson) {
            worker.execute(() -> emit("cloud", runCloudOp(reqId, op, argsJson)));
        }

        // ---- Space & RAM ---------------------------------------------------------------------------------------

        /** what: summary | folder (arg = path) | apps | ram | cleanups | large; arrives as a 'space' event. */
        @JavascriptInterface
        public void spaceGet(final int reqId, final String what, final String arg) {
            worker.execute(() -> {
                JSONObject r = new JSONObject();
                try {
                    r.put("reqId", reqId).put("what", what);
                    if (!"ram".equals(what)) SpaceScanner.ensureFresh(5 * 60_000L);
                    Object data;
                    switch (what) {
                        case "summary": data = SpaceScanner.summary(MainActivity.this, buildStorageReport(), new JSONObject(getPhoneRamBreakdown()), deviceName()); break;
                        case "folder": data = SpaceScanner.folder(arg); break;
                        case "apps": data = SpaceScanner.apps(MainActivity.this); break;
                        case "ram": data = new JSONObject().put("ram", SpaceScanner.summary(MainActivity.this, new JSONObject(), new JSONObject(getPhoneRamBreakdown()), deviceName()).get("ram"))
                                .put("apps", SpaceScanner.ramApps(MainActivity.this)); break;
                        case "cleanups": data = SpaceScanner.cleanups(MainActivity.this); break;
                        case "large": data = SpaceScanner.large(); break;
                        default: throw new IllegalArgumentException("unknown " + what);
                    }
                    r.put("ok", true).put("data", data);
                } catch (Exception e) {
                    try { r.put("ok", false).put("error", String.valueOf(e.getMessage())); } catch (Exception ignored) { }
                }
                emit("space", r);
            });
        }

        /** One ⋮ action on this phone; the result arrives as a 'spaceResult' event {reqId, ok, message, freed}. */
        @JavascriptInterface
        public void spaceAction(final int reqId, final String kind, final String target) {
            worker.execute(() -> runSpaceAction(kind, target, (ok, message, freed) -> {
                try {
                    emit("spaceResult", new JSONObject().put("reqId", reqId).put("ok", ok).put("message", message).put("freed", freed));
                } catch (Exception ignored) { }
            }));
        }

        /** {share, control, usageAccess}: what the PC may see and do with this phone. */
        @JavascriptInterface
        public String getSpaceSharing() {
            SharedPreferences p = getSharedPreferences(PREFS, MODE_PRIVATE);
            try {
                return new JSONObject().put("share", p.getBoolean("space_share", true)).put("control", p.getBoolean("space_control", true))
                        .put("usageAccess", SpaceScanner.hasUsageAccess(MainActivity.this)).toString();
            } catch (Exception e) {
                return "{}";
            }
        }

        @JavascriptInterface
        public void setSpaceSharing(String json) {
            try {
                JSONObject o = new JSONObject(json);
                SharedPreferences.Editor e = getSharedPreferences(PREFS, MODE_PRIVATE).edit();
                if (o.has("share")) e.putBoolean("space_share", o.optBoolean("share"));
                if (o.has("control")) e.putBoolean("space_control", o.optBoolean("control"));
                e.apply();
                reportNow = true;
            } catch (Exception ignored) { }
        }

        /** Android's "Usage access" screen, where the user lets this app see app sizes and recently used apps. */
        @JavascriptInterface
        public void openUsageAccess() {
            runOnUiThread(() -> {
                try {
                    startActivity(new Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS));
                } catch (Exception e) {
                    toast("Open Settings → Apps → Special app access → Usage access");
                }
            });
        }

        /** Opens Tailscale, or its Play Store page when it is not installed. */
        @JavascriptInterface
        public void openTailscale() {
            runOnUiThread(() -> {
                Intent launch = getPackageManager().getLaunchIntentForPackage(TAILSCALE);
                try {
                    if (launch != null) startActivity(launch);
                    else startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=" + TAILSCALE)));
                } catch (Exception e) {
                    try {
                        startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse("https://play.google.com/store/apps/details?id=" + TAILSCALE)));
                    } catch (Exception ignored) { }
                }
            });
        }

        /** Play on phone: opens the full-screen game screen, which starts the game on the PC and streams it. */
        @JavascriptInterface
        public void startGameStream() {
            runOnUiThread(() -> startActivity(new Intent(MainActivity.this, StreamActivity.class)));
        }

        /** The backup way to play (Artemis), while our own stream is new. False when Artemis is not installed. */
        @JavascriptInterface
        public boolean openArtemis() {
            Intent launch = getPackageManager().getLaunchIntentForPackage("com.limelight.noir");
            if (launch == null) return false;
            runOnUiThread(() -> startActivity(launch));
            return true;
        }

        /**
         * Android shows how much RAM each app uses only in its own screens (no app may read that for others). Opens
         * "Running services" (Developer options), else Developer options, else App settings.
         */
        @JavascriptInterface
        public void openRunningServices() {
            runOnUiThread(() -> {
                Intent[] tries = {
                        new Intent().setClassName("com.android.settings", "com.android.settings.Settings$RunningServicesActivity"),
                        new Intent(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS),
                        new Intent(Settings.ACTION_MANAGE_APPLICATIONS_SETTINGS)
                };
                for (Intent i : tries) {
                    try {
                        startActivity(i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                        return;
                    } catch (Exception ignored) { }
                }
                toast("Open Settings → Developer options → Running services");
            });
        }
    }

    // ------------------------------------------------------------------ Space & RAM: actions and sharing with the PC

    private static final String TAILSCALE = "com.tailscale.ipn";
    private static final int REQ_TRASH = 3101;

    interface SpaceDone {
        void done(boolean ok, String message, long freed);
    }

    private volatile SpaceDone trashDone;
    private final java.util.Set<String> spaceFolders = Collections.synchronizedSet(new java.util.HashSet<String>());
    private java.util.concurrent.ScheduledExecutorService spaceSync;
    private volatile long lastReport;
    private volatile boolean reportNow = true;

    private static String deviceName() {
        String m = Build.MODEL == null ? "Phone" : Build.MODEL;
        return Build.MANUFACTURER != null && !m.toLowerCase(Locale.US).startsWith(Build.MANUFACTURER.toLowerCase(Locale.US)) ? Build.MANUFACTURER + " " + m : m;
    }

    private static long freeBytes() {
        try {
            return new StatFs(Environment.getExternalStorageDirectory().getPath()).getAvailableBytes();
        } catch (Exception e) {
            return 0;
        }
    }

    private static boolean deleteTree(File f) {
        if (f.isDirectory()) {
            File[] list = f.listFiles();
            if (list != null) for (File c : list) deleteTree(c);
        }
        return f.delete() || !f.exists();
    }

    private void rescan(String path) {
        try {
            android.media.MediaScannerConnection.scanFile(this, new String[]{path}, null, null);
        } catch (Exception ignored) { }
    }

    /** True when Tailscale's VPN is up on this phone (any VPN counts: Android does not say which). */
    private boolean vpnActive() {
        try {
            android.net.ConnectivityManager cm = (android.net.ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
            if (Build.VERSION.SDK_INT < 23 || cm == null) return false;
            android.net.NetworkCapabilities caps = cm.getNetworkCapabilities(cm.getActiveNetwork());
            return caps != null && caps.hasTransport(android.net.NetworkCapabilities.TRANSPORT_VPN);
        } catch (Exception e) {
            return false;
        }
    }

    private boolean tailscaleInstalled() {
        try {
            getPackageManager().getPackageInfo(TAILSCALE, 0);
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    /** One ⋮ action, asked for on this screen or by the PC. Android's own prompts (trash, uninstall) appear on the phone. */
    private void runSpaceAction(String kind, String target, SpaceDone done) {
        try {
            switch (kind) {
                case "open":
                    new AndroidBridge().openLocalFile(target);
                    done.done(true, "Opened on the phone.", 0);
                    return;
                case "delete": {
                    File f = new File(target);
                    if (SpaceScanner.isProtected(target)) { done.done(false, "Android's own folders cannot be deleted here.", 0); return; }
                    if (!f.exists()) { done.done(false, "It is not there any more.", 0); return; }
                    long before = freeBytes();
                    boolean ok = deleteTree(f);
                    rescan(target);
                    reportNow = true;
                    done.done(ok, ok ? "Deleted." : "Android did not let it be deleted.", Math.max(0, freeBytes() - before));
                    return;
                }
                case "recycle": {
                    File f = new File(target);
                    Uri u = Build.VERSION.SDK_INT >= 30 ? contentUriFor(f) : null;
                    if (u == null) { done.done(false, "This file cannot go to the trash on this phone; use Delete.", 0); return; }
                    trash(Collections.singletonList(u), done);
                    return;
                }
                case "cloud": {
                    File f = new File(target);
                    if (SpaceScanner.isProtected(target) || !f.exists()) { done.done(false, "That cannot be moved.", 0); return; }
                    String base = "cloudssd:Offloaded_Files/From phone";
                    int n = moveToCloud(f, f.isDirectory() ? base + "/" + f.getName() : base);
                    reportNow = true;
                    done.done(n > 0, n > 0 ? "Uploading " + n + (n == 1 ? " file" : " files") + " to the cloud drive; each is removed from the phone once it is safely there."
                            : "There is nothing to move.", 0);
                    return;
                }
                case "uninstall":
                    runOnUiThread(() -> {
                        try {
                            startActivity(new Intent(Intent.ACTION_DELETE, Uri.parse("package:" + target)));
                        } catch (Exception ignored) { }
                    });
                    reportNow = true;
                    done.done(true, "Android asks on the phone whether to uninstall it.", 0);
                    return;
                case "app-info":
                    runOnUiThread(() -> {
                        try {
                            startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + target)));
                        } catch (Exception ignored) { }
                    });
                    done.done(true, "App info is open on the phone (Clear cache, Force stop).", 0);
                    return;
                case "clean":
                    clean(target, done);
                    return;
                case "folder":
                    spaceFolders.add(target);
                    reportNow = true;
                    done.done(true, "", 0);
                    return;
                case "report":
                    reportNow = true;
                    done.done(true, "Sending a fresh report.", 0);
                    return;
                default:
                    done.done(false, "This phone app cannot do \"" + kind + "\". Update it.", 0);
            }
        } catch (Exception e) {
            done.done(false, String.valueOf(e.getMessage()), 0);
        }
    }

    /** Queues every file (in a folder: all of them, keeping the folders) for upload; each is deleted after its upload. */
    private int moveToCloud(File f, String target) {
        if (f.isFile()) {
            TransferHub.get(this).enqueueMove(f.getName(), f.length(), f.getAbsolutePath(), target);
            return 1;
        }
        int n = 0;
        File[] list = f.listFiles();
        if (list != null) for (File c : list) n += moveToCloud(c, c.isDirectory() ? target + "/" + c.getName() : target);
        return n;
    }

    private void clean(String id, SpaceDone done) {
        long before = freeBytes();
        switch (id) {
            case "app-cache": {
                File[] dirs = {getCacheDir(), getExternalCacheDir()};
                for (File d : dirs) {
                    File[] list = d == null ? null : d.listFiles();
                    if (list != null) for (File c : list) deleteTree(c);
                }
                thumbCache.evictAll();
                break;
            }
            case "apks":
                for (File f : SpaceScanner.apkFiles()) if (f.delete()) rescan(f.getAbsolutePath());
                break;
            case "thumbnails": {
                File[] list = new File(SpaceScanner.ROOT, "DCIM/.thumbnails").listFiles();
                if (list != null) for (File c : list) deleteTree(c);
                break;
            }
            case "backed-up": {
                List<Object[]> items = CameraBackup.backedUp(this, 30L * 24 * 3600_000L);
                if (items.isEmpty()) { done.done(true, "Nothing to remove: no backed-up photo is older than 30 days.", 0); return; }
                List<Uri> uris = new ArrayList<>();
                for (Object[] o : items) uris.add((Uri) o[0]);
                if (Build.VERSION.SDK_INT >= 30) { trash(uris, done); return; }
                for (Uri u : uris) {
                    try { getContentResolver().delete(u, null, null); } catch (Exception ignored) { }
                }
                break;
            }
            case "system":
                runOnUiThread(() -> {
                    try {
                        startActivity(new Intent(StorageManager.ACTION_MANAGE_STORAGE));
                    } catch (Exception e) {
                        toast("Open Settings → Storage");
                    }
                });
                done.done(true, "Android's Free up space screen is open on the phone.", 0);
                return;
            default:
                done.done(false, "Unknown clean-up.", 0);
                return;
        }
        reportNow = true;
        long freed = Math.max(0, freeBytes() - before);
        done.done(true, "Cleaned.", freed);
    }

    /** Android 11+: moves photos/videos to the system trash after its own prompt (they can be restored for 30 days). */
    private void trash(List<Uri> uris, SpaceDone done) {
        if (Build.VERSION.SDK_INT < 30) { done.done(false, "No trash on this Android version.", 0); return; }
        try {
            android.app.PendingIntent pi = MediaStore.createTrashRequest(getContentResolver(), uris, true);
            trashDone = done;
            runOnUiThread(() -> {
                try {
                    startIntentSenderForResult(pi.getIntentSender(), REQ_TRASH, null, 0, 0, 0);
                } catch (Exception e) {
                    SpaceDone d = trashDone;
                    trashDone = null;
                    if (d != null) d.done(false, "Android's trash prompt did not open: " + e.getMessage(), 0);
                }
            });
        } catch (Exception e) {
            done.done(false, "Could not move it to the trash: " + e.getMessage(), 0);
        }
    }

    /** While the app is open: tell the PC about this phone (every 45 s, or at once after a change) and do what it asked. */
    private void startSpaceSync() {
        if (spaceSync != null) return;
        spaceSync = java.util.concurrent.Executors.newSingleThreadScheduledExecutor();
        spaceSync.scheduleWithFixedDelay(this::spaceSyncTick, 4, 5, java.util.concurrent.TimeUnit.SECONDS);
    }

    private void stopSpaceSync() {
        if (spaceSync != null) {
            spaceSync.shutdownNow();
            spaceSync = null;
        }
    }

    private boolean pcHas(String feature) {
        String f = getSharedPreferences(PREFS, MODE_PRIVATE).getString("features", "[]");
        return f.contains("\"" + feature + "\"");
    }

    private void spaceSyncTick() {
        try {
            SharedPreferences p = getSharedPreferences(PREFS, MODE_PRIVATE);
            if (!p.getBoolean("space_share", true) || !pcHas("space-phone") || TokenStore.pcToken(this).isEmpty()) return;
            String server = PcLink.resolve(this);
            if (server.isEmpty()) return;
            boolean control = p.getBoolean("space_control", true);
            if (control) {
                String cmds = spaceHttp("GET", server + "/api/space/phone-commands", null);
                JSONArray list = cmds == null ? null : new JSONArray(cmds);
                for (int i = 0; list != null && i < list.length(); i++) {
                    JSONObject c = list.getJSONObject(i);
                    final String id = c.optString("id"), srv = server;
                    runSpaceAction(c.optString("kind"), c.optString("target"), (ok, message, freed) -> worker.execute(() -> {
                        try {
                            spaceHttp("POST", srv + "/api/space/phone-result?id=" + Uploader.enc(id) + "&ok=" + (ok ? 1 : 0) + "&freed=" + freed
                                    + "&message=" + Uploader.enc(message == null ? "" : message), "");
                        } catch (Exception ignored) { }
                        reportNow = true;
                    }));
                }
            }
            if (reportNow || System.currentTimeMillis() - lastReport > 45_000) {
                reportNow = false;
                lastReport = System.currentTimeMillis();
                SpaceScanner.ensureFresh(60_000);
                JSONObject r = SpaceScanner.report(this, buildStorageReport(), new JSONObject(new AndroidBridge().getPhoneRamBreakdown()), deviceName(),
                        new java.util.HashSet<>(spaceFolders), control);
                spaceHttp("POST", server + "/api/space/phone-report", r.toString());
            }
        } catch (Exception e) {
            Log.w(TAG, "Space sync: " + e.getMessage());
        }
    }

    /** One small request to the paired PC with this phone's key; the answer body, or null when it did not answer 2xx. */
    private String spaceHttp(String method, String url, String body) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        try {
            c.setRequestMethod(method);
            c.setConnectTimeout(6000);
            c.setReadTimeout(30000);
            c.setRequestProperty("X-Pairing-Token", TokenStore.pcToken(this));
            if (body != null) {
                byte[] b = body.getBytes("UTF-8");
                c.setDoOutput(true);
                c.setFixedLengthStreamingMode(b.length);
                c.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                try (java.io.OutputStream o = c.getOutputStream()) {
                    o.write(b);
                }
            }
            int code = c.getResponseCode();
            if (code < 200 || code >= 300) return null;
            try (InputStream in = c.getInputStream()) {
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                byte[] buf = new byte[8192];
                for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
                return out.toString("UTF-8");
            }
        } finally {
            c.disconnect();
        }
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
            return null; // no embedded art, broken file, â€¦: the page shows its icon instead
        } finally {
            if (bmp != null) bmp.recycle();
        }
    }

    // ------------------------------------------------------------------ files on the cloud (PC or Google Drive)

    /** Runs one file operation by drive id and answers {reqId, ok, status, data, error, source}. */
    private JSONObject runCloudOp(int reqId, String op, String argsJson) {
        JSONObject r = new JSONObject();
        CloudBackend b = Backends.files(this);
        try {
            r.put("reqId", reqId);
            JSONObject a = argsJson == null || argsJson.isEmpty() ? new JSONObject() : new JSONObject(argsJson);
            Object data;
            switch (op) {
                case "roots": {
                    JSONArray out = new JSONArray();
                    for (RootInfo ri : b.roots())
                        out.put(new JSONObject().put("id", ri.id).put("title", ri.title).put("readOnly", ri.readOnly).put("canCreate", ri.canCreate)
                                .put("totalBytes", ri.totalBytes).put("freeBytes", ri.freeBytes));
                    data = out;
                    break;
                }
                case "list": {
                    JSONArray out = new JSONArray();
                    for (DocInfo d : b.list(a.getString("path"))) out.put(docJson(d));
                    data = new JSONObject().put("entries", out);
                    break;
                }
                case "search": {
                    JSONArray out = new JSONArray();
                    for (DocInfo d : b.search(a.getString("path"), a.getString("q"), a.optInt("max", 100))) out.put(docJson(d));
                    data = new JSONObject().put("entries", out);
                    break;
                }
                case "stat": data = docJson(b.stat(a.getString("path"))); break;
                case "create": data = docJson(b.create(a.getString("parent"), a.getString("name"), a.optBoolean("dir"))); break;
                case "rename": data = docJson(b.rename(a.getString("path"), a.getString("name"))); break;
                case "move": data = docJson(b.move(a.getString("path"), a.getString("to"))); break;
                case "delete": b.delete(a.getString("path")); data = new JSONObject().put("success", true); break;
                default: throw new BackendException(400, "Unknown operation " + op);
            }
            r.put("ok", true).put("status", 200).put("data", data);
        } catch (BackendException e) {
            try { r.put("ok", false).put("status", e.status).put("error", e.getMessage()); } catch (Exception ignored) { }
        } catch (Exception e) {
            try { r.put("ok", false).put("status", 500).put("error", String.valueOf(e.getMessage())); } catch (Exception ignored) { }
        }
        try { r.put("source", b.label()); } catch (Exception ignored) { }
        return r;
    }

    private static long get(Map<String, Long> m, String key) {
        Long v = m.get(key);
        return v == null ? 0 : v;
    }

    private static JSONObject withReq(int reqId, JSONObject o) {
        try { o.put("reqId", reqId); } catch (Exception ignored) { }
        return o;
    }

    private static WebResourceResponse bytesResponse(String mime, byte[] data, int status) {
        Map<String, String> headers = new HashMap<>();
        headers.put("Access-Control-Allow-Origin", "*");
        if (data == null) return new WebResourceResponse(mime, null, 404, "Not Found", headers, new ByteArrayInputStream(new byte[0]));
        headers.put("Cache-Control", "max-age=86400");
        return new WebResourceResponse(mime, null, status, "OK", headers, new ByteArrayInputStream(data));
    }

    /** cloudssd.cloud/read?id=…: a cloud file for the page's video, picture and audio players, with Range support. */
    private WebResourceResponse cloudRead(Uri u, Map<String, String> requestHeaders) {
        String id = u.getQueryParameter("id");
        Map<String, String> headers = new HashMap<>();
        headers.put("Access-Control-Allow-Origin", "*");
        headers.put("Accept-Ranges", "bytes");
        try {
            CloudBackend b = Backends.files(this);
            DocInfo d = b.stat(id);
            long start = 0;
            String range = requestHeaders == null ? null : requestHeaders.get("Range");
            if (range != null && range.startsWith("bytes=")) {
                String spec = range.substring(6).split(",")[0];
                int dash = spec.indexOf('-');
                if (dash > 0) start = Long.parseLong(spec.substring(0, dash).trim());
            }
            InputStream in = b.openRead(id, start);
            String mime = mimeOf(d.name);
            if (start > 0 && d.size > 0) {
                headers.put("Content-Range", "bytes " + start + "-" + (d.size - 1) + "/" + d.size);
                return new WebResourceResponse(mime, null, 206, "Partial Content", headers, in);
            }
            if (d.size > 0) headers.put("Content-Length", String.valueOf(d.size));
            return new WebResourceResponse(mime, null, 200, "OK", headers, in);
        } catch (Exception e) {
            return new WebResourceResponse("text/plain", null, 404, "Not Found", headers, new ByteArrayInputStream(new byte[0]));
        }
    }

    private WebResourceResponse cloudThumb(Uri u) {
        try (InputStream in = Backends.files(this).thumbnail(u.getQueryParameter("id"), 320)) {
            if (in == null) return bytesResponse("image/jpeg", null, 404);
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buf = new byte[16 * 1024];
            for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
            return bytesResponse("image/jpeg", out.toByteArray(), 200);
        } catch (Exception e) {
            return bytesResponse("image/jpeg", null, 404);
        }
    }

    // ------------------------------------------------------------------ uploads from the picker or other apps

    /**
     * Queues files chosen in a picker or shared from another app.
     * @param persisted the app may keep reading them (OPEN_DOCUMENT), so a retry works after the app was closed
     * @param target    the cloud folder ("&lt;drive&gt;:&lt;folder&gt;") to save into, or "" for the PC's upload folder
     */
    private void uploadUris(List<Uri> uris, final String target, boolean persisted) {
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
                TransferHub.get(this).enqueueUpload(f.getName(), f.length(), "file", f.getAbsolutePath(), target);
                continue;
            }
            boolean kept = false;
            if (persisted) {
                try {
                    getContentResolver().takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    kept = true;
                } catch (Exception ignored) { }
            }
            TransferHub.get(this).enqueueUpload(name, size, kept ? "uri" : "uri-temp", uri.toString(), target);
        }
    }

    // ------------------------------------------------------------------ activity

    /** Pairing links, the return from Google sign-in, and files shared to this app from other apps. */
    private void handleIntent(Intent intent) {
        if (intent == null || intent.getAction() == null) return;
        String action = intent.getAction();
        Uri d = intent.getData();
        if (Intent.ACTION_VIEW.equals(action) && d != null && "cloudssd".equals(d.getScheme()) && "pair".equals(d.getHost())) {
            try {
                JSONObject o = new JSONObject();
                o.put("host", d.getQueryParameter("host"));
                if (d.getQueryParameter("pair") != null) o.put("pair", d.getQueryParameter("pair"));
                if (d.getQueryParameter("token") != null) o.put("token", d.getQueryParameter("token"));
                pendingPairLink = o.toString();
                emit("pair", o);
            } catch (Exception ignored) { }
        } else if (Intent.ACTION_VIEW.equals(action) && d != null && GoogleAuth.isRedirect(d)) {
            final Uri data = d;
            worker.execute(() -> {
                JSONObject r = GoogleAuth.handleRedirect(MainActivity.this, data);
                CloudDocumentsProvider.notifyRootsChanged(MainActivity.this);
                emit("drive", r);
            });
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
                worker.execute(() -> uploadUris(uris, "", false));
            }
        }
        // cloudssd://auth (back from the Desktop-key sign-in page) only brings the app to the front.
    }

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        SharedPreferences prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        serverBaseUrl = prefs.getString("server", "");
        TokenStore.pcToken(this); // moves a key saved in plain text by an older version into the key store

        CameraBackup.ensureScheduled(this);
        if (Build.VERSION.SDK_INT >= 33 && !hasPermission("android.permission.POST_NOTIFICATIONS")) {
            requestPermissions(new String[]{"android.permission.POST_NOTIFICATIONS"}, 102);
        }

        int background = nightMode() ? 0xFF121316 : 0xFFF8F9FC;
        webView = new WebView(this);
        final FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(background);
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

        String version = "0";
        try {
            version = getPackageManager().getPackageInfo(getPackageName(), 0).versionName;
        } catch (Exception ignored) { }

        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(true);
        // The page shows phone photos with file:// addresses; it may not read other files' contents through scripts.
        settings.setAllowFileAccess(true);
        settings.setAllowContentAccess(true);
        settings.setAllowFileAccessFromFileURLs(false);
        settings.setAllowUniversalAccessFromFileURLs(false);
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setCacheMode(WebSettings.LOAD_DEFAULT);
        // Pinch zoom for people who need bigger text; the +/- buttons stay hidden.
        settings.setBuiltInZoomControls(true);
        settings.setDisplayZoomControls(false);
        settings.setUserAgentString(settings.getUserAgentString() + " CloudSSD-App/" + version);
        // Inspecting the page over USB only in a debug build of the app.
        WebView.setWebContentsDebuggingEnabled(debuggable());

        webView.setBackgroundColor(background);
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
            /**
             * Only the app's own page loads inside: a link to anywhere else opens in the browser, so no other site
             * ever runs next to the app's bridge.
             */
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                Uri u = request.getUrl();
                String s = u.toString();
                if (s.startsWith("file:///android_asset/")) return false;
                if ("http".equals(u.getScheme()) || "https".equals(u.getScheme())) {
                    try {
                        startActivity(new Intent(Intent.ACTION_VIEW, u).addCategory(Intent.CATEGORY_BROWSABLE).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                    } catch (Exception ignored) { }
                }
                return true;
            }

            @Override
            public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                Uri u = request.getUrl();
                String host = u.getHost();
                if (THUMB_HOST.equals(host)) return thumbResponse(u.getQueryParameter("p"));
                if (PDF_HOST.equals(host)) {
                    int page = 0, width = 1080;
                    try {
                        page = Integer.parseInt(u.getQueryParameter("p"));
                        width = Integer.parseInt(u.getQueryParameter("w"));
                    } catch (Exception ignored) { }
                    return bytesResponse("image/jpeg", PdfPages.page(u.getQueryParameter("k"), page, width), 200);
                }
                if (CLOUD_HOST.equals(host)) {
                    if ("/thumb".equals(u.getPath())) return cloudThumb(u);
                    return cloudRead(u, request.getRequestHeaders());
                }
                return null;
            }
        });

        webView.setDownloadListener((url, userAgent, contentDisposition, mimetype, contentLength) -> {
            // Only PC addresses go to the transfer queue (which adds the key); anything else goes to the browser.
            if (isOurServer(url) || url.startsWith("/")) {
                TransferHub.get(this).enqueueDownload(routeOf(url), URLUtil.guessFileName(url, contentDisposition, mimetype), contentLength);
            } else {
                try {
                    startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
                } catch (Exception ignored) { }
            }
        });

        TransferHub.get(this).addListener(transferListener);
        handleIntent(getIntent());
        webView.loadUrl("file:///android_asset/index.html");
    }

    @Override
    protected void onDestroy() {
        TransferHub.get(this).removeListener(transferListener);
        super.onDestroy();
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
        startSpaceSync();
    }

    @Override
    protected void onPause() {
        stopSpaceSync();
        super.onPause();
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
        if (requestCode == REQ_TRASH) {
            SpaceDone d = trashDone;
            trashDone = null;
            reportNow = true;
            if (d != null) d.done(resultCode == Activity.RESULT_OK, resultCode == Activity.RESULT_OK
                    ? "In the phone's trash. Android empties it after 30 days (or empty it now in Files or Gallery)." : "Not moved to the trash.", 0);
            return;
        }
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
        } else {
            final String target = uploadTarget; // set by "Upload here" in the cloud file browser, for this pick only
            uploadTarget = "";
            if (!uris.isEmpty()) worker.execute(() -> uploadUris(uris, target, true));
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
