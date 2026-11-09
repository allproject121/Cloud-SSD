package com.cloudssd.mobile;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.ActivityManager;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.ContentValues;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.StatFs;
import android.provider.MediaStore;
import android.provider.OpenableColumns;
import android.view.Window;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.URLUtil;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLConnection;
import java.net.URLEncoder;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.atomic.AtomicInteger;

public class MainActivity extends Activity {
    private WebView webView;
    private ValueCallback<Uri[]> uploadMessage;
    private final static int FILECHOOSER_RESULTCODE = 1001;
    private final static int NATIVE_UPLOAD_FILES_CODE = 2001;
    private static final String NOTIF_CHANNEL_ID = "cloudssd_transfers";
    private static final AtomicInteger NOTIF_ID_GEN = new AtomicInteger(1000);

    private String serverBaseUrl = "http://127.0.0.1:8090";
    private String pairingToken = "cloudssd2026";

    private static String formatBytes(long bytes) {
        if (bytes <= 0) return "0 B";
        final String[] units = new String[] { "B", "KB", "MB", "GB", "TB" };
        int digitGroups = (int) (Math.log10(bytes) / Math.log10(1024));
        if (digitGroups >= units.length) digitGroups = units.length - 1;
        return String.format(Locale.US, "%.1f %s", bytes / Math.pow(1024, digitGroups), units[digitGroups]);
    }

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(
                NOTIF_CHANNEL_ID,
                "File Transfers",
                NotificationManager.IMPORTANCE_LOW
            );
            channel.setDescription("Shows file download, speed and transfer progress");
            channel.enableVibration(false);
            channel.setShowBadge(true);
            NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null) {
                nm.createNotificationChannel(channel);
            }
        }
    }

    public class AndroidBridge {
        @JavascriptInterface
        public void setServerBase(final String url, final String token) {
            if (url != null && !url.trim().isEmpty()) {
                String clean = url.trim();
                if (clean.endsWith("/")) clean = clean.substring(0, clean.length() - 1);
                MainActivity.this.serverBaseUrl = clean;
            }
            if (token != null && !token.trim().isEmpty()) {
                MainActivity.this.pairingToken = token.trim();
            }
        }

        @JavascriptInterface
        public String getPhoneStats() {
            JSONObject obj = new JSONObject();
            try {
                // Internal Storage
                File path = Environment.getDataDirectory();
                StatFs stat = new StatFs(path.getPath());
                long blockSize = stat.getBlockSizeLong();
                long totalStorage = stat.getBlockCountLong() * blockSize;
                long freeStorage = stat.getAvailableBlocksLong() * blockSize;
                long usedStorage = totalStorage - freeStorage;

                obj.put("storageTotal", totalStorage);
                obj.put("storageFree", freeStorage);
                obj.put("storageUsed", usedStorage);

                // RAM
                ActivityManager actMan = (ActivityManager) getSystemService(Context.ACTIVITY_SERVICE);
                ActivityManager.MemoryInfo memInfo = new ActivityManager.MemoryInfo();
                if (actMan != null) {
                    actMan.getMemoryInfo(memInfo);
                    obj.put("ramTotal", memInfo.totalMem);
                    obj.put("ramFree", memInfo.availMem);
                    obj.put("ramUsed", memInfo.totalMem - memInfo.availMem);
                }

                // Device Model & OS
                obj.put("model", Build.MODEL);
                obj.put("manufacturer", Build.MANUFACTURER);
                obj.put("androidVersion", Build.VERSION.RELEASE);
            } catch (Exception ignored) { }
            return obj.toString();
        }

        @JavascriptInterface
        public void downloadFile(final String downloadUrl, final String rawFileName) {
            new Thread(new Runnable() {
                @Override
                public void run() {
                    HttpURLConnection conn = null;
                    final int notifId = NOTIF_ID_GEN.incrementAndGet();
                    final NotificationManager nm = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
                    final String fileName = (rawFileName == null || rawFileName.trim().isEmpty())
                            ? "download_" + System.currentTimeMillis() : rawFileName.trim();

                    Notification.Builder builder;
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                        builder = new Notification.Builder(MainActivity.this, NOTIF_CHANNEL_ID);
                    } else {
                        builder = new Notification.Builder(MainActivity.this);
                    }
                    builder.setSmallIcon(android.R.drawable.stat_sys_download)
                           .setContentTitle("Downloading " + fileName)
                           .setContentText("Connecting to PC...")
                           .setOngoing(true)
                           .setProgress(100, 0, false);

                    if (nm != null) {
                        try { nm.notify(notifId, builder.build()); } catch (Exception ignored) {}
                    }

                    try {
                        URL url = new URL(downloadUrl);
                        conn = (HttpURLConnection) url.openConnection();
                        conn.setRequestMethod("GET");
                        conn.setRequestProperty("X-Pairing-Token", pairingToken);
                        conn.setRequestProperty("Authorization", "Bearer " + pairingToken);
                        conn.setConnectTimeout(15000);
                        conn.setReadTimeout(60000);
                        conn.connect();

                        int code = conn.getResponseCode();
                        if (code == 200) {
                            String mime = URLConnection.guessContentTypeFromName(fileName);
                            if (mime == null) mime = "application/octet-stream";

                            final long contentLength = conn.getContentLengthLong();
                            final long startMillis = System.currentTimeMillis();
                            long lastProgressMillis = startMillis;
                            long lastSampleBytes = 0;
                            long bytesReadTotal = 0;
                            Uri finalUri = null;
                            File targetFile = null;

                            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                                ContentValues values = new ContentValues();
                                values.put(MediaStore.MediaColumns.DISPLAY_NAME, fileName);
                                values.put(MediaStore.MediaColumns.MIME_TYPE, mime);
                                values.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS);
                                values.put(MediaStore.MediaColumns.IS_PENDING, 1);

                                Uri uri = getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values);
                                if (uri == null) {
                                    throw new IOException("Unable to create MediaStore entry for " + fileName);
                                }
                                finalUri = uri;

                                try (InputStream in = conn.getInputStream();
                                     OutputStream out = getContentResolver().openOutputStream(uri)) {
                                    if (out == null) throw new IOException("Cannot open MediaStore output stream");
                                    byte[] buf = new byte[64 * 1024];
                                    int read;
                                    while ((read = in.read(buf)) != -1) {
                                        out.write(buf, 0, read);
                                        bytesReadTotal += read;

                                        long now = System.currentTimeMillis();
                                        long delta = now - lastProgressMillis;
                                        if (delta >= 300) {
                                            long deltaBytes = bytesReadTotal - lastSampleBytes;
                                            final double speedMBps = ((deltaBytes / (1024.0 * 1024.0)) / (delta / 1000.0));
                                            lastProgressMillis = now;
                                            lastSampleBytes = bytesReadTotal;

                                            final int progressPercent = contentLength > 0 ? (int)((bytesReadTotal * 100) / contentLength) : -1;
                                            final double curSpeed = speedMBps;
                                            final long curBytes = bytesReadTotal;
                                            final long totalBytes = contentLength;

                                            builder.setProgress(100, Math.max(0, progressPercent), progressPercent < 0);
                                            builder.setContentText(String.format(Locale.US, "%.1f MB/s • %s / %s",
                                                    curSpeed, formatBytes(curBytes), totalBytes > 0 ? formatBytes(totalBytes) : "?"));
                                            if (nm != null) {
                                                try { nm.notify(notifId, builder.build()); } catch (Exception ignored) {}
                                            }

                                            runOnUiThread(new Runnable() {
                                                @Override
                                                public void run() {
                                                    if (webView != null) {
                                                        String safe = fileName.replace("'", "\\'");
                                                        webView.evaluateJavascript(String.format(Locale.US,
                                                            "if (window.onDownloadProgress) window.onDownloadProgress('%s', %d, %.2f, %d, %d);",
                                                            safe, progressPercent, curSpeed, curBytes, totalBytes), null);
                                                    }
                                                }
                                            });
                                        }
                                    }
                                    out.flush();
                                }

                                values.clear();
                                values.put(MediaStore.MediaColumns.IS_PENDING, 0);
                                getContentResolver().update(uri, values, null, null);
                            } else {
                                File downloadDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS);
                                if (!downloadDir.exists()) downloadDir.mkdirs();

                                targetFile = new File(downloadDir, fileName);
                                finalUri = Uri.fromFile(targetFile);

                                try (InputStream in = conn.getInputStream();
                                     OutputStream out = new FileOutputStream(targetFile)) {
                                    byte[] buf = new byte[64 * 1024];
                                    int read;
                                    while ((read = in.read(buf)) != -1) {
                                        out.write(buf, 0, read);
                                        bytesReadTotal += read;

                                        long now = System.currentTimeMillis();
                                        long delta = now - lastProgressMillis;
                                        if (delta >= 300) {
                                            long deltaBytes = bytesReadTotal - lastSampleBytes;
                                            final double speedMBps = ((deltaBytes / (1024.0 * 1024.0)) / (delta / 1000.0));
                                            lastProgressMillis = now;
                                            lastSampleBytes = bytesReadTotal;

                                            final int progressPercent = contentLength > 0 ? (int)((bytesReadTotal * 100) / contentLength) : -1;
                                            final double curSpeed = speedMBps;
                                            final long curBytes = bytesReadTotal;
                                            final long totalBytes = contentLength;

                                            builder.setProgress(100, Math.max(0, progressPercent), progressPercent < 0);
                                            builder.setContentText(String.format(Locale.US, "%.1f MB/s • %s / %s",
                                                    curSpeed, formatBytes(curBytes), totalBytes > 0 ? formatBytes(totalBytes) : "?"));
                                            if (nm != null) {
                                                try { nm.notify(notifId, builder.build()); } catch (Exception ignored) {}
                                            }

                                            runOnUiThread(new Runnable() {
                                                @Override
                                                public void run() {
                                                    if (webView != null) {
                                                        String safe = fileName.replace("'", "\\'");
                                                        webView.evaluateJavascript(String.format(Locale.US,
                                                            "if (window.onDownloadProgress) window.onDownloadProgress('%s', %d, %.2f, %d, %d);",
                                                            safe, progressPercent, curSpeed, curBytes, totalBytes), null);
                                                    }
                                                }
                                            });
                                        }
                                    }
                                    out.flush();
                                }

                                Intent scanIntent = new Intent(Intent.ACTION_MEDIA_SCANNER_SCAN_FILE);
                                scanIntent.setData(Uri.fromFile(targetFile));
                                sendBroadcast(scanIntent);
                            }

                            // Notification on Complete
                            builder.setSmallIcon(android.R.drawable.stat_sys_download_done)
                                   .setContentTitle("Download Complete")
                                   .setContentText(fileName + " (" + formatBytes(bytesReadTotal) + ")")
                                   .setProgress(0, 0, false)
                                   .setOngoing(false)
                                   .setAutoCancel(true);

                            try {
                                Intent openIntent = new Intent(Intent.ACTION_VIEW);
                                openIntent.setDataAndType(finalUri, mime);
                                openIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                                int flags = PendingIntent.FLAG_UPDATE_CURRENT;
                                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                                    flags |= PendingIntent.FLAG_IMMUTABLE;
                                }
                                PendingIntent pi = PendingIntent.getActivity(MainActivity.this, notifId, openIntent, flags);
                                builder.setContentIntent(pi);
                            } catch (Exception ignored) { }

                            if (nm != null) {
                                try { nm.notify(notifId, builder.build()); } catch (Exception ignored) {}
                            }

                            final long finalBytes = bytesReadTotal;
                            runOnUiThread(new Runnable() {
                                @Override
                                public void run() {
                                    Toast.makeText(MainActivity.this, "Saved to Downloads: " + fileName, Toast.LENGTH_LONG).show();
                                    if (webView != null) {
                                        String safe = fileName.replace("'", "\\'");
                                        webView.evaluateJavascript(String.format(Locale.US,
                                            "if (window.onDownloadComplete) window.onDownloadComplete('%s', %d); if (window.showToast) window.showToast('Downloaded: %s');",
                                            safe, finalBytes, safe), null);
                                    }
                                }
                            });
                        } else {
                            final int errCode = code;
                            builder.setSmallIcon(android.R.drawable.stat_notify_error)
                                   .setContentTitle("Download Failed")
                                   .setContentText("HTTP " + errCode + " for " + fileName)
                                   .setProgress(0, 0, false)
                                   .setOngoing(false)
                                   .setAutoCancel(true);
                            if (nm != null) {
                                try { nm.notify(notifId, builder.build()); } catch (Exception ignored) {}
                            }

                            runOnUiThread(new Runnable() {
                                @Override
                                public void run() {
                                    Toast.makeText(MainActivity.this, "Download failed (HTTP " + errCode + ")", Toast.LENGTH_SHORT).show();
                                }
                            });
                        }
                    } catch (final Exception ex) {
                        builder.setSmallIcon(android.R.drawable.stat_notify_error)
                               .setContentTitle("Download Error")
                               .setContentText(fileName + ": " + ex.getMessage())
                               .setProgress(0, 0, false)
                               .setOngoing(false)
                               .setAutoCancel(true);
                        if (nm != null) {
                            try { nm.notify(notifId, builder.build()); } catch (Exception ignored) {}
                        }

                        runOnUiThread(new Runnable() {
                            @Override
                            public void run() {
                                Toast.makeText(MainActivity.this, "Download error: " + ex.getMessage(), Toast.LENGTH_LONG).show();
                            }
                        });
                    } finally {
                        if (conn != null) conn.disconnect();
                    }
                }
            }).start();
        }

        @JavascriptInterface
        public void pickAndUploadFiles(final String type) {
            runOnUiThread(new Runnable() {
                @Override
                public void run() {
                    Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
                    intent.addCategory(Intent.CATEGORY_OPENABLE);
                    if ("photos".equalsIgnoreCase(type) || "photo".equalsIgnoreCase(type) || "images".equalsIgnoreCase(type)) {
                        intent.setType("image/*");
                    } else {
                        intent.setType("*/*");
                    }
                    intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
                    try {
                        startActivityForResult(Intent.createChooser(intent, "Select Files to Send to PC"), NATIVE_UPLOAD_FILES_CODE);
                    } catch (Exception e) {
                        Toast.makeText(MainActivity.this, "Cannot open file picker: " + e.getMessage(), Toast.LENGTH_SHORT).show();
                    }
                }
            });
        }

        @JavascriptInterface
        public void openWith(final String fileUrl, final String mimeType, final String fileName) {
            runOnUiThread(new Runnable() {
                @Override
                public void run() {
                    try {
                        Intent intent = new Intent(Intent.ACTION_VIEW);
                        Uri uri = Uri.parse(fileUrl);
                        String type = (mimeType != null && !mimeType.isEmpty() && !mimeType.equals("application/octet-stream"))
                                ? mimeType : "*/*";
                        intent.setDataAndType(uri, type);
                        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                        startActivity(Intent.createChooser(intent, "Open " + (fileName != null ? fileName : "file") + " with"));
                    } catch (Exception e) {
                        Toast.makeText(MainActivity.this, "Cannot open: " + e.getMessage(), Toast.LENGTH_SHORT).show();
                    }
                }
            });
        }

        @JavascriptInterface
        public void requestAllPermissions() {
            runOnUiThread(new Runnable() {
                @Override
                public void run() {
                    if (Build.VERSION.SDK_INT >= 33) {
                        requestPermissions(new String[]{
                            "android.permission.READ_MEDIA_IMAGES",
                            "android.permission.READ_MEDIA_VIDEO",
                            "android.permission.READ_MEDIA_AUDIO",
                            "android.permission.POST_NOTIFICATIONS"
                        }, 101);
                    } else if (Build.VERSION.SDK_INT >= 23) {
                        requestPermissions(new String[]{
                            "android.permission.READ_EXTERNAL_STORAGE",
                            "android.permission.WRITE_EXTERNAL_STORAGE"
                        }, 101);
                    }
                }
            });
        }

        @JavascriptInterface
        public boolean hasStoragePermission() {
            if (Build.VERSION.SDK_INT >= 33) {
                return checkSelfPermission("android.permission.READ_MEDIA_IMAGES") == PackageManager.PERMISSION_GRANTED;
            } else if (Build.VERSION.SDK_INT >= 23) {
                return checkSelfPermission("android.permission.READ_EXTERNAL_STORAGE") == PackageManager.PERMISSION_GRANTED;
            }
            return true;
        }

        @JavascriptInterface
        public String getPhoneCategoryStats() {
            JSONObject obj = new JSONObject();
            try {
                // Images
                long imgBytes = 0; int imgCount = 0;
                Uri imgUri = MediaStore.Images.Media.EXTERNAL_CONTENT_URI;
                String[] proj = { MediaStore.MediaColumns.SIZE };
                try (Cursor c = getContentResolver().query(imgUri, proj, null, null, null)) {
                    if (c != null) {
                        imgCount = c.getCount();
                        int szIdx = c.getColumnIndex(MediaStore.MediaColumns.SIZE);
                        while (c.moveToNext()) {
                            if (szIdx >= 0) imgBytes += c.getLong(szIdx);
                        }
                    }
                }
                obj.put("imagesCount", imgCount);
                obj.put("imagesBytes", imgBytes);

                // Videos
                long vidBytes = 0; int vidCount = 0;
                Uri vidUri = MediaStore.Video.Media.EXTERNAL_CONTENT_URI;
                try (Cursor c = getContentResolver().query(vidUri, proj, null, null, null)) {
                    if (c != null) {
                        vidCount = c.getCount();
                        int szIdx = c.getColumnIndex(MediaStore.MediaColumns.SIZE);
                        while (c.moveToNext()) {
                            if (szIdx >= 0) vidBytes += c.getLong(szIdx);
                        }
                    }
                }
                obj.put("videosCount", vidCount);
                obj.put("videosBytes", vidBytes);

                // Audio
                long audBytes = 0; int audCount = 0;
                Uri audUri = MediaStore.Audio.Media.EXTERNAL_CONTENT_URI;
                try (Cursor c = getContentResolver().query(audUri, proj, null, null, null)) {
                    if (c != null) {
                        audCount = c.getCount();
                        int szIdx = c.getColumnIndex(MediaStore.MediaColumns.SIZE);
                        while (c.moveToNext()) {
                            if (szIdx >= 0) audBytes += c.getLong(szIdx);
                        }
                    }
                }
                obj.put("audioCount", audCount);
                obj.put("audioBytes", audBytes);

                // Downloads folder
                long dlBytes = 0; int dlCount = 0;
                File dlDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS);
                if (dlDir != null && dlDir.exists()) {
                    File[] dlFiles = dlDir.listFiles();
                    if (dlFiles != null) {
                        for (File f : dlFiles) {
                            if (f.isFile() && !f.getName().startsWith(".")) {
                                dlCount++;
                                dlBytes += f.length();
                            }
                        }
                    }
                }
                obj.put("downloadsCount", dlCount);
                obj.put("downloadsBytes", dlBytes);
            } catch (Exception ignored) { }
            return obj.toString();
        }

        @JavascriptInterface
        public String getPhoneDownloadsList() {
            JSONArray arr = new JSONArray();
            try {
                File dlDir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS);
                if (dlDir != null && dlDir.exists()) {
                    File[] files = dlDir.listFiles();
                    if (files != null) {
                        java.util.Arrays.sort(files, new java.util.Comparator<File>() {
                            @Override
                            public int compare(File a, File b) {
                                return Long.compare(b.lastModified(), a.lastModified());
                            }
                        });
                        int limit = Math.min(files.length, 60);
                        for (int i = 0; i < limit; i++) {
                            File f = files[i];
                            if (f.isFile() && !f.getName().startsWith(".")) {
                                JSONObject item = new JSONObject();
                                item.put("name", f.getName());
                                item.put("path", f.getAbsolutePath());
                                item.put("size", f.length());
                                item.put("modified", f.lastModified());
                                item.put("isLocal", true);
                                arr.put(item);
                            }
                        }
                    }
                }
            } catch (Exception ignored) { }
            return arr.toString();
        }

        @JavascriptInterface
        public void openLocalFile(final String path) {
            runOnUiThread(new Runnable() {
                @Override
                public void run() {
                    try {
                        File f = new File(path);
                        if (!f.exists()) {
                            Toast.makeText(MainActivity.this, "File not found: " + path, Toast.LENGTH_SHORT).show();
                            return;
                        }
                        String mime = URLConnection.guessContentTypeFromName(f.getName());
                        if (mime == null) mime = "*/*";
                        Intent intent = new Intent(Intent.ACTION_VIEW);
                        Uri uri;
                        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                            // Use direct file intent with grant or content URI
                            uri = Uri.fromFile(f);
                        } else {
                            uri = Uri.fromFile(f);
                        }
                        intent.setDataAndType(uri, mime);
                        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_GRANT_READ_URI_PERMISSION);
                        startActivity(Intent.createChooser(intent, "Open " + f.getName() + " with"));
                    } catch (Exception e) {
                        Toast.makeText(MainActivity.this, "Cannot open: " + e.getMessage(), Toast.LENGTH_SHORT).show();
                    }
                }
            });
        }

        @JavascriptInterface
        public void testPing(final String targetUrl) {
            new Thread(new Runnable() {
                @Override
                public void run() {
                    long start = System.currentTimeMillis();
                    int latency = -1;
                    try {
                        String clean = (targetUrl != null && !targetUrl.isEmpty()) ? targetUrl : serverBaseUrl;
                        if (clean.endsWith("/")) clean = clean.substring(0, clean.length() - 1);
                        URL url = new URL(clean + "/api/status");
                        HttpURLConnection conn = (HttpURLConnection) url.openConnection();
                        conn.setConnectTimeout(3000);
                        conn.setReadTimeout(3000);
                        conn.setRequestMethod("GET");
                        int code = conn.getResponseCode();
                        latency = (int)(System.currentTimeMillis() - start);
                        conn.disconnect();
                    } catch (Exception e) {
                        latency = -1;
                    }
                    final int finalLatency = latency;
                    runOnUiThread(new Runnable() {
                        @Override
                        public void run() {
                            if (webView != null) {
                                webView.evaluateJavascript("if (window.onPingResult) window.onPingResult(" + finalLatency + ");", null);
                            }
                        }
                    });
                }
            }).start();
        }
    }

    private void uploadUrisToPc(List<Uri> uris) {
        for (Uri uri : uris) {
            String fileName = "upload_" + System.currentTimeMillis();
            long fileSize = -1;
            try (Cursor cursor = getContentResolver().query(uri, null, null, null, null)) {
                if (cursor != null && cursor.moveToFirst()) {
                    int nameIdx = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                    int sizeIdx = cursor.getColumnIndex(OpenableColumns.SIZE);
                    if (nameIdx >= 0) fileName = cursor.getString(nameIdx);
                    if (sizeIdx >= 0) fileSize = cursor.getLong(sizeIdx);
                }
            } catch (Exception ignored) { }

            final String fName = fileName;
            final long fSize = fileSize;

            runOnUiThread(new Runnable() {
                @Override
                public void run() {
                    Toast.makeText(MainActivity.this, "Sending to PC: " + fName + "...", Toast.LENGTH_SHORT).show();
                    if (webView != null) {
                        webView.evaluateJavascript("if (window.showToast) window.showToast('Sending " + fName.replace("'", "\\'") + " to PC...');", null);
                    }
                }
            });

            HttpURLConnection conn = null;
            try {
                String base = (serverBaseUrl != null && !serverBaseUrl.isEmpty()) ? serverBaseUrl : "http://127.0.0.1:8090";
                String token = (pairingToken != null && !pairingToken.isEmpty()) ? pairingToken : "cloudssd2026";
                String uploadUrl = base + "/api/upload?filename=" + URLEncoder.encode(fName, "UTF-8") + "&token=" + URLEncoder.encode(token, "UTF-8");

                URL url = new URL(uploadUrl);
                conn = (HttpURLConnection) url.openConnection();
                conn.setRequestMethod("POST");
                conn.setDoOutput(true);
                conn.setConnectTimeout(15000);
                conn.setReadTimeout(60000);
                conn.setRequestProperty("Content-Type", "application/octet-stream");
                conn.setRequestProperty("X-Pairing-Token", token);
                conn.setRequestProperty("Authorization", "Bearer " + token);

                if (fSize > 0) {
                    conn.setFixedLengthStreamingMode(fSize);
                } else {
                    conn.setChunkedStreamingMode(64 * 1024);
                }

                try (InputStream in = getContentResolver().openInputStream(uri);
                     OutputStream out = conn.getOutputStream()) {
                    if (in == null) throw new IOException("Cannot open input stream for " + uri);
                    byte[] buffer = new byte[64 * 1024];
                    int bytesRead;
                    while ((bytesRead = in.read(buffer)) != -1) {
                        out.write(buffer, 0, bytesRead);
                    }
                    out.flush();
                }

                int code = conn.getResponseCode();
                if (code == 200) {
                    runOnUiThread(new Runnable() {
                        @Override
                        public void run() {
                            Toast.makeText(MainActivity.this, "Sent to PC: " + fName, Toast.LENGTH_LONG).show();
                            if (webView != null) {
                                String safeName = fName.replace("'", "\\'");
                                webView.evaluateJavascript("if (window.showToast) window.showToast('Sent to PC: " + safeName + "'); if (window.fetchTransferHistory) window.fetchTransferHistory(true);", null);
                            }
                        }
                    });
                } else {
                    final int failedCode = code;
                    runOnUiThread(new Runnable() {
                        @Override
                        public void run() {
                            Toast.makeText(MainActivity.this, "Upload failed (HTTP " + failedCode + ")", Toast.LENGTH_SHORT).show();
                        }
                    });
                }
            } catch (final Exception e) {
                runOnUiThread(new Runnable() {
                    @Override
                    public void run() {
                        Toast.makeText(MainActivity.this, "Upload error: " + e.getMessage(), Toast.LENGTH_SHORT).show();
                    }
                });
            } finally {
                if (conn != null) conn.disconnect();
            }
        }
    }

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        requestWindowFeature(Window.FEATURE_NO_TITLE);
        getWindow().setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN, WindowManager.LayoutParams.FLAG_FULLSCREEN);

        createNotificationChannel();

        if (Build.VERSION.SDK_INT >= 33) {
            if (checkSelfPermission("android.permission.POST_NOTIFICATIONS") != PackageManager.PERMISSION_GRANTED) {
                requestPermissions(new String[]{"android.permission.POST_NOTIFICATIONS"}, 102);
            }
        }

        webView = new WebView(this);
        setContentView(webView);

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
        settings.setUserAgentString(settings.getUserAgentString() + " CloudSSD-App/0.7.0");

        webView.setBackgroundColor(0xFF0D1117);
        webView.addJavascriptInterface(new AndroidBridge(), "AndroidBridge");

        webView.setWebChromeClient(new WebChromeClient() {
            @Override
            public boolean onShowFileChooser(WebView webView, ValueCallback<Uri[]> filePathCallback, FileChooserParams fileChooserParams) {
                if (uploadMessage != null) {
                    uploadMessage.onReceiveValue(null);
                    uploadMessage = null;
                }
                uploadMessage = filePathCallback;

                Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
                intent.addCategory(Intent.CATEGORY_OPENABLE);
                intent.setType("*/*");
                intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);

                try {
                    startActivityForResult(Intent.createChooser(intent, "Select Files to Send"), FILECHOOSER_RESULTCODE);
                } catch (Exception e) {
                    if (uploadMessage != null) {
                        uploadMessage.onReceiveValue(null);
                        uploadMessage = null;
                    }
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
        });

        webView.setDownloadListener(new android.webkit.DownloadListener() {
            @Override
            public void onDownloadStart(String url, String userAgent, String contentDisposition, String mimetype, long contentLength) {
                String guessName = URLUtil.guessFileName(url, contentDisposition, mimetype);
                new AndroidBridge().downloadFile(url, guessName);
            }
        });

        webView.loadUrl("file:///android_asset/index.html");
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent intent) {
        if (requestCode == NATIVE_UPLOAD_FILES_CODE) {
            if (resultCode == Activity.RESULT_OK && intent != null) {
                final List<Uri> urisToUpload = new ArrayList<>();
                if (intent.getData() != null) {
                    urisToUpload.add(intent.getData());
                } else if (intent.getClipData() != null) {
                    int count = intent.getClipData().getItemCount();
                    for (int i = 0; i < count; i++) {
                        urisToUpload.add(intent.getClipData().getItemAt(i).getUri());
                    }
                }
                if (!urisToUpload.isEmpty()) {
                    new Thread(new Runnable() {
                        @Override
                        public void run() {
                            uploadUrisToPc(urisToUpload);
                        }
                    }).start();
                }
            }
        } else if (requestCode == FILECHOOSER_RESULTCODE) {
            if (uploadMessage == null) return;
            Uri[] results = null;
            if (resultCode == Activity.RESULT_OK && intent != null) {
                if (intent.getData() != null) {
                    results = new Uri[]{intent.getData()};
                } else if (intent.getClipData() != null) {
                    int count = intent.getClipData().getItemCount();
                    results = new Uri[count];
                    for (int i = 0; i < count; i++) {
                        results[i] = intent.getClipData().getItemAt(i).getUri();
                    }
                }
            }
            uploadMessage.onReceiveValue(results);
            uploadMessage = null;
        } else {
            super.onActivityResult(requestCode, resultCode, intent);
        }
    }

    @Override
    public void onBackPressed() {
        if (webView != null) {
            webView.evaluateJavascript("window.onAndroidBack ? window.onAndroidBack() : false;", new ValueCallback<String>() {
                @Override
                public void onReceiveValue(String value) {
                    if (!"true".equalsIgnoreCase(value)) {
                        MainActivity.super.onBackPressed();
                    }
                }
            });
            return;
        }
        super.onBackPressed();
    }
}
