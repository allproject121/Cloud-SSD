package com.cloudssd.mobile;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Color;
import android.graphics.pdf.PdfRenderer;
import android.os.ParcelFileDescriptor;
import android.util.LruCache;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.HashMap;
import java.util.Map;

/**
 * PDFs shown inside the app with Android's own PDF renderer: the file is fetched once into the app's cache (and
 * continued if the connection drops), then each page is drawn as a picture when the page scrolls to it.
 */
final class PdfPages {
    private static final long CACHE_LIMIT = 200L * 1024 * 1024;
    private static final Map<String, PdfRenderer> open = new HashMap<>();
    private static final LruCache<String, byte[]> pages = new LruCache<String, byte[]>(24 * 1024 * 1024) {
        @Override
        protected int sizeOf(String key, byte[] value) {
            return value.length;
        }
    };

    interface Events {
        void emit(JSONObject event);
    }

    private PdfPages() {
    }

    private static File dir(Context c) {
        File d = new File(c.getCacheDir(), "pdf");
        d.mkdirs();
        return d;
    }

    /**
     * Opens a PDF: {@code source} is a phone file path, "pc:" + a PC route, or "cloud:" + a cloud item id.
     * Sends {state: loading|ready|error} events; "ready" carries the key and every page's size.
     */
    static void openPdf(Context c, int reqId, String source, long size, Events ev) {
        try {
            File file;
            if (source.startsWith("pc:") || source.startsWith("cloud:")) {
                file = new File(dir(c), Integer.toHexString(source.hashCode()) + "-" + size + ".pdf");
                if (!file.exists() || (size > 0 && file.length() != size)) fetch(c, reqId, source, size, file, ev);
            } else file = new File(source);
            String key = file.getName();
            JSONArray sizes = new JSONArray();
            synchronized (open) {
                PdfRenderer r = open.get(key);
                if (r == null) {
                    r = new PdfRenderer(ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY));
                    open.put(key, r);
                }
                for (int i = 0; i < r.getPageCount(); i++) {
                    try (PdfRenderer.Page p = r.openPage(i)) {
                        sizes.put(new JSONObject().put("w", p.getWidth()).put("h", p.getHeight()));
                    }
                }
            }
            ev.emit(new JSONObject().put("reqId", reqId).put("state", "ready").put("key", key).put("pages", sizes));
            trim(c);
        } catch (SecurityException e) {
            send(ev, reqId, "This PDF is protected with a password; open it with another app.");
        } catch (Exception e) {
            send(ev, reqId, "This PDF could not be opened: " + e.getMessage());
        }
    }

    private static void send(Events ev, int reqId, String error) {
        try {
            ev.emit(new JSONObject().put("reqId", reqId).put("state", "error").put("error", error));
        } catch (Exception ignored) {
        }
    }

    private static void fetch(Context c, int reqId, String source, long size, File file, Events ev) throws Exception {
        File part = new File(file.getPath() + ".part");
        long have = part.exists() ? part.length() : 0;
        InputStream in;
        HttpURLConnection conn = null;
        if (source.startsWith("cloud:")) in = Backends.files(c).openRead(source.substring(6), have);
        else {
            String server = PcLink.resolve(c);
            if (server.isEmpty()) throw new IOException("PC not reachable");
            conn = (HttpURLConnection) new URL(server + source.substring(3)).openConnection();
            conn.setRequestProperty("X-Pairing-Token", TokenStore.pcToken(c));
            conn.setConnectTimeout(10000);
            conn.setReadTimeout(60000);
            if (have > 0) conn.setRequestProperty("Range", "bytes=" + have + "-");
            int code = conn.getResponseCode();
            if (code == 200) have = 0;
            else if (code != 206) throw new IOException("PC answered HTTP " + code);
            in = conn.getInputStream();
        }
        long got = have, last = 0;
        try (InputStream i = in; OutputStream o = new FileOutputStream(part, have > 0)) {
            byte[] buf = new byte[64 * 1024];
            for (int n; (n = i.read(buf)) > 0; ) {
                o.write(buf, 0, n);
                got += n;
                if (got - last > 256 * 1024) {
                    last = got;
                    ev.emit(new JSONObject().put("reqId", reqId).put("state", "loading").put("bytes", got).put("total", size));
                }
            }
        } finally {
            if (conn != null) conn.disconnect();
        }
        if (!part.renameTo(file)) throw new IOException("Cannot keep the PDF");
    }

    /** One page as a JPEG about {@code width} pixels wide (white paper, like a viewer shows it). */
    static byte[] page(String key, int index, int width) {
        String id = key + "|" + index + "|" + width;
        byte[] hit = pages.get(id);
        if (hit != null) return hit;
        synchronized (open) {
            PdfRenderer r = open.get(key);
            if (r == null || index < 0 || index >= r.getPageCount()) return null;
            try (PdfRenderer.Page p = r.openPage(index)) {
                int w = Math.max(200, Math.min(2400, width));
                int h = Math.max(1, (int) ((long) w * p.getHeight() / Math.max(1, p.getWidth())));
                Bitmap bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
                bmp.eraseColor(Color.WHITE);
                p.render(bmp, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                bmp.compress(Bitmap.CompressFormat.JPEG, 85, out);
                bmp.recycle();
                byte[] b = out.toByteArray();
                pages.put(id, b);
                return b;
            } catch (Exception | OutOfMemoryError e) {
                return null;
            }
        }
    }

    static void close(String key) {
        synchronized (open) {
            PdfRenderer r = open.remove(key);
            if (r != null) r.close();
        }
    }

    private static void trim(Context c) {
        File[] all = dir(c).listFiles();
        if (all == null) return;
        long size = 0;
        for (File f : all) size += f.length();
        if (size <= CACHE_LIMIT) return;
        java.util.Arrays.sort(all, (a, b) -> Long.compare(a.lastModified(), b.lastModified()));
        for (File f : all) {
            if (size <= CACHE_LIMIT * 3 / 4) break;
            synchronized (open) {
                if (open.containsKey(f.getName())) continue;
            }
            size -= f.length();
            f.delete();
        }
    }
}
