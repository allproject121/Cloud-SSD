package com.cloudssd.mobile;

import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioTrack;
import android.os.Build;

import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Plays the game's sound from the PC (16-bit stereo 48 kHz) with a small cushion of about 40 ms. Sound that piles up
 * beyond about 120 ms is thrown away, so it never drifts behind the picture.
 */
final class AudioPlayer {
    private static final int RATE = 48_000, BYTES_PER_MS = RATE * 4 / 1000;
    private static final int START_AFTER = 40 * BYTES_PER_MS, TOO_MUCH = 120 * BYTES_PER_MS;

    private final LinkedBlockingQueue<byte[]> queue = new LinkedBlockingQueue<>();
    private final AtomicInteger queued = new AtomicInteger();
    private final Thread thread;
    private volatile boolean released;

    AudioPlayer() {
        thread = new Thread(this::run, "game-audio");
        thread.setPriority(Thread.MAX_PRIORITY);
        thread.start();
    }

    void queue(byte[] buf, int offset, int length) {
        byte[] pcm = new byte[length];
        System.arraycopy(buf, offset, pcm, 0, length);
        queue.offer(pcm);
        if (queued.addAndGet(length) > TOO_MUCH) {
            // Behind: drop the oldest sound until only the cushion is left.
            byte[] old;
            while (queued.get() > START_AFTER && (old = queue.poll()) != null) queued.addAndGet(-old.length);
        }
    }

    void release() {
        released = true;
        thread.interrupt();
    }

    private void run() {
        int min = AudioTrack.getMinBufferSize(RATE, AudioFormat.CHANNEL_OUT_STEREO, AudioFormat.ENCODING_PCM_16BIT);
        AudioTrack.Builder b = new AudioTrack.Builder()
                .setAudioAttributes(new AudioAttributes.Builder()
                        .setUsage(AudioAttributes.USAGE_GAME)
                        .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build())
                .setAudioFormat(new AudioFormat.Builder()
                        .setSampleRate(RATE)
                        .setChannelMask(AudioFormat.CHANNEL_OUT_STEREO)
                        .setEncoding(AudioFormat.ENCODING_PCM_16BIT).build())
                .setBufferSizeInBytes(Math.max(min, 20 * BYTES_PER_MS))
                .setTransferMode(AudioTrack.MODE_STREAM);
        if (Build.VERSION.SDK_INT >= 26) b.setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY);
        AudioTrack track = b.build();
        boolean playing = false;
        try {
            while (!released) {
                if (!playing) {
                    // Fill the cushion first, so a late packet does not cut the sound.
                    if (queued.get() < START_AFTER) { Thread.sleep(5); continue; }
                    track.play();
                    playing = true;
                }
                byte[] pcm = queue.poll(100, TimeUnit.MILLISECONDS);
                if (pcm == null) {
                    // The game went quiet: refill the cushion before playing again.
                    track.pause();
                    track.flush();
                    playing = false;
                    continue;
                }
                queued.addAndGet(-pcm.length);
                track.write(pcm, 0, pcm.length);
            }
        } catch (InterruptedException ignored) {
        } finally {
            try { track.stop(); } catch (Exception ignored) { }
            track.release();
        }
    }
}
