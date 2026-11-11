package com.cloudssd.mobile;

import android.content.Context;
import android.os.SystemClock;
import android.view.Surface;

import org.json.JSONObject;

import java.io.BufferedInputStream;
import java.io.ByteArrayOutputStream;
import java.io.DataInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.net.URL;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;

/**
 * The phone's side of "Play on phone": asks the PC to open the game (/api/game/start), then switches one connection
 * to the PC's stream format (/api/game/stream): pictures go to {@link VideoPlayer}, sound to {@link AudioPlayer},
 * fingers and pings go back. Message layout: [type 1][length 4, little-endian][payload], as in the PC's
 * StreamProtocol.
 */
final class StreamClient {
    interface Listener {
        void onInfo(JSONObject info);
        void onStatus(String text);
        void onStats(int fps, int pingMs, int kbps);
        void onEnded(String reason);
    }

    static final byte DOWN = 1, MOVE = 2, UP = 3;
    private static final byte VIDEO = 1, AUDIO = 2, INFO = 3, PONG = 4;
    private static final byte TOUCH = 16, PING = 17, NEED_KEYFRAME = 18, BYE = 19;
    private static final int MAX_MESSAGE = 8 * 1024 * 1024;

    private final Context context;
    private final Surface surface;
    private final Listener listener;
    private volatile boolean closed;
    private volatile Socket socket;
    private OutputStream out;
    private VideoPlayer video;
    private AudioPlayer audio;
    private int frames, bytes, pingMs = -1;
    private long statsSince;

    StreamClient(Context context, Surface surface, Listener listener) {
        this.context = context.getApplicationContext();
        this.surface = surface;
        this.listener = listener;
    }

    void start() {
        Thread t = new Thread(this::run, "game-stream");
        t.setPriority(Thread.MAX_PRIORITY);
        t.start();
    }

    void close() {
        if (closed) return;
        closed = true;
        new Thread(() -> {
            send(BYE, new byte[0]);
            try { if (socket != null) socket.close(); } catch (IOException ignored) { }
        }).start();
    }

    private void run() {
        String reason = null;
        try {
            String server = PcLink.resolve(context);
            String token = TokenStore.pcToken(context);
            if (server.isEmpty() || token.isEmpty()) throw new IOException("PC nahi mila. Phone aur PC ek hi Wi-Fi par hon, aur Cloud-SSD PC par khula ho.");
            startGame(server, token);
            if (closed) return;
            listener.onStatus("Jud raha hai…");
            connect(server, token);
            video = new VideoPlayer(surface, () -> send(NEED_KEYFRAME, new byte[0]));
            audio = new AudioPlayer();
            Thread pinger = new Thread(this::pingLoop, "game-ping");
            pinger.start();
            readLoop(new DataInputStream(new BufferedInputStream(socket.getInputStream(), 256 * 1024)));
            if (!closed) reason = "PC ne game band kar diya.";
        } catch (Exception e) {
            if (!closed) reason = e.getMessage() != null ? e.getMessage() : "PC se connection toot gaya.";
        } finally {
            closed = true;
            try { if (socket != null) socket.close(); } catch (IOException ignored) { }
            if (video != null) video.release();
            if (audio != null) audio.release();
            if (reason != null) listener.onEnded(reason);
        }
    }

    /** Opens the game on the PC (or brings it back if it is already open) and waits for its window. */
    private void startGame(String server, String token) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(server + "/api/game/start").openConnection();
        c.setConnectTimeout(5000);
        c.setReadTimeout(75_000);
        c.setRequestMethod("POST");
        c.setRequestProperty("X-Pairing-Token", token);
        c.setRequestProperty("Connection", "close");
        c.setDoOutput(true);
        c.getOutputStream().close();
        int code = c.getResponseCode();
        String body = readAll(code < 400 ? c.getInputStream() : c.getErrorStream());
        c.disconnect();
        if (code != 200) throw new IOException(errorText(body, "PC par game nahi khula (" + code + ")."));
    }

    private void connect(String server, String token) throws IOException {
        URL url = new URL(server);
        int port = url.getPort() > 0 ? url.getPort() : 8090;
        Socket s = new Socket();
        s.setTcpNoDelay(true);
        s.setReceiveBufferSize(1024 * 1024);
        s.connect(new InetSocketAddress(url.getHost(), port), 5000);
        socket = s;
        out = s.getOutputStream();
        String hello = "GET /api/game/stream HTTP/1.1\r\nHost: " + url.getHost() + ":" + port + "\r\nX-Pairing-Token: " + token
                + "\r\nConnection: Upgrade\r\nUpgrade: cloudssd-game\r\n\r\n";
        out.write(hello.getBytes(StandardCharsets.US_ASCII));
        out.flush();
        // Read the answer's header; anything but 101 carries a JSON reason.
        InputStream in = s.getInputStream();
        ByteArrayOutputStream head = new ByteArrayOutputStream();
        int b, last4 = 0;
        while (last4 != 0x0D0A0D0A && (b = in.read()) >= 0) {
            head.write(b);
            last4 = (last4 << 8) | b;
            if (head.size() > 16 * 1024) throw new IOException("PC ka jawab samajh nahi aaya.");
        }
        String header = head.toString("US-ASCII");
        if (!header.startsWith("HTTP/1.1 101")) {
            String body = readAll(in);
            throw new IOException(errorText(body, "PC ne game stream mana kar diya: " + header.split("\r\n")[0]));
        }
    }

    private void readLoop(DataInputStream in) throws IOException, InterruptedException {
        byte[] header = new byte[5];
        statsSince = SystemClock.elapsedRealtime();
        while (!closed) {
            in.readFully(header);
            int type = header[0];
            int length = ByteBuffer.wrap(header, 1, 4).order(ByteOrder.LITTLE_ENDIAN).getInt();
            if (length < 0 || length > MAX_MESSAGE) throw new IOException("Bad message from the PC");
            byte[] payload = new byte[length];
            in.readFully(payload);
            switch (type) {
                case VIDEO:
                    if (length < 9) break;
                    ByteBuffer v = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN);
                    long captured = v.getLong();
                    boolean key = payload[8] == 1;
                    video.queue(payload, 9, length - 9, captured, key);
                    frames++;
                    bytes += length;
                    break;
                case AUDIO:
                    if (length > 8) audio.queue(payload, 8, length - 8);
                    break;
                case INFO:
                    try { listener.onInfo(new JSONObject(new String(payload, StandardCharsets.UTF_8))); } catch (Exception ignored) { }
                    break;
                case PONG:
                    if (length >= 8) {
                        long sent = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN).getLong();
                        pingMs = (int) ((SystemClock.elapsedRealtimeNanos() / 1000 - sent) / 1000);
                    }
                    break;
            }
            long now = SystemClock.elapsedRealtime();
            if (now - statsSince >= 1000) {
                listener.onStats((int) (frames * 1000L / (now - statsSince)), Math.max(pingMs, 0), (int) (bytes * 8L / (now - statsSince)));
                frames = 0;
                bytes = 0;
                statsSince = now;
            }
        }
    }

    private void pingLoop() {
        while (!closed) {
            byte[] p = ByteBuffer.allocate(8).order(ByteOrder.LITTLE_ENDIAN).putLong(SystemClock.elapsedRealtimeNanos() / 1000).array();
            send(PING, p);
            SystemClock.sleep(1000);
        }
    }

    void sendTouch(byte[] ids, byte[] states, float[] xs, float[] ys) {
        int n = ids.length;
        ByteBuffer b = ByteBuffer.allocate(1 + n * 6).order(ByteOrder.LITTLE_ENDIAN);
        b.put((byte) n);
        for (int i = 0; i < n; i++) {
            b.put(ids[i]).put(states[i]);
            b.putShort((short) Math.round(Math.max(0f, Math.min(1f, xs[i])) * 65535f));
            b.putShort((short) Math.round(Math.max(0f, Math.min(1f, ys[i])) * 65535f));
        }
        send(TOUCH, b.array());
    }

    private synchronized void send(byte type, byte[] payload) {
        OutputStream o = out;
        if (o == null) return;
        try {
            byte[] msg = new byte[5 + payload.length];
            msg[0] = type;
            ByteBuffer.wrap(msg, 1, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(payload.length);
            System.arraycopy(payload, 0, msg, 5, payload.length);
            o.write(msg);
            o.flush();
        } catch (IOException ignored) {
            // The read loop notices the closed connection and ends the session.
        }
    }

    private static String readAll(InputStream in) {
        if (in == null) return "";
        try {
            ByteArrayOutputStream b = new ByteArrayOutputStream();
            byte[] buf = new byte[4096];
            int n;
            while ((n = in.read(buf)) > 0 && b.size() < 64 * 1024) b.write(buf, 0, n);
            return b.toString("UTF-8");
        } catch (IOException e) {
            return "";
        }
    }

    private static String errorText(String body, String fallback) {
        try {
            String e = new JSONObject(body).optString("error", "");
            return e.isEmpty() ? fallback : e;
        } catch (Exception e) {
            return fallback;
        }
    }
}
