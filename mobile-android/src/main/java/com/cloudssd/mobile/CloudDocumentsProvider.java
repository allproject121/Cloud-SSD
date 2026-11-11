package com.cloudssd.mobile;

import android.content.Context;
import android.content.SharedPreferences;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.os.Build;
import android.os.Bundle;
import android.os.CancellationSignal;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.ParcelFileDescriptor;
import android.os.SystemClock;
import android.os.storage.StorageManager;
import android.provider.DocumentsContract;
import android.provider.DocumentsContract.Document;
import android.provider.DocumentsContract.Root;
import android.provider.DocumentsProvider;
import android.webkit.MimeTypeMap;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileNotFoundException;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import com.cloudssd.mobile.CloudBackend.BackendException;
import com.cloudssd.mobile.CloudBackend.DocInfo;
import com.cloudssd.mobile.CloudBackend.RootInfo;

/**
 * Shows the cloud drives in Android's own Files app and in every app's "Open" / "Save" picker, so they can be used
 * like normal storage: open, save, new folder, rename, move, delete.
 *
 * A document id is "&lt;drive&gt;:&lt;folder/file&gt;" (see {@link CloudBackend}); it does not contain the PC's address
 * or drive letter, so links other apps keep stay valid when those change.
 */
public class CloudDocumentsProvider extends DocumentsProvider {
    static final String AUTHORITY = "com.cloudssd.mobile.documents";

    private static final String[] ROOT_COLUMNS = {
            Root.COLUMN_ROOT_ID, Root.COLUMN_DOCUMENT_ID, Root.COLUMN_TITLE, Root.COLUMN_SUMMARY, Root.COLUMN_FLAGS,
            Root.COLUMN_ICON, Root.COLUMN_AVAILABLE_BYTES, Root.COLUMN_CAPACITY_BYTES
    };
    private static final String[] DOCUMENT_COLUMNS = {
            Document.COLUMN_DOCUMENT_ID, Document.COLUMN_DISPLAY_NAME, Document.COLUMN_MIME_TYPE, Document.COLUMN_SIZE,
            Document.COLUMN_LAST_MODIFIED, Document.COLUMN_FLAGS
    };

    /** A folder's listing is reused this long; the Files app asks for the same folder and its items several times in a row. */
    private static final long LIST_TTL_MS = 15000;
    private static final long ROOTS_REFRESH_MS = 30000;
    /** Largest file an app may open for editing in place ("rw"): it is copied to the phone first. */
    private static final long EDIT_IN_PLACE_MAX = 512L * 1024 * 1024;
    private static final String CACHE_PREFS = "cloudssd_docs";

    private static final class Cached<T> {
        final T value;
        final long at = SystemClock.elapsedRealtime();

        Cached(T value) {
            this.value = value;
        }

        boolean fresh() {
            return SystemClock.elapsedRealtime() - at < LIST_TTL_MS;
        }
    }

    private CloudBackend backend;
    private final ExecutorService io = Executors.newCachedThreadPool();
    private Handler closeHandler;
    private static volatile long rootsRefreshedAt;

    private final Map<String, Cached<List<DocInfo>>> listings = new LinkedHashMap<String, Cached<List<DocInfo>>>(16, 0.75f, true) {
        @Override
        protected boolean removeEldestEntry(Map.Entry<String, Cached<List<DocInfo>>> eldest) {
            return size() > 30;
        }
    };
    private final Map<String, Cached<DocInfo>> documents = new HashMap<>();

    @Override
    public boolean onCreate() {
        // The PC when it answers (home or Tailscale), Google Drive directly when it does not.
        backend = Backends.files(getContext());
        HandlerThread t = new HandlerThread("cloudssd-docs");
        t.start();
        closeHandler = new Handler(t.getLooper());
        return true;
    }

    /** Tells the Files app that the list of drives may have changed (after pairing, or when a drive is connected). */
    static void notifyRootsChanged(Context context) {
        rootsRefreshedAt = 0; // ask the PC again on the next look, even if it was asked a moment ago
        context.getContentResolver().notifyChange(DocumentsContract.buildRootsUri(AUTHORITY), null);
    }

    // ---- ids -----------------------------------------------------------------------------------------------------

    private static boolean isRootDocument(String id) {
        return id.indexOf(':') == id.length() - 1;
    }

    private static String rootOf(String id) {
        int colon = id.indexOf(':');
        return colon < 0 ? id : id.substring(0, colon);
    }

    private static String parentOf(String id) {
        int slash = id.lastIndexOf('/');
        return slash >= 0 ? id.substring(0, slash) : id.substring(0, id.indexOf(':') + 1);
    }

    private static String nameOf(String id) {
        return id.substring(Math.max(id.lastIndexOf('/'), id.indexOf(':')) + 1);
    }

    private static FileNotFoundException notFound(IOException e) {
        FileNotFoundException f = new FileNotFoundException(e.getMessage());
        f.initCause(e);
        return f;
    }

    // ---- drives --------------------------------------------------------------------------------------------------

    /** The drives as last reported by the PC. Kept on the phone so the Files app never waits for the network here. */
    private List<RootInfo> cachedRoots() {
        List<RootInfo> roots = new ArrayList<>();
        SharedPreferences p = getContext().getSharedPreferences(CACHE_PREFS, Context.MODE_PRIVATE);
        try {
            JSONArray a = new JSONArray(p.getString("roots", "[]"));
            for (int i = 0; i < a.length(); i++) {
                JSONObject o = a.getJSONObject(i);
                RootInfo r = new RootInfo();
                r.id = o.getString("id");
                r.title = o.optString("title", r.id);
                r.readOnly = o.optBoolean("readOnly");
                r.canCreate = o.optBoolean("canCreate");
                r.totalBytes = o.optLong("total", -1);
                r.freeBytes = o.optLong("free", -1);
                roots.add(r);
            }
        } catch (JSONException ignored) {
        }
        return roots;
    }

    private static String toJson(List<RootInfo> roots) throws JSONException {
        JSONArray a = new JSONArray();
        for (RootInfo r : roots)
            a.put(new JSONObject().put("id", r.id).put("title", r.title).put("readOnly", r.readOnly).put("canCreate", r.canCreate)
                    .put("total", r.totalBytes).put("free", r.freeBytes));
        return a.toString();
    }

    private void refreshRootsSoon() {
        long now = SystemClock.elapsedRealtime();
        if (rootsRefreshedAt != 0 && now - rootsRefreshedAt < ROOTS_REFRESH_MS) return;
        rootsRefreshedAt = now;
        final Context context = getContext();
        io.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    String fresh = toJson(backend.roots());
                    SharedPreferences p = context.getSharedPreferences(CACHE_PREFS, Context.MODE_PRIVATE);
                    if (fresh.equals(p.getString("roots", "[]"))) return;
                    p.edit().putString("roots", fresh).apply();
                    context.getContentResolver().notifyChange(DocumentsContract.buildRootsUri(AUTHORITY), null);
                } catch (IOException | JSONException e) {
                    // PC off or not on this Wi-Fi: keep showing the drives known from last time.
                }
            }
        });
    }

    @Override
    public Cursor queryRoots(String[] projection) {
        MatrixCursor c = new MatrixCursor(projection != null ? projection : ROOT_COLUMNS);
        if (!backend.isConfigured()) return c;
        for (RootInfo r : cachedRoots()) {
            MatrixCursor.RowBuilder row = c.newRow();
            row.add(Root.COLUMN_ROOT_ID, r.id);
            row.add(Root.COLUMN_DOCUMENT_ID, r.id + ":");
            row.add(Root.COLUMN_TITLE, r.title);
            row.add(Root.COLUMN_SUMMARY, "Cloud-SSD · via " + backend.label());
            row.add(Root.COLUMN_FLAGS, Root.FLAG_SUPPORTS_IS_CHILD | Root.FLAG_SUPPORTS_SEARCH | (r.readOnly ? 0 : Root.FLAG_SUPPORTS_CREATE));
            row.add(Root.COLUMN_ICON, R.mipmap.ic_launcher);
            row.add(Root.COLUMN_AVAILABLE_BYTES, r.freeBytes >= 0 ? r.freeBytes : null);
            row.add(Root.COLUMN_CAPACITY_BYTES, r.totalBytes >= 0 ? r.totalBytes : null);
        }
        refreshRootsSoon();
        return c;
    }

    @Override
    public boolean isChildDocument(String parentDocumentId, String documentId) {
        if (parentDocumentId.equals(documentId)) return true;
        return documentId.startsWith(isRootDocument(parentDocumentId) ? parentDocumentId : parentDocumentId + "/");
    }

    // ---- looking ---------------------------------------------------------------------------------------------------

    private DocInfo rootDocument(String id) {
        DocInfo d = new DocInfo();
        d.id = id;
        d.name = rootOf(id);
        d.dir = true;
        for (RootInfo r : cachedRoots()) {
            if (!r.id.equalsIgnoreCase(d.name)) continue;
            d.name = r.title;
            d.canCreate = r.canCreate;
        }
        return d;
    }

    private List<DocInfo> children(String folderId) throws IOException {
        synchronized (listings) {
            Cached<List<DocInfo>> hit = listings.get(folderId);
            if (hit != null && hit.fresh()) return hit.value;
        }
        List<DocInfo> list = backend.list(folderId);
        synchronized (listings) {
            listings.put(folderId, new Cached<>(list));
            if (documents.size() > 5000) documents.clear();
            for (DocInfo d : list) documents.put(d.id, new Cached<>(d));
        }
        return list;
    }

    private DocInfo document(String id) throws IOException {
        if (isRootDocument(id)) return rootDocument(id);
        synchronized (listings) {
            Cached<DocInfo> hit = documents.get(id);
            if (hit != null && hit.fresh()) return hit.value;
        }
        DocInfo d = backend.stat(id);
        remember(d);
        return d;
    }

    private void remember(DocInfo d) {
        synchronized (listings) {
            documents.put(d.id, new Cached<>(d));
        }
    }

    /** Something in this folder changed: forget what we knew and let the Files app reload it. */
    private void changed(String folderId, String... goneIds) {
        synchronized (listings) {
            listings.remove(folderId);
            for (String id : goneIds) documents.remove(id);
        }
        getContext().getContentResolver().notifyChange(DocumentsContract.buildChildDocumentsUri(AUTHORITY, folderId), null);
    }

    private static String mimeOf(DocInfo d) {
        if (d.dir) return Document.MIME_TYPE_DIR;
        int dot = d.name.lastIndexOf('.');
        String mime = dot < 0 ? null : MimeTypeMap.getSingleton().getMimeTypeFromExtension(d.name.substring(dot + 1).toLowerCase(Locale.ROOT));
        return mime != null ? mime : "application/octet-stream";
    }

    private static void addRow(MatrixCursor c, DocInfo d) {
        int flags = 0;
        String mime = mimeOf(d);
        if (mime.startsWith("image/") || mime.startsWith("video/")) flags |= Document.FLAG_SUPPORTS_THUMBNAIL;
        if (d.dir && d.canCreate) flags |= Document.FLAG_DIR_SUPPORTS_CREATE;
        if (!d.dir && d.canEdit) flags |= Document.FLAG_SUPPORTS_WRITE;
        if (d.canEdit) flags |= Document.FLAG_SUPPORTS_DELETE | Document.FLAG_SUPPORTS_RENAME | Document.FLAG_SUPPORTS_MOVE;
        MatrixCursor.RowBuilder row = c.newRow();
        row.add(Document.COLUMN_DOCUMENT_ID, d.id);
        row.add(Document.COLUMN_DISPLAY_NAME, d.name);
        row.add(Document.COLUMN_MIME_TYPE, mimeOf(d));
        row.add(Document.COLUMN_SIZE, d.dir ? null : d.size);
        row.add(Document.COLUMN_LAST_MODIFIED, d.modified > 0 ? d.modified : null);
        row.add(Document.COLUMN_FLAGS, flags);
    }

    @Override
    public Cursor queryDocument(String documentId, String[] projection) throws FileNotFoundException {
        MatrixCursor c = new MatrixCursor(projection != null ? projection : DOCUMENT_COLUMNS);
        try {
            addRow(c, document(documentId));
        } catch (IOException e) {
            throw notFound(e);
        }
        return c;
    }

    @Override
    public Cursor queryChildDocuments(String parentDocumentId, String[] projection, String sortOrder) {
        MatrixCursor c = new MatrixCursor(projection != null ? projection : DOCUMENT_COLUMNS);
        c.setNotificationUri(getContext().getContentResolver(), DocumentsContract.buildChildDocumentsUri(AUTHORITY, parentDocumentId));
        try {
            for (DocInfo d : children(parentDocumentId)) addRow(c, d);
        } catch (IOException e) {
            // Shown by the Files app in place of the (empty) folder.
            Bundle extras = new Bundle();
            extras.putString(DocumentsContract.EXTRA_ERROR, e instanceof BackendException ? e.getMessage() : "Could not load this folder from the PC.");
            c.setExtras(extras);
        }
        return c;
    }

    // ---- changing --------------------------------------------------------------------------------------------------

    @Override
    public String createDocument(String parentDocumentId, String mimeType, String displayName) throws FileNotFoundException {
        boolean dir = Document.MIME_TYPE_DIR.equals(mimeType);
        String name = displayName;
        if (!dir && name.indexOf('.') < 0) {
            String ext = MimeTypeMap.getSingleton().getExtensionFromMimeType(mimeType);
            if (ext != null) name += "." + ext;
        }
        try {
            DocInfo d = backend.create(parentDocumentId, name, dir);
            remember(d);
            changed(parentDocumentId);
            return d.id;
        } catch (IOException e) {
            throw notFound(e);
        }
    }

    @Override
    public String renameDocument(String documentId, String displayName) throws FileNotFoundException {
        try {
            DocInfo d = backend.rename(documentId, displayName);
            remember(d);
            changed(parentOf(documentId), documentId);
            if (d.id.equals(documentId)) return null;
            revokeDocumentPermission(documentId);
            return d.id;
        } catch (IOException e) {
            throw notFound(e);
        }
    }

    @Override
    public String moveDocument(String sourceDocumentId, String sourceParentDocumentId, String targetParentDocumentId) throws FileNotFoundException {
        try {
            DocInfo d = backend.move(sourceDocumentId, targetParentDocumentId);
            remember(d);
            changed(parentOf(sourceDocumentId), sourceDocumentId);
            changed(targetParentDocumentId);
            revokeDocumentPermission(sourceDocumentId);
            return d.id;
        } catch (IOException e) {
            throw notFound(e);
        }
    }

    @Override
    public void deleteDocument(String documentId) throws FileNotFoundException {
        try {
            backend.delete(documentId);
            synchronized (listings) {
                listings.remove(documentId);
            }
            changed(parentOf(documentId), documentId);
            revokeDocumentPermission(documentId);
        } catch (IOException e) {
            throw notFound(e);
        }
    }

    // ---- thumbnails and search -------------------------------------------------------------------------------------

    /** Thumbnails are kept on the phone (64 MB at most), so scrolling back through a folder does not fetch them again. */
    @Override
    public android.content.res.AssetFileDescriptor openDocumentThumbnail(String documentId, android.graphics.Point sizeHint, CancellationSignal signal)
            throws FileNotFoundException {
        int px = Math.max(96, Math.min(512, sizeHint == null ? 256 : Math.max(sizeHint.x, sizeHint.y)));
        File dir = new File(getContext().getCacheDir(), "doc-thumbs");
        File file;
        try {
            DocInfo d = document(documentId);
            file = new File(dir, Integer.toHexString((documentId + "|" + d.modified + "|" + d.size + "|" + px).hashCode()) + ".jpg");
            if (!file.exists()) {
                try (InputStream in = backend.thumbnail(documentId, px)) {
                    if (in == null) throw new FileNotFoundException("No thumbnail");
                    dir.mkdirs();
                    File tmp = new File(dir, file.getName() + ".tmp");
                    try (OutputStream out = new FileOutputStream(tmp)) {
                        copy(in, out);
                    }
                    if (!tmp.renameTo(file)) throw new FileNotFoundException("No thumbnail");
                }
                trimThumbs(dir);
            }
        } catch (IOException e) {
            throw notFound(e);
        }
        return new android.content.res.AssetFileDescriptor(ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY), 0,
                android.content.res.AssetFileDescriptor.UNKNOWN_LENGTH);
    }

    private static void trimThumbs(File dir) {
        File[] all = dir.listFiles();
        if (all == null) return;
        long size = 0;
        for (File f : all) size += f.length();
        if (size < 64L * 1024 * 1024) return;
        java.util.Arrays.sort(all, (a, b) -> Long.compare(a.lastModified(), b.lastModified()));
        for (File f : all) {
            if (size < 48L * 1024 * 1024) break;
            size -= f.length();
            f.delete();
        }
    }

    @Override
    public Cursor querySearchDocuments(String rootId, String query, String[] projection) throws FileNotFoundException {
        MatrixCursor c = new MatrixCursor(projection != null ? projection : DOCUMENT_COLUMNS);
        try {
            for (DocInfo d : backend.search(rootId + ":", query, 100)) {
                remember(d);
                addRow(c, d);
            }
        } catch (IOException e) {
            Bundle extras = new Bundle();
            extras.putString(DocumentsContract.EXTRA_ERROR, e.getMessage());
            c.setExtras(extras);
        }
        return c;
    }

    // ---- opening ---------------------------------------------------------------------------------------------------

    @Override
    public ParcelFileDescriptor openDocument(String documentId, String mode, CancellationSignal signal) throws FileNotFoundException {
        try {
            if ("r".equals(mode)) return openForRead(documentId);
            if ("w".equals(mode) || "wt".equals(mode)) return openForWrite(documentId);
            return openForEdit(documentId, mode);
        } catch (IOException e) {
            throw notFound(e);
        }
    }

    private ParcelFileDescriptor openForRead(final String id) throws IOException {
        if (Build.VERSION.SDK_INT >= 26) {
            RemoteFileReader reader = new RemoteFileReader(backend, id, document(id).size);
            StorageManager storage = (StorageManager) getContext().getSystemService(Context.STORAGE_SERVICE);
            return storage.openProxyFileDescriptor(ParcelFileDescriptor.MODE_READ_ONLY, reader, reader.handler);
        }
        // Android 7: the file can only be read from start to end.
        final InputStream in = backend.openRead(id, 0);
        final ParcelFileDescriptor[] pipe = ParcelFileDescriptor.createReliablePipe();
        io.execute(new Runnable() {
            @Override
            public void run() {
                try (OutputStream out = new ParcelFileDescriptor.AutoCloseOutputStream(pipe[1])) {
                    copy(in, out);
                } catch (IOException e) {
                    try {
                        pipe[1].closeWithError(e.getMessage());
                    } catch (IOException ignored) {
                    }
                } finally {
                    closeQuietly(in);
                }
            }
        });
        return pipe[0];
    }

    /** The app writes into a pipe and the bytes go straight to the PC, so even a large video needs no room on the phone. */
    private ParcelFileDescriptor openForWrite(final String id) throws IOException {
        final ParcelFileDescriptor[] pipe = ParcelFileDescriptor.createReliablePipe();
        io.execute(new Runnable() {
            @Override
            public void run() {
                // At the end of the data, make sure the app really finished: if it crashed or gave up, reading fails
                // here and the PC keeps the old file instead of half a new one.
                InputStream in = new ParcelFileDescriptor.AutoCloseInputStream(pipe[0]) {
                    @Override
                    public int read(byte[] b, int off, int len) throws IOException {
                        int n = super.read(b, off, len);
                        if (n < 0) pipe[0].checkError();
                        return n;
                    }
                };
                try {
                    remember(backend.write(id, in));
                    changed(parentOf(id));
                } catch (IOException e) {
                    try {
                        pipe[0].closeWithError(e.getMessage());
                    } catch (IOException ignored) {
                    }
                } finally {
                    closeQuietly(in);
                }
            }
        });
        return pipe[1];
    }

    /**
     * Read-and-write access ("rw", "rwt", "wa"): the app needs a real file it can jump around in, so it gets a copy on
     * the phone, which is sent to the PC when the app closes it.
     */
    private ParcelFileDescriptor openForEdit(final String id, String mode) throws IOException {
        final File copy = File.createTempFile("edit", ".tmp", getContext().getCacheDir());
        try {
            if (mode.indexOf('t') < 0) {
                if (document(id).size > EDIT_IN_PLACE_MAX) throw new IOException("This file is too large to be edited in place.");
                try (InputStream in = backend.openRead(id, 0); OutputStream out = new FileOutputStream(copy)) {
                    copy(in, out);
                }
            }
            return ParcelFileDescriptor.open(copy, ParcelFileDescriptor.parseMode(mode), closeHandler, new ParcelFileDescriptor.OnCloseListener() {
                @Override
                public void onClose(final IOException failed) {
                    io.execute(new Runnable() {
                        @Override
                        public void run() {
                            try {
                                if (failed != null) return; // the app died while writing: keep the old file
                                try (InputStream in = new FileInputStream(copy)) {
                                    remember(backend.write(id, in));
                                }
                                changed(parentOf(id));
                            } catch (IOException ignored) {
                            } finally {
                                copy.delete();
                            }
                        }
                    });
                }
            });
        } catch (IOException | RuntimeException e) {
            copy.delete();
            throw e;
        }
    }

    private static void copy(InputStream in, OutputStream out) throws IOException {
        byte[] buf = new byte[64 * 1024];
        for (int n; (n = in.read(buf)) >= 0; ) out.write(buf, 0, n);
    }

    private static void closeQuietly(InputStream in) {
        try {
            in.close();
        } catch (IOException ignored) {
        }
    }
}
