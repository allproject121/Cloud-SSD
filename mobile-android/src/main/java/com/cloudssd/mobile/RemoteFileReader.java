package com.cloudssd.mobile;

import android.os.Handler;
import android.os.HandlerThread;
import android.os.ProxyFileDescriptorCallback;
import android.system.ErrnoException;
import android.system.OsConstants;

import java.io.IOException;
import java.io.InputStream;

/**
 * Lets another app read a cloud file like a local one, including jumping around in it (video players, PDF and zip
 * viewers do). Android 8+ only. Reads are answered from one download that keeps running from where the app is
 * reading; a new request per block would cost about a second each. A jump elsewhere starts a new download there.
 * Every callback runs on this reader's own thread, so a slow file never blocks another open file.
 */
final class RemoteFileReader extends ProxyFileDescriptorCallback {
    /** Reading ahead this far is cheaper than starting a new download. */
    private static final long SKIP_MAX = 1024 * 1024;
    /** An app that keeps the file open without reading should not keep a download open on the PC. */
    private static final long IDLE_MS = 15000;

    private final CloudBackend backend;
    private final String id;
    private final long size;
    private final HandlerThread thread;
    final Handler handler;

    private InputStream in;
    private long position;
    private final byte[] skipBuffer = new byte[64 * 1024];
    private final Runnable idleClose = new Runnable() {
        @Override
        public void run() {
            closeStream();
        }
    };

    RemoteFileReader(CloudBackend backend, String id, long size) {
        this.backend = backend;
        this.id = id;
        this.size = size;
        thread = new HandlerThread("cloudssd-read");
        thread.start();
        handler = new Handler(thread.getLooper());
    }

    @Override
    public long onGetSize() {
        return size;
    }

    @Override
    public int onRead(long offset, int count, byte[] data) throws ErrnoException {
        handler.removeCallbacks(idleClose);
        try {
            try {
                return readAt(offset, count, data);
            } catch (IOException first) {
                closeStream(); // a dropped connection: try once more on a new one
                return readAt(offset, count, data);
            }
        } catch (IOException e) {
            closeStream();
            throw new ErrnoException("onRead", OsConstants.EIO);
        } finally {
            handler.postDelayed(idleClose, IDLE_MS);
        }
    }

    private int readAt(long offset, int count, byte[] data) throws IOException {
        if (offset >= size) return 0;
        if (in != null && offset > position && offset - position <= SKIP_MAX) {
            while (position < offset) {
                int n = in.read(skipBuffer, 0, (int) Math.min(skipBuffer.length, offset - position));
                if (n < 0) break;
                position += n;
            }
        }
        if (in == null || offset != position) {
            closeStream();
            in = backend.openRead(id, offset);
            position = offset;
        }
        int total = 0;
        while (total < count) {
            int n = in.read(data, total, count - total);
            if (n < 0) break;
            total += n;
        }
        position += total;
        // A short answer before the end of the file would look like "end of file" to the reading app.
        if (total < count && position < size) throw new IOException("The download stopped early.");
        return total;
    }

    private void closeStream() {
        if (in == null) return;
        try {
            in.close();
        } catch (IOException ignored) {
        }
        in = null;
    }

    @Override
    public void onRelease() {
        handler.removeCallbacks(idleClose);
        closeStream();
        thread.quitSafely();
    }
}
