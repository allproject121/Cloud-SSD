package com.cloudssd.mobile;

import android.app.Activity;
import android.app.AlertDialog;
import android.graphics.Color;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.widget.FrameLayout;
import android.widget.TextView;

import org.json.JSONObject;

/**
 * "Play on phone": the PC's game fills this screen, its sound plays here, and fingers on the screen play the game on
 * the PC. Starts the game on the PC first, then keeps one connection open for picture, sound and touches
 * ({@link StreamClient}). Back asks before it ends.
 */
public class StreamActivity extends Activity implements StreamClient.Listener {
    private final Handler ui = new Handler(Looper.getMainLooper());
    private FrameLayout root;
    private SurfaceView surface;
    private TextView status, message;
    private StreamClient client;
    private int videoW = 1280, videoH = 720;
    private boolean showStats = true;
    private volatile int pcFps = -1;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        requestWindowFeature(Window.FEATURE_NO_TITLE);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON | WindowManager.LayoutParams.FLAG_FULLSCREEN);
        if (Build.VERSION.SDK_INT >= 28) {
            getWindow().getAttributes().layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
        }
        root = new FrameLayout(this);
        root.setBackgroundColor(Color.BLACK);
        surface = new SurfaceView(this);
        root.addView(surface, new FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT, Gravity.CENTER));
        status = label(12, 0x99FFFFFF);
        status.setPadding(24, 12, 24, 12);
        status.setOnClickListener(v -> { showStats = !showStats; status.setAlpha(showStats ? 1f : 0.25f); });
        root.addView(status, new FrameLayout.LayoutParams(FrameLayout.LayoutParams.WRAP_CONTENT, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.TOP | Gravity.START));
        message = label(18, Color.WHITE);
        message.setBackgroundColor(0xB0000000);
        message.setPadding(48, 32, 48, 32);
        root.addView(message, new FrameLayout.LayoutParams(FrameLayout.LayoutParams.WRAP_CONTENT, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.CENTER));
        setContentView(root);
        hideSystemBars();
        root.addOnLayoutChangeListener((v, l, t, r, b, ol, ot, or, ob) -> fitSurface());

        surface.setOnTouchListener((v, e) -> { onFingers(e); return true; });
        surface.getHolder().addCallback(new SurfaceHolder.Callback() {
            @Override public void surfaceCreated(SurfaceHolder h) { start(h); }
            @Override public void surfaceChanged(SurfaceHolder h, int f, int w, int hh) { }
            @Override public void surfaceDestroyed(SurfaceHolder h) { stop(); }
        });
        showMessage("PC par game khul raha hai…");
    }

    private TextView label(int sp, int color) {
        TextView t = new TextView(this);
        t.setTextSize(sp);
        t.setTextColor(color);
        t.setShadowLayer(4, 0, 0, Color.BLACK);
        return t;
    }

    private void start(SurfaceHolder holder) {
        if (client != null) return;
        client = new StreamClient(this, holder.getSurface(), this);
        client.start();
    }

    private void stop() {
        if (client != null) {
            client.close();
            client = null;
        }
    }

    @Override
    protected void onPause() {
        super.onPause();
        // Leaving the screen ends the game session; the game keeps running on the PC.
        if (isFinishing()) stop();
    }

    @Override
    protected void onDestroy() {
        stop();
        super.onDestroy();
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) hideSystemBars();
    }

    @SuppressWarnings("deprecation")
    private void hideSystemBars() {
        root.setSystemUiVisibility(View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY | View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN);
    }

    @Override
    @SuppressWarnings("deprecation")
    public void onBackPressed() {
        new AlertDialog.Builder(this)
                .setMessage("Game band karein? Game PC par chalta rahega.")
                .setPositiveButton("Band karo", (d, w) -> finish())
                .setNegativeButton("Khelte raho", null)
                .show();
    }

    /** The picture keeps its shape: as large as the screen allows, black bars on the sides if needed. */
    private void fitSurface() {
        int w = root.getWidth(), h = root.getHeight();
        if (w == 0 || h == 0) return;
        int sw = w, sh = (int) ((long) w * videoH / videoW);
        if (sh > h) { sh = h; sw = (int) ((long) h * videoW / videoH); }
        FrameLayout.LayoutParams lp = (FrameLayout.LayoutParams) surface.getLayoutParams();
        if (lp.width == sw && lp.height == sh) return;
        lp.width = sw;
        lp.height = sh;
        surface.setLayoutParams(lp);
    }

    /** Every finger on the picture, as the PC wants it: id, down / move / up, place from 0 to 1. */
    private void onFingers(MotionEvent e) {
        StreamClient c = client;
        if (c == null) return;
        int action = e.getActionMasked(), index = e.getActionIndex();
        int count = Math.min(e.getPointerCount(), 10);
        byte[] ids = new byte[count], states = new byte[count];
        float[] xs = new float[count], ys = new float[count];
        float w = surface.getWidth(), h = surface.getHeight();
        for (int i = 0; i < count; i++) {
            ids[i] = (byte) e.getPointerId(i);
            xs[i] = e.getX(i) / w;
            ys[i] = e.getY(i) / h;
            byte s = StreamClient.MOVE;
            if (action == MotionEvent.ACTION_CANCEL) s = StreamClient.UP;
            else if (i == index && (action == MotionEvent.ACTION_DOWN || action == MotionEvent.ACTION_POINTER_DOWN)) s = StreamClient.DOWN;
            else if (i == index && (action == MotionEvent.ACTION_UP || action == MotionEvent.ACTION_POINTER_UP)) s = StreamClient.UP;
            states[i] = s;
        }
        c.sendTouch(ids, states, xs, ys);
    }

    private void showMessage(String text) {
        message.setText(text);
        message.setVisibility(text == null ? View.GONE : View.VISIBLE);
    }

    // ---- StreamClient.Listener (any thread) ----------------------------------------------------------------------

    @Override
    public void onInfo(JSONObject info) {
        ui.post(() -> {
            pcFps = info.optInt("gameFps", -1);
            int w = info.optInt("width", videoW), h = info.optInt("height", videoH);
            if (w > 0 && h > 0 && (w != videoW || h != videoH)) { videoW = w; videoH = h; fitSurface(); }
            String error = info.optString("error", "");
            if (!error.isEmpty() && !"null".equals(error)) showMessage(error);
            else if (!info.optBoolean("inFront", true)) showMessage("Game PC par saamne nahi hai.\nPC par BlueStacks ko saamne laao.");
            else showMessage(null);
        });
    }

    @Override
    public void onStatus(String text) {
        ui.post(() -> showMessage(text));
    }

    @Override
    public void onStats(int fps, int pingMs, int kbps) {
        // "PC" is how fast the PC itself draws the game: when it is low, the PC is the slow part, not the Wi-Fi.
        int pc = pcFps;
        ui.post(() -> status.setText(fps + " fps" + (pc > 0 ? " · PC " + pc : "") + " · " + pingMs + " ms · " + (kbps / 1000) + " Mbps"));
    }

    @Override
    public void onEnded(String reason) {
        ui.post(() -> {
            if (isFinishing()) return;
            new AlertDialog.Builder(this)
                    .setMessage(reason)
                    .setCancelable(false)
                    .setPositiveButton("OK", (d, w) -> finish())
                    .show();
        });
    }
}
