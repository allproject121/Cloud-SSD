using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using CloudSSD.Core;

namespace CloudSSD.App;

/// <summary>
/// A cloud drive the phone may manage files on. <paramref name="FixedTopLevel"/> marks the "Each Account" drive,
/// whose first level is one folder per account and cannot be changed. <paramref name="CacheDir"/> is where rclone
/// keeps written data until it is uploaded, so that is the disk that must have room.
/// </summary>
internal sealed record CloudRoot(string Id, string Root, bool ReadOnly, bool FixedTopLevel, string? CacheDir, string DisplayName)
{
    /// <summary>How deep an item must be before it may be created, renamed, moved or deleted.</summary>
    public int MinEditDepth => FixedTopLevel ? 2 : 1;
}

/// <summary>A path the phone sent, resolved to a place inside one cloud drive. Relative uses '/', "" is the drive itself.</summary>
internal sealed record FsTarget(CloudRoot Root, string Relative, string FullPath)
{
    public string Id => Root.Id + ":" + Relative;
    public int Depth => Relative.Length == 0 ? 0 : Relative.Count(c => c == '/') + 1;
    public string Name => Relative.Length == 0 ? Root.DisplayName : Relative[(Relative.LastIndexOf('/') + 1)..];
    public bool CanEdit => !Root.ReadOnly && Depth >= Root.MinEditDepth;
    public bool CanCreateInside => !Root.ReadOnly && Depth + 1 >= Root.MinEditDepth;
}

/// <summary>
/// File management for the phone: the routes under /api/fs/ let it use a cloud drive like normal storage (list,
/// create, write, rename, move, delete). They only ever touch paths inside a connected cloud drive; the phone names
/// a place as "&lt;drive id&gt;:&lt;folder/file&gt;" (for example "cloudssd:Photos/a.jpg"), never as a Windows path.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    /// <summary>Temporary name of a file that is still being received; hidden from listings.</summary>
    internal const string PartSuffix = ".cssd-part";
    private const long CacheReserveBytes = 10L * 1024 * 1024 * 1024;

    /// <summary>Tests have no real mount; they supply folders to act as cloud drives.</summary>
    internal Func<IReadOnlyList<CloudRoot>>? CloudRootsOverride;

    private IReadOnlyList<CloudRoot> CloudRoots() => CloudRootsOverride?.Invoke()
        ?? _host.Mounts.Sessions.Where(s => s.State == MountState.Mounted)
            .Select(s => new CloudRoot(s.Id, s.Root, s.Definition.ReadOnly, s.Id == AppHost.SingleMountId, s.CacheDir, s.Definition.DisplayName))
            .ToList();

    private class FsException(int status, string message) : Exception(message)
    {
        public int Status { get; } = status;
    }

    /// <summary>A resumable upload that must continue from <see cref="Offset"/> (the bytes the PC really has).</summary>
    private sealed class FsOffsetException(long offset, string message, int status = 409) : FsException(status, message)
    {
        public long Offset { get; } = offset;
    }

    private static Task SendFsErrorAsync(NetworkStream stream, Exception e, CancellationToken ct)
    {
        var (status, message) = e switch
        {
            FsException f => (f.Status, f.Message),
            UnauthorizedAccessException => (403, "The drive refused this change."),
            FileNotFoundException or DirectoryNotFoundException => (404, "Not found."),
            // 0x70 = disk full, 0x27 = handle disk full: the cache disk or the cloud account has no room.
            _ when (e.HResult & 0xFFFF) is 0x70 or 0x27 => (507, "No space left for this file."),
            _ => (500, e.Message)
        };
        object body = e is FsOffsetException o ? new { error = message, offset = o.Offset } : new { error = message };
        return SendJsonAsync(stream, status, StatusText(status), body, ct);
    }

    /// <summary>Handles /api/fs/* and /api/backup/*; false for every other path.</summary>
    private async Task<bool> TryHandleFsRouteAsync(string method, string path, Dictionary<string, string> q, Dictionary<string, string> headers,
        NetworkStream stream, string clientIp, CancellationToken ct)
    {
        bool fs = path.StartsWith("/api/fs/", StringComparison.OrdinalIgnoreCase);
        bool backup = path.StartsWith("/api/backup/", StringComparison.OrdinalIgnoreCase);
        if (!fs && !backup) return false;

        var route = path.ToLowerInvariant();
        bool read = route is "/api/fs/roots" or "/api/fs/list" or "/api/fs/stat" or "/api/fs/read" or "/api/backup/index"
            or "/api/fs/thumb" or "/api/fs/search" or "/api/fs/usage";
        if (!read && method != "POST")
        {
            await SendJsonAsync(stream, 405, "Method Not Allowed", new { error = "Use POST." }, ct);
            return true;
        }

        try
        {
            if (route == "/api/fs/read")
            {
                await FsReadAsync(q, headers, stream, clientIp, ct);
                return true;
            }
            if (route == "/api/fs/thumb")
            {
                await FsThumbAsync(q, stream, ct);
                return true;
            }
            object? result = route switch
            {
                "/api/fs/roots" => FsRoots(),
                "/api/fs/list" => FsList(q),
                "/api/fs/stat" => FsStat(q),
                "/api/fs/search" => FsSearch(q, ct),
                "/api/fs/usage" => FsUsage(q, ct),
                "/api/fs/create" => FsCreate(q),
                "/api/fs/rename" => FsRename(q),
                "/api/fs/move" => FsMove(q),
                "/api/fs/delete" => FsDelete(q),
                "/api/fs/write" => await FsWriteAsync(q, headers, stream, clientIp, ct),
                "/api/backup/index" => BackupIndex(q),
                "/api/backup/upload" => await BackupUploadAsync(q, headers, stream, clientIp, ct),
                _ => null
            };
            if (result is null) await SendJsonAsync(stream, 404, "Not Found", new { error = "API endpoint not found" }, ct);
            else await SendJsonAsync(stream, 200, "OK", result, ct);
        }
        catch (Exception e) when (e is FsException or UnauthorizedAccessException or IOException)
        {
            if (route is "/api/fs/write" or "/api/backup/upload") await DrainBrieflyAsync(stream, ct);
            await SendFsErrorAsync(stream, e, ct);
        }
        return true;
    }

    /// <summary>
    /// Reads away what the phone is still sending before a refusal is answered. Closing a connection with unread data
    /// makes Windows reset it, and the phone would then see "connection lost" instead of the reason. Bounded: a large
    /// upload is not swallowed just to say no.
    /// </summary>
    private static async Task DrainBrieflyAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var total = CancellationTokenSource.CreateLinkedTokenSource(ct);
        total.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            for (long drained = 0; drained < 4 * 1024 * 1024;)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                idle.CancelAfter(TimeSpan.FromMilliseconds(150));
                int read = await stream.ReadAsync(buffer, idle.Token);
                if (read == 0) break;
                drained += read;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (IOException) { }
    }

    private static string StatusText(int status) => status switch
    {
        200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed",
        409 => "Conflict", 411 => "Length Required", 415 => "Unsupported Media Type", 429 => "Too Many Requests",
        503 => "Service Unavailable", 507 => "Insufficient Storage", _ => "Internal Server Error"
    };

    // ---- paths ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// True for a name Windows keeps exactly as given: no path characters, no ".." and no trailing dot or space
    /// (Windows silently drops those, so two different names would mean one file), and not a device name.
    /// </summary>
    internal static bool IsPlainName(string name)
    {
        if (name.Length is 0 or > 255 || name is "." or "..") return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (name[^1] is '.' or ' ') return false;
        var stem = (name.Contains('.') ? name[..name.IndexOf('.')] : name).ToUpperInvariant();
        return !(stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])));
    }

    /// <summary>
    /// Resolves "&lt;drive id&gt;:&lt;folder/file&gt;" strictly: every segment must be a plain name, and the result must
    /// lie inside that drive. Unlike <see cref="ResolveLocalPath"/> it never guesses a similar existing name — a write
    /// must land exactly where it was asked to.
    /// </summary>
    internal static FsTarget ResolveCloudPath(IReadOnlyList<CloudRoot> roots, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new FsException(400, "Missing path.");
        int colon = raw.IndexOf(':');
        if (colon <= 0) throw new FsException(400, "A path looks like <drive>:<folder/file>.");
        var id = raw[..colon];
        var root = roots.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new FsException(503, "That cloud drive is not connected on the PC.");
        var segments = raw[(colon + 1)..].Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => !IsPlainName(s))) throw new FsException(400, "That name cannot be used.");

        var rootFull = Path.GetFullPath(root.Root).TrimEnd('\\') + "\\";
        var full = Path.GetFullPath(Path.Combine(rootFull, Path.Combine(segments)));
        if (segments.Length > 0 && !full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new FsException(403, "Outside the cloud drive.");
        return new FsTarget(root, string.Join('/', segments), segments.Length == 0 ? rootFull : full);
    }

    private FsTarget Target(Dictionary<string, string> q, string key = "path") => ResolveCloudPath(CloudRoots(), q.GetValueOrDefault(key));

    private static FsTarget Child(FsTarget parent, string name) =>
        new(parent.Root, parent.Relative.Length == 0 ? name : parent.Relative + "/" + name, Path.Combine(parent.FullPath, name));

    private static FileSystemInfo? Info(FsTarget t) =>
        Directory.Exists(t.FullPath) ? new DirectoryInfo(t.FullPath) : File.Exists(t.FullPath) ? new FileInfo(t.FullPath) : null;

    private static FileSystemInfo Existing(FsTarget t) => Info(t) ?? throw new FsException(404, "Not found.");

    private static void RequireEditable(FsTarget t)
    {
        if (t.Root.ReadOnly) throw new FsException(403, "This drive is read-only.");
        if (t.Depth == 0) throw new FsException(403, "The drive itself cannot be changed.");
        if (!t.CanEdit) throw new FsException(403, "Account folders cannot be changed. Open an account folder first.");
    }

    private static void RequireCreatable(FsTarget parent)
    {
        if (parent.Root.ReadOnly) throw new FsException(403, "This drive is read-only.");
        if (!parent.CanCreateInside) throw new FsException(403, "Nothing can be added here. Open an account folder first.");
    }

    /// <summary>A name that is free in the folder: "photo.jpg", else "photo (1).jpg", "photo (2).jpg"…</summary>
    internal static string FreeName(string folder, string name)
    {
        var candidate = name;
        for (int n = 1; File.Exists(Path.Combine(folder, candidate)) || Directory.Exists(Path.Combine(folder, candidate)); n++)
            candidate = $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}";
        return candidate;
    }

    private static object Entry(FsTarget t, FileSystemInfo info) => new
    {
        id = t.Id,
        name = t.Name,
        isDirectory = info is DirectoryInfo,
        size = info is FileInfo f ? f.Length : (long?)null,
        modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds(),
        created = new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds(),
        canEdit = t.CanEdit,
        canCreate = info is DirectoryInfo && t.CanCreateInside
    };

    // ---- reads ---------------------------------------------------------------------------------------------------

    private object FsRoots() => CloudRoots().Select(r =>
    {
        long? total = null, free = null;
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(r.Root))!);
            if (d.IsReady) (total, free) = (d.TotalSize, d.AvailableFreeSpace);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { }
        return new
        {
            id = r.Id, title = r.DisplayName, readOnly = r.ReadOnly, fixedTopLevel = r.FixedTopLevel,
            canCreate = !r.ReadOnly && !r.FixedTopLevel, totalBytes = total, freeBytes = free
        };
    }).ToList();

    private object FsList(Dictionary<string, string> q)
    {
        var t = Target(q);
        if (Existing(t) is not DirectoryInfo dir) throw new FsException(400, "Not a folder.");
        var entries = dir.EnumerateFileSystemInfos()
            .Where(i => (i.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !i.Name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i is DirectoryInfo ? 0 : 1).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => Entry(Child(t, i.Name), i)).ToList();
        return new { folder = Entry(t, dir), entries };
    }

    private object FsStat(Dictionary<string, string> q)
    {
        var t = Target(q);
        var info = Existing(t);
        if (q.GetValueOrDefault("details") is not ("1" or "true")) return Entry(t, info);
        // Details screen: the usual entry plus the full path and, for photos and videos, what Windows knows about them.
        var media = info is FileInfo && MediaDetailsFactory is { } f ? f(t.FullPath) : null;
        return new { entry = Entry(t, info), fullPath = CleanPath(t.FullPath), drive = t.Root.DisplayName, media };
    }

    /// <summary>The file's bytes (with Range support), exactly like /api/stream but named by drive id.</summary>
    private async Task FsReadAsync(Dictionary<string, string> q, Dictionary<string, string> headers, NetworkStream stream, string clientIp, CancellationToken ct)
    {
        var t = Target(q);
        if (Existing(t) is not FileInfo file) throw new FsException(400, "Not a file.");
        await SendFileAsync(file, headers, stream, clientIp, null, null, "stream", ct);
    }

    // ---- changes -------------------------------------------------------------------------------------------------

    private object FsCreate(Dictionary<string, string> q)
    {
        var parent = Target(q, "parent");
        if (Existing(parent) is not DirectoryInfo) throw new FsException(400, "Not a folder.");
        RequireCreatable(parent);
        var wanted = (q.GetValueOrDefault("name") ?? "").Trim();
        var name = FreeName(parent.FullPath, IsPlainName(wanted) ? wanted : SafeUploadName(wanted));
        var t = Child(parent, name);
        if (q.GetValueOrDefault("dir") is "1" or "true") Directory.CreateDirectory(t.FullPath);
        else using (File.Open(t.FullPath, FileMode.CreateNew, FileAccess.Write)) { }
        return Entry(t, Existing(t));
    }

    private object FsRename(Dictionary<string, string> q)
    {
        var t = Target(q);
        var info = Existing(t);
        RequireEditable(t);
        var name = (q.GetValueOrDefault("name") ?? "").Trim();
        if (!IsPlainName(name)) throw new FsException(400, "That name cannot be used.");
        if (name == t.Name) return Entry(t, info);
        var parent = ResolveCloudPath(CloudRoots(), t.Root.Id + ":" + (t.Relative.Contains('/') ? t.Relative[..t.Relative.LastIndexOf('/')] : ""));
        var dest = Child(parent, name);
        // Only a change of letter case may point at the item itself.
        if (Info(dest) != null && !string.Equals(name, t.Name, StringComparison.OrdinalIgnoreCase)) throw new FsException(409, "That name is already used here.");
        MoveItem(info, dest.FullPath);
        return Entry(dest, Existing(dest));
    }

    private object FsMove(Dictionary<string, string> q)
    {
        var t = Target(q);
        var info = Existing(t);
        RequireEditable(t);
        var folder = Target(q, "to");
        if (Existing(folder) is not DirectoryInfo) throw new FsException(400, "The destination is not a folder.");
        if (!string.Equals(folder.Root.Id, t.Root.Id, StringComparison.OrdinalIgnoreCase)) throw new FsException(400, "Items cannot be moved between drives.");
        RequireCreatable(folder);
        if (info is DirectoryInfo && (folder.Relative + "/").StartsWith(t.Relative + "/", StringComparison.OrdinalIgnoreCase))
            throw new FsException(400, "A folder cannot be moved into itself.");
        // On the "Each Account" drive a move to another account would download and re-upload everything through the PC.
        if (t.Root.FixedTopLevel && !string.Equals(t.Relative.Split('/')[0], folder.Relative.Split('/')[0], StringComparison.OrdinalIgnoreCase))
            throw new FsException(409, "Items cannot be moved between accounts here. Use the All Accounts drive.");
        var dest = Child(folder, t.Name);
        if (Info(dest) != null) throw new FsException(409, "An item with that name is already there.");
        MoveItem(info, dest.FullPath);
        return Entry(dest, Existing(dest));
    }

    private static void MoveItem(FileSystemInfo info, string destination)
    {
        if (info is DirectoryInfo) Directory.Move(info.FullName, destination);
        else File.Move(info.FullName, destination);
    }

    private object FsDelete(Dictionary<string, string> q)
    {
        var t = Target(q);
        var info = Existing(t);
        RequireEditable(t);
        // On Google Drive this moves the item to the account's trash, where it can be restored.
        if (info is DirectoryInfo) Directory.Delete(t.FullPath, recursive: true);
        else File.Delete(t.FullPath);
        return new { success = true, id = t.Id };
    }

    private async Task<object> FsWriteAsync(Dictionary<string, string> q, Dictionary<string, string> headers, NetworkStream stream, string clientIp, CancellationToken ct)
    {
        var t = Target(q);
        if (t.Depth == 0) throw new FsException(400, "Missing file name.");
        var parent = ResolveCloudPath(CloudRoots(), t.Root.Id + ":" + (t.Relative.Contains('/') ? t.Relative[..t.Relative.LastIndexOf('/')] : ""));
        // A phone moving a whole folder to the cloud sends its files with mkdirs=1: missing folders are made on the way.
        if (q.GetValueOrDefault("mkdirs") == "1" && parent.Depth > 0 && Existing(parent) is null && !File.Exists(parent.FullPath))
        {
            RequireCreatable(parent);
            Directory.CreateDirectory(parent.FullPath);
        }
        if (Existing(parent) is not DirectoryInfo) throw new FsException(400, "Not a folder.");
        RequireCreatable(parent);
        if (Directory.Exists(t.FullPath)) throw new FsException(409, "A folder with that name is already there.");
        bool overwrite = q.GetValueOrDefault("overwrite") is "1" or "true";
        if (q.GetValueOrDefault("uploadId") is { } uploadId)
        {
            if (!IsUploadId(uploadId)) throw new FsException(400, "uploadId must be 32 lowercase hex characters.");
            if (QueryLong(q, "total") is not { } total) throw new FsException(400, "A resumable upload needs total.");
            var r = await ReceiveResumableAsync(uploadId, "fs:" + t.Root.Id, parent.FullPath, t.Name, overwrite, ModifiedTime(q),
                QueryLong(q, "offset") ?? 0, total, t.Root.CacheDir ?? t.Root.Root, headers, stream, clientIp, ct);
            if (!r.Complete) return new { complete = false, offset = r.Offset };
            var done = Child(parent, Path.GetFileName(r.FinalPath!));
            return Entry(done, Existing(done));
        }
        var name = overwrite ? t.Name : FreeName(parent.FullPath, t.Name);
        await ReceiveFileAsync(headers, stream, clientIp, parent.FullPath, name, overwrite, ModifiedTime(q), t.Root.CacheDir ?? t.Root.Root, ct);
        var result = Child(parent, name);
        return Entry(result, Existing(result));
    }

    private static DateTime? ModifiedTime(Dictionary<string, string> q) =>
        long.TryParse(q.GetValueOrDefault("modified"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;

    /// <summary>
    /// Receives a request body into <paramref name="folder"/> as <paramref name="name"/>. The data goes to a hidden
    /// part file first and is renamed only when it is complete, in the same request — inside rclone's write-back delay,
    /// so the cloud sees one upload of the finished file and never a half-written one. Returns the size.
    /// </summary>
    private async Task<long> ReceiveFileAsync(Dictionary<string, string> headers, NetworkStream stream, string clientIp, string folder, string name,
        bool overwrite, DateTime? modifiedUtc, string spaceCheckedOn, CancellationToken ct)
    {
        long contentLength = -1;
        if (headers.TryGetValue("Content-Length", out var clStr) && !long.TryParse(clStr, out contentLength)) contentLength = -1;
        bool chunked = headers.TryGetValue("Transfer-Encoding", out var te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase);
        if (contentLength < 0 && !chunked) throw new FsException(411, "Upload needs a Content-Length or chunked transfer encoding.");

        // What is written lands in rclone's cache on the PC first, so that disk needs the room (minus the usual reserve).
        long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(spaceCheckedOn))!).AvailableFreeSpace;
        long budget = free - CacheReserveBytes;
        if (budget <= 0 || contentLength > budget)
            throw new FsException(507, $"Not enough free space on the PC ({ByteSize.Format(Math.Max(0, free))} free, 10 GB is kept in reserve).");

        var dest = Path.Combine(folder, name);
        var part = Path.Combine(folder, $"{name}.{Guid.NewGuid():N}"[..Math.Min(name.Length + 9, 200)] + PartSuffix);
        long saved = 0;
        bool complete = false;
        var sw = Stopwatch.StartNew();
        try
        {
            await using (var file = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, useAsync: true))
            {
                var body = chunked ? (Stream)new ChunkedBodyStream(stream) : stream;
                long limit = chunked ? budget : contentLength;
                var buffer = new byte[64 * 1024];
                while (saved < limit)
                {
                    int read;
                    try { read = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - saved)), ct); }
                    catch (Exception e) when (e is IOException or InvalidDataException) { break; } // the phone went away mid-upload
                    if (read == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    saved += read;
                    TrackBytes(read);
                }
                complete = chunked ? ((ChunkedBodyStream)body).Finished : saved == contentLength;
            }
            if (!complete) throw new FsException(400, "The upload was cut off or too large for the free space; nothing was saved.");
            if (modifiedUtc is { } m) File.SetLastWriteTimeUtc(part, m);
            File.Move(part, dest, overwrite);
        }
        catch (Exception)
        {
            try { File.Delete(part); } catch (IOException) { }
            double mbps = saved / (1024.0 * 1024.0) / Math.Max(0.01, sw.Elapsed.TotalSeconds);
            RecordTransfer(name, dest, saved, "upload", ResolveDeviceName(clientIp), clientIp, mbps, "Interrupted");
            throw;
        }
        double speed = saved / (1024.0 * 1024.0) / Math.Max(0.01, sw.Elapsed.TotalSeconds);
        FileTransferred?.Invoke(name, saved);
        RecordTransfer(name, dest, saved, "upload", ResolveDeviceName(clientIp), clientIp, speed, "Completed");
        return saved;
    }
}
