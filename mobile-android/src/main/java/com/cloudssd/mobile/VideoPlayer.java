package com.cloudssd.mobile;

import android.media.MediaCodec;
import android.media.MediaFormat;
import android.os.Build;
import android.view.Surface;

import java.nio.ByteBuffer;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;

/**
 * Shows the PC's H.264 pictures on the screen as soon as they are decoded: the phone's hardware decoder in
 * low-latency mode, set up from the first keyframe's SPS/PPS, every picture rendered the moment it comes out. If the
 * decoder falls behind or fails, waiting pictures are dropped and the PC is asked for a keyframe to start clean.
 */
final class VideoPlayer {
    private static final class Unit {
        final byte[] data; final long time; final boolean key;
        Unit(byte[] data, long time, boolean key) { this.data = data; this.time = time; this.key = key; }
    }

    private final Surface surface;
    private final Runnable askKeyframe;
    private final ArrayBlockingQueue<Unit> queue = new ArrayBlockingQueue<>(8);
    private final Thread thread;
    private volatile boolean released;
    private MediaCodec codec;
    private volatile boolean waitForKey = true;

    VideoPlayer(Surface surface, Runnable askKeyframe) {
        this.surface = surface;
        this.askKeyframe = askKeyframe;
        thread = new Thread(this::run, "game-video");
        thread.setPriority(Thread.MAX_PRIORITY);
        thread.start();
    }

    void queue(byte[] buf, int offset, int length, long captureMicros, boolean key) {
        byte[] au = new byte[length];
        System.arraycopy(buf, offset, au, 0, length);
        if (!queue.offer(new Unit(au, captureMicros, key))) {
            // The decoder is behind: what waits is stale. Start again from a keyframe.
            queue.clear();
            waitForKey = true;
            askKeyframe.run();
        }
    }

    void release() {
        released = true;
        thread.interrupt();
    }

    private void run() {
        MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
        int pending = 0;   // pictures given to the decoder that have not come out yet
        try {
            while (!released) {
                // While a picture is inside the decoder, look for it often; otherwise just wait for the next one.
                Unit u = queue.poll(pending > 0 ? 1 : 20, TimeUnit.MILLISECONDS);
                if (u != null) {
                    if (waitForKey && !u.key) continue;
                    if (codec == null && !open(u.data)) continue;
                    waitForKey = false;
                    int in = codec.dequeueInputBuffer(10_000);
                    if (in < 0) { waitForKey = true; askKeyframe.run(); continue; }
                    ByteBuffer b = codec.getInputBuffer(in);
                    b.clear();
                    b.put(u.data);
                    codec.queueInputBuffer(in, 0, u.data.length, u.time, 0);
                    pending++;
                }
                if (codec == null || pending == 0) continue;
                // Show only the newest decoded picture; older ones that came out together are skipped.
                int newest = -1, out;
                while ((out = codec.dequeueOutputBuffer(info, newest < 0 && u != null ? 4_000 : 0)) >= 0
                        || out == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED || out == MediaCodec.INFO_OUTPUT_BUFFERS_CHANGED) {
                    if (out < 0) continue;
                    if (newest >= 0) codec.releaseOutputBuffer(newest, false);
                    newest = out;
                    pending = Math.max(0, pending - 1);
                }
                if (newest >= 0) codec.releaseOutputBuffer(newest, true);
            }
        } catch (InterruptedException ignored) {
        } catch (Exception e) {
            // A decoder that broke cannot be trusted; the session ends with a clear message from the reader instead.
        } finally {
            if (codec != null) {
                try { codec.stop(); } catch (Exception ignored) { }
                codec.release();
            }
        }
    }

    private boolean open(byte[] keyframe) {
        byte[][] params = SpsPps.find(keyframe);
        if (params == null) return false;
        try {
            MediaFormat f = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, 1280, 720);
            f.setByteBuffer("csd-0", ByteBuffer.wrap(params[0]));
            f.setByteBuffer("csd-1", ByteBuffer.wrap(params[1]));
            if (Build.VERSION.SDK_INT >= 30) f.setInteger(MediaFormat.KEY_LOW_LATENCY, 1);
            f.setInteger("vendor.qti-ext-dec-low-latency.enable", 1);   // Snapdragon's own switch
            f.setInteger(MediaFormat.KEY_PRIORITY, 0);                   // real time
            f.setInteger(MediaFormat.KEY_OPERATING_RATE, Short.MAX_VALUE);
            codec = MediaCodec.createDecoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
            codec.configure(f, surface, null, 0);
            codec.start();
            return true;
        } catch (Exception e) {
            if (codec != null) codec.release();
            codec = null;
            return false;
        }
    }

    /** Finds the SPS and PPS (with their start codes) in an Annex-B access unit. */
    static final class SpsPps {
        static byte[][] find(byte[] au) {
            byte[] sps = null, pps = null;
            int i = 0;
            while ((i = next(au, i)) >= 0) {
                int start = i;
                int end = next(au, i + 3);
                int nalStart = au[start + 2] == 1 ? start + 3 : start + 4;
                int type = au[nalStart] & 0x1F;
                int stop = end < 0 ? au.length : end;
                byte[] nal = new byte[stop - start];
                System.arraycopy(au, start, nal, 0, nal.length);
                if (type == 7) sps = nal;
                else if (type == 8) pps = nal;
                if (end < 0) break;
                i = end;
            }
            return sps != null && pps != null ? new byte[][] { sps, pps } : null;
        }

        /** Index of the next 00 00 01 or 00 00 00 01 at or after <code>from</code>, or -1. */
        private static int next(byte[] b, int from) {
            for (int i = from; i + 3 < b.length; i++) {
                if (b[i] == 0 && b[i + 1] == 0) {
                    if (b[i + 2] == 1) return i;
                    if (b[i + 2] == 0 && b[i + 3] == 1) return i;
                }
            }
            return -1;
        }
    }
}
