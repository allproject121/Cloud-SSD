package com.cloudssd.mobile;

import android.content.Context;
import android.os.SystemClock;

import java.io.FilterInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.List;

import com.cloudssd.mobile.CloudBackend.BackendException;
import com.cloudssd.mobile.CloudBackend.DocInfo;
import com.cloudssd.mobile.CloudBackend.RootInfo;

/**
 * Picks where the cloud files come from: the PC when it answers (at home or through Tailscale), otherwise Google
 * Drive directly when the phone is signed in. The choice is kept for half a minute so the Files app does not
 * flip between the two on every request.
 */
final class Backends {
    private Backends() {
    }

    private static volatile CloudBackend files;

    static synchronized CloudBackend files(Context c) {
        if (files == null) files = new Routing(c.getApplicationContext());
        return files;
    }

    static CloudBackend drive(Context c) {
        return new DriveBackend(c);
    }

    /** Signed in to Google on this phone and allowed to use it when the PC is off. */
    static boolean driveReady(Context c) {
        return c.getSharedPreferences("cloudssd", Context.MODE_PRIVATE).getBoolean("drive_direct", true) && new DriveBackend(c).isConfigured();
    }

    /** Creates the folders of an id ("cloudssd:Phone Backup/Pixel/Camera") that do not exist yet. */
    static void ensureFolder(CloudBackend b, String folderId) throws IOException {
        int colon = folderId.indexOf(':');
        String rest = folderId.substring(colon + 1);
        String cur = folderId.substring(0, colon + 1);
        for (String seg : rest.split("/")) {
            if (seg.isEmpty()) continue;
            String next = cur + (cur.endsWith(":") ? "" : "/") + seg;
            try {
                if (!b.stat(next).dir) throw new BackendException(409, "A file named " + seg + " is in the way.");
            } catch (BackendException e) {
                if (e.status != 404) throw e;
                b.create(cur, seg, true);
            }
            cur = next;
        }
    }

    /** "PC" while the PC answers, "Google Drive" while the phone uses Drive directly, or "" when neither is set up. */
    static String source(Context c) {
        CloudBackend f = files(c);
        return f instanceof Routing ? ((Routing) f).current().label() : f.label();
    }

    private static final class Routing implements CloudBackend {
        private static final long STICKY_MS = 30_000;
        private final Context ctx;
        private final PcBackend pc;
        private volatile CloudBackend chosen;
        private volatile long chosenAt;

        Routing(Context c) {
            ctx = c;
            pc = new PcBackend(c);
        }

        CloudBackend current() {
            CloudBackend ch = chosen;
            if (ch != null && SystemClock.elapsedRealtime() - chosenAt < STICKY_MS) return ch;
            return pc.isConfigured() ? pc : driveReady(ctx) ? new DriveBackend(ctx) : pc;
        }

        private void use(CloudBackend b) {
            boolean changed = chosen == null || chosen.getClass() != b.getClass();
            chosen = b;
            chosenAt = SystemClock.elapsedRealtime();
            if (changed) CloudDocumentsProvider.notifyRootsChanged(ctx);
        }

        private boolean fallBack(IOException e) {
            return e instanceof BackendException
                    && (((BackendException) e).status == BackendException.UNREACHABLE || ((BackendException) e).status == BackendException.NOT_PAIRED)
                    && driveReady(ctx);
        }

        private interface Call<T> {
            T run(CloudBackend b) throws IOException;
        }

        private <T> T route(Call<T> call) throws IOException {
            CloudBackend first = current();
            try {
                T r = call.run(first);
                use(first);
                return r;
            } catch (IOException e) {
                if (first instanceof PcBackend && fallBack(e)) {
                    DriveBackend drive = new DriveBackend(ctx);
                    T r = call.run(drive);
                    use(drive);
                    return r;
                }
                throw e;
            }
        }

        @Override
        public boolean isConfigured() {
            return pc.isConfigured() || driveReady(ctx);
        }

        @Override
        public String label() {
            return current().label();
        }

        @Override
        public List<RootInfo> roots() throws IOException {
            return route(CloudBackend::roots);
        }

        @Override
        public List<DocInfo> list(String folderId) throws IOException {
            return route(b -> b.list(folderId));
        }

        @Override
        public DocInfo stat(String id) throws IOException {
            return route(b -> b.stat(id));
        }

        @Override
        public DocInfo create(String parentId, String name, boolean dir) throws IOException {
            return route(b -> b.create(parentId, name, dir));
        }

        @Override
        public DocInfo rename(String id, String name) throws IOException {
            return route(b -> b.rename(id, name));
        }

        @Override
        public DocInfo move(String id, String toFolderId) throws IOException {
            return route(b -> b.move(id, toFolderId));
        }

        @Override
        public void delete(String id) throws IOException {
            route(b -> {
                b.delete(id);
                return null;
            });
        }

        @Override
        public InputStream openRead(String id, long offset) throws IOException {
            return route(b -> b.openRead(id, offset));
        }

        /**
         * Falls back to Drive only when no byte of the data was read yet (the PC refused at connect time); after that
         * the source is spent and the error is real.
         */
        @Override
        public DocInfo write(String id, InputStream data) throws IOException {
            final long[] read = {0};
            InputStream counted = new FilterInputStream(data) {
                @Override
                public int read(byte[] b, int off, int len) throws IOException {
                    int n = super.read(b, off, len);
                    if (n > 0) read[0] += n;
                    return n;
                }
            };
            CloudBackend first = current();
            try {
                DocInfo d = first.write(id, counted);
                use(first);
                return d;
            } catch (IOException e) {
                if (read[0] == 0 && first instanceof PcBackend && fallBack(e)) {
                    DriveBackend drive = new DriveBackend(ctx);
                    DocInfo d = drive.write(id, counted);
                    use(drive);
                    return d;
                }
                throw e;
            }
        }

        @Override
        public InputStream thumbnail(String id, int px) throws IOException {
            return route(b -> b.thumbnail(id, px));
        }

        @Override
        public List<DocInfo> search(String folderId, String query, int max) throws IOException {
            return route(b -> b.search(folderId, query, max));
        }
    }
}
