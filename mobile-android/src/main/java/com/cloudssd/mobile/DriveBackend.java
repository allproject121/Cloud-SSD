package com.cloudssd.mobile;

import android.content.Context;
import android.os.SystemClock;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayInputStream;
import java.io.FileNotFoundException;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.text.ParseException;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.TimeZone;

/**
 * The same two drives the PC shows, read straight from Google Drive with the accounts signed in on this phone:
 * "cloudssd:" (All Accounts — every account's files together, new files go to the account with the most room) and
 * "accounts:" (Each Account — one folder per account). Ids are the same as the PC's, so a file opened in the Files
 * app keeps working whichever of the two answers. Names are matched the way rclone stores them on the PC.
 */
final class DriveBackend implements CloudBackend {
    static final String UNION = "cloudssd";
    static final String EACH = "accounts";
    private static final long CACHE_MS = 60_000;

    private final Context ctx;

    /** path cache: "email|path" → {meta, at} */
    private static final Map<String, Object[]> metaCache = Collections.synchronizedMap(new LinkedHashMap<String, Object[]>(64, 0.75f, true) {
        @Override
        protected boolean removeEldestEntry(Map.Entry<String, Object[]> e) {
            return size() > 800;
        }
    });

    DriveBackend(Context c) {
        ctx = c.getApplicationContext();
    }

    @Override
    public String label() {
        return "Google Drive";
    }

    // ---- accounts ------------------------------------------------------------------------------------------------

    private List<JSONObject> accounts() {
        JSONArray all = GoogleAuth.accounts(ctx);
        List<JSONObject> out = new ArrayList<>();
        for (int i = 0; i < all.length(); i++) {
            JSONObject a = all.optJSONObject(i);
            if (a != null && !a.optBoolean("needsReauth")) out.add(a);
        }
        return out;
    }

    @Override
    public boolean isConfigured() {
        return GoogleAuth.available() && !accounts().isEmpty();
    }

    private String token(String email) throws IOException {
        return GoogleAuth.accessToken(ctx, email);
    }

    /** Accounts with the most free room first (that is where a new file goes on "All Accounts"). */
    private List<JSONObject> byFreeSpace() {
        List<JSONObject> list = accounts();
        Collections.sort(list, (a, b) -> Long.compare(b.optLong("freeBytes", 0), a.optLong("freeBytes", 0)));
        return list;
    }

    private JSONObject accountForFolder(String folder) throws BackendException {
        for (JSONObject a : accounts()) if (a.optString("folder").equalsIgnoreCase(folder)) return a;
        throw new BackendException(404, "That account is not signed in on this phone.");
    }

    // ---- ids → places -----------------------------------------------------------------------------------------------

    /** One place inside one account: the account and the path below its Drive root ("" = the root). */
    private static final class Place {
        final JSONObject account;
        final String path;

        Place(JSONObject account, String path) {
            this.account = account;
            this.path = path;
        }

        String email() {
            return account.optString("email");
        }
    }

    private static String drive(String id) {
        int c = id.indexOf(':');
        if (c <= 0) return "";
        return id.substring(0, c);
    }

    private static String rel(String id) {
        int c = id.indexOf(':');
        String r = c < 0 ? id : id.substring(c + 1);
        while (r.startsWith("/")) r = r.substring(1);
        while (r.endsWith("/")) r = r.substring(0, r.length() - 1);
        return r;
    }

    /** Every account the id could live in (all of them for "All Accounts", the one for "Each Account"). */
    private List<Place> places(String id) throws BackendException {
        String d = drive(id), r = rel(id);
        List<Place> out = new ArrayList<>();
        if (UNION.equalsIgnoreCase(d)) {
            for (JSONObject a : byFreeSpace()) out.add(new Place(a, r));
        } else if (EACH.equalsIgnoreCase(d)) {
            if (r.isEmpty()) return out;
            int slash = r.indexOf('/');
            String folder = slash < 0 ? r : r.substring(0, slash);
            out.add(new Place(accountForFolder(folder), slash < 0 ? "" : r.substring(slash + 1)));
        } else throw new BackendException(404, "Unknown drive.");
        return out;
    }

    private static String childId(String parentId, String name) {
        return parentId + (parentId.endsWith(":") ? "" : "/") + name;
    }

    private static String parentOf(String id) {
        int slash = id.lastIndexOf('/'), colon = id.indexOf(':');
        return slash > colon ? id.substring(0, slash) : id.substring(0, colon + 1);
    }

    private static String leafOf(String id) {
        return id.substring(Math.max(id.lastIndexOf('/'), id.indexOf(':')) + 1);
    }

    // ---- names (as rclone writes them on the PC) ------------------------------------------------------------------

    /** A Drive name as the PC shows it: "/" in a name becomes the full-width slash, so paths stay unambiguous. */
    static String shown(String driveName) {
        return driveName.replace('/', '／');
    }

    private static String real(String shownName) {
        return shownName.replace('／', '/');
    }

    private static final String[][] EXPORTS = {
            {"application/vnd.google-apps.document", ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"},
            {"application/vnd.google-apps.spreadsheet", ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"},
            {"application/vnd.google-apps.presentation", ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation"},
            {"application/vnd.google-apps.drawing", ".png", "image/png"}
    };

    private static String[] exportOf(String mime) {
        for (String[] e : EXPORTS) if (e[0].equals(mime)) return e;
        return null;
    }

    private static boolean isFolder(JSONObject m) {
        return DriveApi.FOLDER_MIME.equals(m.optString("mimeType"));
    }

    /** Google forms, sites and the like have no file form: they are not listed. */
    private static boolean listable(JSONObject m) {
        String mime = m.optString("mimeType");
        return !mime.startsWith("application/vnd.google-apps.") || isFolder(m) || exportOf(mime) != null || "application/vnd.google-apps.shortcut".equals(mime);
    }

    private static String nameOf(JSONObject m) {
        String[] e = exportOf(m.optString("mimeType"));
        return shown(m.optString("name")) + (e == null ? "" : e[1]);
    }

    private static long time(String rfc) {
        if (rfc == null || rfc.isEmpty()) return 0;
        try {
            SimpleDateFormat f = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.US);
            f.setTimeZone(TimeZone.getTimeZone("UTC"));
            return f.parse(rfc).getTime();
        } catch (ParseException e) {
            return 0;
        }
    }

    private DocInfo doc(String id, JSONObject m, boolean editable) {
        DocInfo d = new DocInfo();
        d.id = id;
        d.name = nameOf(m);
        d.dir = isFolder(m);
        d.size = m.optLong("size", 0);
        d.modified = time(m.optString("modifiedTime"));
        d.exported = exportOf(m.optString("mimeType")) != null;
        JSONObject caps = m.optJSONObject("capabilities");
        d.canEdit = editable && !d.exported && (caps == null || caps.optBoolean("canEdit", true));
        d.canCreate = d.dir && (caps == null || caps.optBoolean("canAddChildren", true));
        if (d.exported) d.size = -1;
        return d;
    }

    // ---- walking paths ------------------------------------------------------------------------------------------

    private static void forget(String email) {
        synchronized (metaCache) {
            metaCache.keySet().removeIf(k -> k.startsWith(email + "|"));
        }
    }

    /** The item at {@code path} in the account, or null. Two items with one name: the oldest wins (as rclone does). */
    private JSONObject find(String email, String path) throws IOException {
        String key = email + "|" + path;
        Object[] hit = metaCache.get(key);
        if (hit != null && SystemClock.elapsedRealtime() - (Long) hit[1] < CACHE_MS) return (JSONObject) hit[0];
        JSONObject meta;
        if (path.isEmpty()) {
            meta = DriveApi.get(token(email), "root");
        } else {
            int slash = path.lastIndexOf('/');
            JSONObject parent = find(email, slash < 0 ? "" : path.substring(0, slash));
            if (parent == null || !isFolder(parent)) return null;
            String name = path.substring(slash + 1);
            meta = child(email, parent.optString("id"), name);
        }
        if (meta != null && "application/vnd.google-apps.shortcut".equals(meta.optString("mimeType"))) {
            JSONObject sd = meta.optJSONObject("shortcutDetails");
            if (sd != null) {
                JSONObject target = DriveApi.get(token(email), sd.optString("targetId"));
                try {
                    target.put("name", meta.optString("name"));
                } catch (JSONException ignored) {
                }
                meta = target;
            }
        }
        metaCache.put(key, new Object[]{meta, SystemClock.elapsedRealtime()});
        return meta;
    }

    private JSONObject child(String email, String parentId, String shownName) throws IOException {
        String name = real(shownName);
        List<JSONObject> hits = DriveApi.query(token(email), "'" + DriveApi.q(parentId) + "' in parents and name = '" + DriveApi.q(name) + "' and trashed = false", 20);
        if (hits.isEmpty()) {
            // "Report.docx" may be the Google Doc "Report".
            for (String[] e : EXPORTS) {
                if (!name.endsWith(e[1])) continue;
                hits = DriveApi.query(token(email), "'" + DriveApi.q(parentId) + "' in parents and name = '" + DriveApi.q(name.substring(0, name.length() - e[1].length()))
                        + "' and mimeType = '" + e[0] + "' and trashed = false", 5);
                if (!hits.isEmpty()) break;
            }
        }
        if (hits.isEmpty()) return null;
        JSONObject oldest = hits.get(0);
        for (JSONObject h : hits) if (time(h.optString("createdTime")) < time(oldest.optString("createdTime"))) oldest = h;
        return oldest;
    }

    // ---- CloudBackend: reading ----------------------------------------------------------------------------------

    @Override
    public List<RootInfo> roots() throws IOException {
        long total = 0, free = 0;
        boolean known = false;
        for (JSONObject a : accounts()) {
            try {
                JSONObject q = DriveApi.about(token(a.optString("email"))).optJSONObject("storageQuota");
                if (q != null && q.optLong("limit", -1) > 0) {
                    long limit = q.optLong("limit"), usage = q.optLong("usage", 0);
                    GoogleAuth.updateAccount(ctx, a.optString("email"), "totalBytes", limit);
                    GoogleAuth.updateAccount(ctx, a.optString("email"), "freeBytes", Math.max(0, limit - usage));
                    total += limit;
                    free += Math.max(0, limit - usage);
                    known = true;
                }
            } catch (IOException ignored) {
                // one account not answering does not hide the others
            }
        }
        List<RootInfo> roots = new ArrayList<>();
        RootInfo all = new RootInfo();
        all.id = UNION;
        all.title = "All Accounts";
        all.canCreate = true;
        all.totalBytes = known ? total : -1;
        all.freeBytes = known ? free : -1;
        roots.add(all);
        RootInfo each = new RootInfo();
        each.id = EACH;
        each.title = "Each Account";
        each.canCreate = false;
        each.totalBytes = all.totalBytes;
        each.freeBytes = all.freeBytes;
        roots.add(each);
        return roots;
    }

    @Override
    public List<DocInfo> list(String folderId) throws IOException {
        String d = drive(folderId), r = rel(folderId);
        List<DocInfo> out = new ArrayList<>();
        if (EACH.equalsIgnoreCase(d) && r.isEmpty()) {
            for (JSONObject a : accounts()) {
                DocInfo acc = new DocInfo();
                acc.id = EACH + ":" + a.optString("folder");
                acc.name = a.optString("folder");
                acc.dir = true;
                acc.canCreate = true;
                out.add(acc);
            }
            return out;
        }
        Map<String, DocInfo> merged = new LinkedHashMap<>();
        boolean found = false;
        for (Place p : places(folderId)) {
            JSONObject folder = find(p.email(), p.path);
            if (folder == null || !isFolder(folder)) continue;
            found = true;
            for (JSONObject m : DriveApi.children(token(p.email()), folder.optString("id"))) {
                if (!listable(m)) continue;
                String name = nameOf(m);
                if (merged.containsKey(name.toLowerCase(Locale.ROOT))) continue;   // folders merge by name; for files the first account wins
                merged.put(name.toLowerCase(Locale.ROOT), doc(childId(folderId, name), m, true));
            }
        }
        if (!found) throw new BackendException(404, "Folder not found on Google Drive.");
        out.addAll(merged.values());
        Collections.sort(out, (a, b) -> a.dir != b.dir ? (a.dir ? -1 : 1) : a.name.compareToIgnoreCase(b.name));
        return out;
    }

    @Override
    public DocInfo stat(String id) throws IOException {
        String d = drive(id), r = rel(id);
        if (r.isEmpty() || (EACH.equalsIgnoreCase(d) && r.indexOf('/') < 0)) {
            DocInfo root = new DocInfo();
            root.id = id;
            root.name = r.isEmpty() ? (UNION.equalsIgnoreCase(d) ? "All Accounts" : "Each Account") : r;
            root.dir = true;
            root.canCreate = !r.isEmpty() || UNION.equalsIgnoreCase(d);
            if (EACH.equalsIgnoreCase(d) && !r.isEmpty()) accountForFolder(r);
            return root;
        }
        for (Place p : places(id)) {
            JSONObject m = find(p.email(), p.path);
            if (m != null) return doc(id, m, true);
        }
        throw new BackendException(404, "Not found on Google Drive.");
    }

    @Override
    public InputStream openRead(String id, long offset) throws IOException {
        for (Place p : places(id)) {
            JSONObject m = find(p.email(), p.path);
            if (m == null || isFolder(m)) continue;
            String[] e = exportOf(m.optString("mimeType"));
            return DriveApi.read(token(p.email()), m.optString("id"), e == null ? null : e[2], offset);
        }
        throw new FileNotFoundException("Not found on Google Drive.");
    }

    @Override
    public InputStream thumbnail(String id, int px) throws IOException {
        for (Place p : places(id)) {
            JSONObject m = find(p.email(), p.path);
            if (m == null) continue;
            String link = m.optString("thumbnailLink", "");
            if (link.isEmpty()) return null;
            link = link.replaceAll("=s\\d+$", "") + "=s" + Math.max(64, Math.min(1600, px));
            HttpURLConnection c = (HttpURLConnection) new URL(link).openConnection();
            c.setConnectTimeout(10000);
            c.setReadTimeout(20000);
            c.setRequestProperty("Authorization", "Bearer " + token(p.email()));
            try {
                if (c.getResponseCode() != 200) return null;
                java.io.ByteArrayOutputStream out = new java.io.ByteArrayOutputStream();
                try (InputStream in = c.getInputStream()) {
                    byte[] buf = new byte[16 * 1024];
                    for (int n; (n = in.read(buf)) > 0; ) out.write(buf, 0, n);
                }
                return new ByteArrayInputStream(out.toByteArray());
            } finally {
                c.disconnect();
            }
        }
        return null;
    }

    @Override
    public List<DocInfo> search(String folderId, String query, int max) throws IOException {
        List<DocInfo> out = new ArrayList<>();
        String[] words = query.trim().split("\\s+");
        StringBuilder q = new StringBuilder("trashed = false");
        for (String w : words) if (!w.isEmpty()) q.append(" and name contains '").append(DriveApi.q(w)).append("'");
        String d = drive(folderId);
        for (Place p : places(UNION.equalsIgnoreCase(d) ? UNION + ":" : folderId)) {
            String token = token(p.email());
            String base = EACH.equalsIgnoreCase(d) ? EACH + ":" + p.account.optString("folder") : UNION + ":";
            for (JSONObject m : DriveApi.query(token, q.toString(), max)) {
                if (!listable(m)) continue;
                String path = pathOf(token, m);
                if (path == null) continue;   // not in My Drive (shared with me, other drives)
                String id = childId(base, path);
                if (!id.toLowerCase(Locale.ROOT).startsWith(folderId.toLowerCase(Locale.ROOT).replaceAll("/$", ""))) continue;
                out.add(doc(id, m, true));
                if (out.size() >= max) return out;
            }
        }
        return out;
    }

    /** "Folder/Sub/name" of an item below My Drive, walking its parents (null when it is not under My Drive). */
    private String pathOf(String token, JSONObject m) throws IOException {
        StringBuilder path = new StringBuilder(nameOf(m));
        JSONArray parents = m.optJSONArray("parents");
        String rootId = DriveApi.get(token, "root").optString("id");
        for (int depth = 0; depth < 30 && parents != null && parents.length() > 0; depth++) {
            String pid = parents.optString(0);
            if (pid.equals(rootId)) return path.toString();
            JSONObject parent = DriveApi.get(token, pid);
            path.insert(0, shown(parent.optString("name")) + "/");
            parents = parent.optJSONArray("parents");
        }
        return null;
    }

    // ---- CloudBackend: changing ---------------------------------------------------------------------------------

    /** The folder in this account, created on the way when missing. */
    private JSONObject ensureFolder(String email, String path) throws IOException {
        JSONObject m = find(email, path);
        if (m != null) {
            if (!isFolder(m)) throw new BackendException(409, "A file with that name is in the way.");
            return m;
        }
        int slash = path.lastIndexOf('/');
        JSONObject parent = ensureFolder(email, slash < 0 ? "" : path.substring(0, slash));
        JSONObject made = DriveApi.createFolder(token(email), parent.optString("id"), real(path.substring(slash + 1)));
        forget(email);
        return made;
    }

    /** Where a new item in this folder goes: an account that already has the folder (most room first), else the roomiest. */
    private Place placeForNew(String parentId) throws IOException {
        List<Place> ps = places(parentId);
        if (ps.isEmpty()) throw new BackendException(403, "Account folders cannot be changed. Open an account folder first.");
        for (Place p : ps) {
            JSONObject f = find(p.email(), p.path);
            if (f != null && isFolder(f)) return p;
        }
        return ps.get(0);
    }

    private String freeName(String email, String parentPath, String name) throws IOException {
        String candidate = name;
        for (int n = 1; find(email, parentPath.isEmpty() ? candidate : parentPath + "/" + candidate) != null; n++) {
            int dot = name.lastIndexOf('.');
            candidate = dot > 0 ? name.substring(0, dot) + " (" + n + ")" + name.substring(dot) : name + " (" + n + ")";
        }
        return candidate;
    }

    @Override
    public DocInfo create(String parentId, String name, boolean dir) throws IOException {
        Place p = placeForNew(parentId);
        JSONObject parent = ensureFolder(p.email(), p.path);
        String free = freeName(p.email(), p.path, name);
        JSONObject m = dir ? DriveApi.createFolder(token(p.email()), parent.optString("id"), real(free))
                : DriveApi.upload(token(p.email()), parent.optString("id"), null, real(free), 0, new ByteArrayInputStream(new byte[0]));
        forget(p.email());
        return doc(childId(parentId, free), m, true);
    }

    private static void requireEditable(String id) throws BackendException {
        String d = drive(id), r = rel(id);
        if (r.isEmpty()) throw new BackendException(403, "The drive itself cannot be changed.");
        if (EACH.equalsIgnoreCase(d) && r.indexOf('/') < 0) throw new BackendException(403, "Account folders cannot be changed. Open an account folder first.");
    }

    @Override
    public DocInfo rename(String id, String name) throws IOException {
        requireEditable(id);
        DocInfo last = null;
        String parent = parentOf(id);
        for (Place p : places(id)) {
            JSONObject m = find(p.email(), p.path);
            if (m == null) continue;
            try {
                JSONObject r = DriveApi.update(token(p.email()), m.optString("id"), new JSONObject().put("name", real(name)), null, null);
                forget(p.email());
                last = doc(childId(parent, name), r, true);
            } catch (JSONException e) {
                throw new IOException(e);
            }
        }
        if (last == null) throw new BackendException(404, "Not found on Google Drive.");
        return last;
    }

    @Override
    public DocInfo move(String id, String toFolderId) throws IOException {
        requireEditable(id);
        if (!drive(id).equalsIgnoreCase(drive(toFolderId))) throw new BackendException(400, "Items cannot be moved between drives.");
        if (EACH.equalsIgnoreCase(drive(id))) {
            String a = rel(id).split("/")[0], b = rel(toFolderId).split("/")[0];
            if (!a.equalsIgnoreCase(b)) throw new BackendException(409, "Items cannot be moved between accounts here. Use the All Accounts drive.");
        }
        String name = leafOf(id);
        DocInfo last = null;
        List<Place> destPlaces = places(toFolderId);
        for (Place p : places(id)) {
            JSONObject m = find(p.email(), p.path);
            if (m == null) continue;
            String destPath = null;
            for (Place dp : destPlaces) if (dp.email().equals(p.email())) destPath = dp.path;
            if (destPath == null) continue;
            JSONObject dest = ensureFolder(p.email(), destPath);
            JSONArray parents = m.optJSONArray("parents");
            String from = parents != null && parents.length() > 0 ? parents.optString(0) : null;
            JSONObject r = DriveApi.update(token(p.email()), m.optString("id"), null, dest.optString("id"), from);
            forget(p.email());
            last = doc(childId(toFolderId, name), r, true);
        }
        if (last == null) throw new BackendException(404, "Not found on Google Drive.");
        return last;
    }

    @Override
    public void delete(String id) throws IOException {
        requireEditable(id);
        boolean any = false;
        for (Place p : places(id)) {
            JSONObject m = find(p.email(), p.path);
            if (m == null) continue;
            try {
                // To the account's trash, where it can be restored for 30 days.
                DriveApi.update(token(p.email()), m.optString("id"), new JSONObject().put("trashed", true), null, null);
            } catch (JSONException e) {
                throw new IOException(e);
            }
            forget(p.email());
            any = true;
        }
        if (!any) throw new BackendException(404, "Not found on Google Drive.");
    }

    @Override
    public DocInfo write(String id, InputStream data) throws IOException {
        requireEditable(id);
        String parentId = parentOf(id);
        String name = leafOf(id);
        for (Place p : places(id)) {
            JSONObject m = find(p.email(), p.path);
            if (m != null && !isFolder(m)) {
                if (exportOf(m.optString("mimeType")) != null) throw new BackendException(403, "Google Docs files can only be changed in Google's own apps.");
                JSONObject r = DriveApi.upload(token(p.email()), null, m.optString("id"), null, 0, data);
                forget(p.email());
                return doc(id, r, true);
            }
        }
        Place p = placeForNew(parentId);
        JSONObject parent = ensureFolder(p.email(), p.path);
        JSONObject r = DriveApi.upload(token(p.email()), parent.optString("id"), null, real(name), 0, data);
        forget(p.email());
        return doc(id, r, true);
    }
}
