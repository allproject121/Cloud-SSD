package com.cloudssd.mobile;

import android.content.Context;
import android.content.SharedPreferences;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.SystemClock;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.NetworkInterface;
import java.net.URL;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/**
 * Which address reaches the paired PC right now. At home that is its Wi-Fi address; away from home it is its
 * Tailscale address (when the PC allows that). Before the pairing key is sent anywhere, the address must answer with
 * this PC's public id: on a café's Wi-Fi "192.168.1.5" is someone else's device and must never get the key.
 * Used by everything that talks to the PC without the page: the Files app, camera backup, transfers.
 */
final class PcLink {
    private static final String PREFS = "cloudssd";
    private static final long VERIFIED_MS = 60_000;
    private static final long DOWN_MS = 5_000;

    private static final Object lock = new Object();
    private static String verifiedUrl = "";
    private static String verifiedNet = "";
    private static long verifiedAt;
    private static final Map<String, Long> downUntil = new HashMap<>();

    private PcLink() {
    }

    private static SharedPreferences prefs(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    /** A random id for this install of the app; the PC uses it to keep one entry per phone. */
    static String installId(Context c) {
        SharedPreferences p = prefs(c);
        String id = p.getString("install_id", "");
        if (id.isEmpty()) {
            id = UUID.randomUUID().toString().replace("-", "");
            p.edit().putString("install_id", id).apply();
        }
        return id;
    }

    static boolean isPaired(Context c) {
        return !prefs(c).getString("server", "").isEmpty() && !TokenStore.pcToken(c).isEmpty();
    }

    /** The page tells us the PC's addresses: the one it paired on (home) and the ones that work away (Tailscale). */
    static void setCandidates(Context c, String lan, JSONArray away, String pcId) {
        SharedPreferences.Editor e = prefs(c).edit();
        if (lan != null && !lan.isEmpty()) e.putString("server_lan", strip(lan));
        if (away != null) e.putString("server_away", away.toString());
        if (pcId != null && !pcId.isEmpty()) e.putString("pc_id", pcId);
        e.apply();
        synchronized (lock) {
            verifiedAt = 0;
        }
    }

    private static String strip(String url) {
        String u = url.trim();
        while (u.endsWith("/")) u = u.substring(0, u.length() - 1);
        return u;
    }

    // ---- the network the phone is on ----------------------------------------------------------------------------

    /** {wifi, metered, vpn, tailnetIp, key}: what the phone is connected through right now. */
    static JSONObject netInfo(Context c) {
        JSONObject o = new JSONObject();
        try {
            ConnectivityManager cm = (ConnectivityManager) c.getSystemService(Context.CONNECTIVITY_SERVICE);
            Network n = cm == null ? null : cm.getActiveNetwork();
            NetworkCapabilities caps = n == null ? null : cm.getNetworkCapabilities(n);
            boolean wifi = caps != null && (caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) || caps.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET));
            boolean vpn = caps != null && caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN);
            // With Tailscale on, the active network is the VPN; whether the phone is really on Wi-Fi underneath is
            // told by a Wi-Fi network existing at all.
            if (vpn && cm != null) {
                for (Network other : cm.getAllNetworks()) {
                    NetworkCapabilities oc = cm.getNetworkCapabilities(other);
                    if (oc != null && !oc.hasTransport(NetworkCapabilities.TRANSPORT_VPN)
                            && (oc.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) || oc.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET))) wifi = true;
                }
            }
            boolean metered = cm != null && cm.isActiveNetworkMetered();
            o.put("wifi", wifi);
            o.put("metered", metered);
            o.put("vpn", vpn);
            o.put("tailnetIp", tailnetIp());
            o.put("key", (n == null ? "none" : n.toString()) + (wifi ? ":w" : ":m"));
        } catch (Exception ignored) {
        }
        return o;
    }

    /** This phone's Tailscale address (100.64.0.0/10) when the Tailscale app is connected, else "". */
    static String tailnetIp() {
        try {
            for (NetworkInterface ni : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!ni.isUp()) continue;
                for (InetAddress a : Collections.list(ni.getInetAddresses())) {
                    if (!(a instanceof Inet4Address)) continue;
                    byte[] b = a.getAddress();
                    if ((b[0] & 0xFF) == 100 && (b[1] & 0xC0) == 64) return a.getHostAddress();
                }
            }
        } catch (Exception ignored) {
        }
        return "";
    }

    // ---- choosing the address -----------------------------------------------------------------------------------

    /** The addresses to try, best first: on Wi-Fi the home address, then Tailscale; off Wi-Fi Tailscale only. */
    static List<String> candidates(Context c, boolean onWifi) {
        SharedPreferences p = prefs(c);
        List<String> out = new ArrayList<>();
        String lan = p.getString("server_lan", p.getString("server", ""));
        if (!lan.isEmpty() && (onWifi || lan.contains("127.0.0.1") || lan.contains("://100."))) out.add(strip(lan));
        try {
            JSONArray away = new JSONArray(p.getString("server_away", "[]"));
            for (int i = 0; i < away.length(); i++) {
                String u = strip(away.optString(i, ""));
                if (!u.isEmpty() && !out.contains(u)) out.add(u);
            }
        } catch (JSONException ignored) {
        }
        String last = p.getString("server", "");
        if (!last.isEmpty() && !out.contains(strip(last)) && onWifi) out.add(strip(last));
        return out;
    }

    /**
     * An address of the paired PC that answers now, or "" when none does. Checked with the PC's public id before
     * the key is ever sent there; the answer is reused for a minute on the same network.
     */
    static String resolve(Context c) {
        JSONObject net = netInfo(c);
        String key = net.optString("key", "");
        synchronized (lock) {
            if (!verifiedUrl.isEmpty() && key.equals(verifiedNet) && SystemClock.elapsedRealtime() - verifiedAt < VERIFIED_MS && !isDown(verifiedUrl))
                return verifiedUrl;
        }
        String pcId = prefs(c).getString("pc_id", "");
        for (String url : candidates(c, net.optBoolean("wifi", true))) {
            if (isDown(url)) continue;
            if (identify(url, pcId)) {
                synchronized (lock) {
                    verifiedUrl = url;
                    verifiedNet = key;
                    verifiedAt = SystemClock.elapsedRealtime();
                }
                prefs(c).edit().putString("server", url).apply();
                return url;
            }
            markDown(url);
        }
        return "";
    }

    /** True when the address answers as our PC (an older PC that has no id is accepted on the address it was paired on). */
    private static boolean identify(String url, String pcId) {
        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL(url + "/api/status").openConnection();
            conn.setConnectTimeout(2500);
            conn.setReadTimeout(4000);
            conn.setUseCaches(false);
            conn.setRequestProperty("Connection", "close");
            if (conn.getResponseCode() != 200) return false;
            JSONObject s = new JSONObject(readAll(conn.getInputStream()));
            if (!s.has("pcName")) return false;
            String theirs = s.optString("pcId", "");
            return pcId.isEmpty() || theirs.isEmpty() || pcId.equals(theirs);
        } catch (Exception e) {
            return false;
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    static void markDown(String url) {
        synchronized (lock) {
            downUntil.put(url, SystemClock.elapsedRealtime() + DOWN_MS);
            if (url.equals(verifiedUrl)) verifiedAt = 0;
        }
    }

    private static boolean isDown(String url) {
        synchronized (lock) {
            Long until = downUntil.get(url);
            return until != null && SystemClock.elapsedRealtime() < until;
        }
    }

    /** "lan" or "tailnet" for the address in use. */
    static String routeOf(String url) {
        return url != null && url.contains("://100.") || (url != null && url.contains(".ts.net")) ? "tailnet" : "lan";
    }

    private static String readAll(InputStream in) throws java.io.IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
        return out.toString("UTF-8");
    }
}
