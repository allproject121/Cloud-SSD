using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CloudSSD.Core;
using CloudSSD.Games;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>
/// Routes for the newer phone app: settings and devices, signed file links, deeper search (files and folders, one
/// folder or every cloud drive), thumbnails of cloud photos and videos, folder sizes, and game save backups that can
/// be downloaded to the phone or restored on the PC.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    /// <summary>
    /// Makes a JPEG thumbnail (longest side about <c>width</c> pixels) of a local file, or null when it cannot. Set by
    /// the desktop app (it needs Windows imaging); the command-line tool leaves it unset and the phone shows icons.
    /// </summary>
    public Func<string, int, CancellationToken, Task<byte[]?>>? ThumbnailFactory { get; set; }

    /// <summary>What Windows knows about a photo or video (size in pixels, length, date taken, camera), or null. Set by the desktop app.</summary>
    public Func<string, object?>? MediaDetailsFactory { get; set; }

    private async Task<bool> TryHandleExtraRouteAsync(string method, string path, Dictionary<string, string> q, Dictionary<string, string> headers,
        NetworkStream stream, string clientIp, CancellationToken ct)
    {
        switch (path.ToLowerInvariant())
        {
            case "/api/media-url":
                await HandleMediaUrlEndpointAsync(q, stream, ct);
                return true;
            case "/api/settings":
                await SendJsonAsync(stream, 200, "OK", SettingsDto(CurrentCaller.Value!), ct);
                return true;
            case "/api/devices/remove":
                await HandleDevicesRemoveAsync(method, q, stream, ct);
                return true;
            case "/api/pair/upgrade":
                await HandlePairUpgradeAsync(method, q, stream, ct);
                return true;
            case "/api/upload/status":
                try { await SendJsonAsync(stream, 200, "OK", UploadStatus(q), ct); }
                catch (FsException e) { await SendFsErrorAsync(stream, e, ct); }
                return true;
            case "/api/saves/download":
                await HandleSaveDownloadAsync(q, headers, stream, clientIp, ct);
                return true;
            case "/api/saves/restore":
                await HandleSaveRestoreAsync(method, q, stream, ct);
                return true;
            default:
                return false;
        }
    }

    // ---- search -----------------------------------------------------------------------------------------------------

    private sealed record SearchHits(List<DirectoryInfo> Folders, List<FileInfo> Files, bool Truncated);

    private static int SearchDepth(Dictionary<string, string> q) =>
        int.TryParse(q.GetValueOrDefault("depth"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? Math.Clamp(d, 1, 8) : 3;

    /// <summary>
    /// Names under <paramref name="root"/> that contain every word of <paramref name="query"/> (any order, any case).
    /// Breadth first, so shallow matches come first; junctions are not followed. A cloud drive answers each folder
    /// with a round trip, so the walk has a time budget and says when it stopped early.
    /// </summary>
    private static SearchHits SearchTree(DirectoryInfo root, string? query, string? category, int depth, int limit, TimeSpan budget, bool folders, CancellationToken ct)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hitFolders = new List<DirectoryInfo>();
        var hitFiles = new List<FileInfo>();
        bool truncated = false;
        var clock = Stopwatch.StartNew();
        try
        {
            var opt = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = depth,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
            };
            foreach (var item in root.EnumerateFileSystemInfos("*", opt))
            {
                if (clock.Elapsed > budget || ct.IsCancellationRequested) { truncated = true; break; }
                if (item.Name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!words.All(w => item.Name.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
                if (item is DirectoryInfo dir)
                {
                    if (!folders || category != null) continue;
                    hitFolders.Add(dir);
                }
                else if (item is FileInfo file)
                {
                    if (!string.IsNullOrEmpty(category) && !MatchesCategory(category, file.Extension.ToLowerInvariant(), file.Name)) continue;
                    hitFiles.Add(file);
                }
                if (hitFolders.Count + hitFiles.Count >= limit) { truncated = true; break; }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { truncated = true; }
        return new SearchHits(hitFolders, hitFiles, truncated);
    }

    /// <summary>One search over every connected cloud drive, sharing one time budget.</summary>
    private object SearchEverywhere(string query, Dictionary<string, string> q, CancellationToken ct)
    {
        var folders = new List<DirectoryInfo>();
        var files = new List<FileInfo>();
        bool truncated = false;
        var clock = Stopwatch.StartNew();
        var total = TimeSpan.FromSeconds(15);
        bool wantFolders = q.GetValueOrDefault("folders") is "1" or "true";
        foreach (var r in CloudRoots())
        {
            var left = total - clock.Elapsed;
            if (left <= TimeSpan.Zero || folders.Count + files.Count >= 150) { truncated = true; break; }
            var hits = SearchTree(new DirectoryInfo(r.Root), query, null, SearchDepth(q), 150 - folders.Count - files.Count, left, wantFolders, ct);
            folders.AddRange(hits.Folders);
            files.AddRange(hits.Files);
            truncated |= hits.Truncated;
        }
        return new
        {
            currentPath = "",
            parentPath = (string?)null,
            folders = folders.Select(d => new { name = d.Name, path = CleanPath(d.FullName), modified = d.LastWriteTimeUtc }),
            files = files.Select(FileDto),
            truncated
        };
    }

    /// <summary>The drive-id form of a path that lies inside a cloud drive.</summary>
    private static FsTarget TargetFor(CloudRoot root, string fullPath)
    {
        var rootFull = Path.GetFullPath(root.Root).TrimEnd('\\') + "\\";
        var rel = Path.GetFullPath(fullPath).TrimEnd('\\');
        rel = rel.Length >= rootFull.Length ? rel[rootFull.Length..].Replace('\\', '/') : "";
        return new FsTarget(root, rel, fullPath);
    }

    /// <summary>/api/fs/search: matches below a cloud folder, as drive ids (used by the phone's Files app).</summary>
    private object FsSearch(Dictionary<string, string> q, CancellationToken ct)
    {
        var t = Target(q);
        if (Existing(t) is not DirectoryInfo dir) throw new FsException(400, "Not a folder.");
        var text = (q.GetValueOrDefault("q") ?? "").Trim();
        if (text.Length == 0) throw new FsException(400, "Missing search text.");
        int limit = int.TryParse(q.GetValueOrDefault("limit"), out var l) ? Math.Clamp(l, 1, 500) : 100;
        var hits = SearchTree(dir, text, null, SearchDepth(q), limit, TimeSpan.FromSeconds(15), folders: true, ct);
        var entries = hits.Folders.Cast<FileSystemInfo>().Concat(hits.Files).Select(i =>
        {
            var hit = TargetFor(t.Root, i.FullName);
            var parent = hit.Relative.Contains('/') ? hit.Root.Id + ":" + hit.Relative[..hit.Relative.LastIndexOf('/')] : hit.Root.Id + ":";
            return new { entry = Entry(hit, i), parent };
        }).ToList();
        return new { entries, truncated = hits.Truncated };
    }

    /// <summary>/api/fs/usage: how many files and how many bytes a folder holds (with a time budget on a cloud drive).</summary>
    private object FsUsage(Dictionary<string, string> q, CancellationToken ct)
    {
        var t = Target(q);
        if (Existing(t) is not DirectoryInfo dir) throw new FsException(400, "Not a folder.");
        long files = 0, folders = 0, bytes = 0;
        bool truncated = false;
        var clock = Stopwatch.StartNew();
        try
        {
            var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var i in dir.EnumerateFileSystemInfos("*", opt))
            {
                if (clock.Elapsed > TimeSpan.FromSeconds(20) || ct.IsCancellationRequested) { truncated = true; break; }
                if (i is FileInfo f) { files++; bytes += f.Length; }
                else folders++;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { truncated = true; }
        return new { files, folders, bytes, bytesFormatted = ByteSize.Format(bytes), truncated };
    }

    // ---- thumbnails ------------------------------------------------------------------------------------------------

    internal static readonly HashSet<string> ThumbnailExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".heic", ".heif", ".tif", ".tiff",
        ".mp4", ".m4v", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".3gp"
    };

    private readonly SemaphoreSlim _thumbGate = new(2, 2);
    private DateTime _lastThumbTrim = DateTime.MinValue;
    private string ThumbDir => Path.Combine(_host.Paths.DataRoot, "thumbs");
    internal const long ThumbCacheBytes = 300L * 1024 * 1024;

    private async Task FsThumbAsync(Dictionary<string, string> q, NetworkStream stream, CancellationToken ct)
    {
        var t = Target(q);
        if (Existing(t) is not FileInfo file || file.Name.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase)) throw new FsException(400, "Not a file.");
        if (!ThumbnailExtensions.Contains(file.Extension)) throw new FsException(415, "No thumbnail for this kind of file.");
        if (ThumbnailFactory is not { } make) throw new FsException(404, "Thumbnails are not available on this PC.");
        int w = int.TryParse(q.GetValueOrDefault("w"), out var ww) ? Math.Clamp(ww, 64, 2048) : 256;

        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{w}"))).ToLowerInvariant();
        var dir = Path.Combine(ThumbDir, key[..2]);
        var jpg = Path.Combine(dir, key + ".jpg");
        var none = Path.Combine(dir, key + ".none");
        byte[]? bytes = null;
        if (File.Exists(jpg))
        {
            try
            {
                bytes = await File.ReadAllBytesAsync(jpg, ct);
                File.SetLastAccessTimeUtc(jpg, DateTime.UtcNow);
            }
            catch (IOException) { bytes = null; }
        }
        else if (File.Exists(none) && DateTime.UtcNow - File.GetLastWriteTimeUtc(none) < TimeSpan.FromHours(24))
        {
            throw new FsException(404, "No thumbnail for this file.");
        }

        if (bytes is null)
        {
            if (!await _thumbGate.WaitAsync(TimeSpan.FromSeconds(30), ct)) throw new FsException(503, "Busy making thumbnails; try again.");
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(TimeSpan.FromSeconds(20));
                try { bytes = await make(file.FullName, w, limit.Token); }
                catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _log.LogDebug(e, "Thumbnail failed for {File}", file.Name);
                    bytes = null;
                }
                Directory.CreateDirectory(dir);
                if (bytes is { Length: > 0 }) await File.WriteAllBytesAsync(jpg, bytes, ct);
                else await File.WriteAllTextAsync(none, "", ct);
            }
            finally { _thumbGate.Release(); }
            if (DateTime.UtcNow - _lastThumbTrim > TimeSpan.FromMinutes(10)) _ = Task.Run(TrimThumbCache);
            if (bytes is not { Length: > 0 }) throw new FsException(404, "No thumbnail for this file.");
        }
        await SendResponseAsync(stream, 200, "OK", "image/jpeg", bytes, ct, cacheControl: "private, max-age=2592000");
    }

    /// <summary>Keeps the thumbnail cache under its size by dropping the least recently used ones.</summary>
    private void TrimThumbCache()
    {
        _lastThumbTrim = DateTime.UtcNow;
        try
        {
            if (!Directory.Exists(ThumbDir)) return;
            var all = new DirectoryInfo(ThumbDir).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
            long size = all.Sum(f => f.Length);
            foreach (var f in all.OrderBy(f => f.LastAccessTimeUtc))
            {
                if (size <= ThumbCacheBytes) break;
                try { size -= f.Length; f.Delete(); } catch (IOException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogDebug(e, "Trimming thumbnails failed"); }
    }

    // ---- game saves ---------------------------------------------------------------------------------------------

    private static readonly Regex SaveZipName = new(@"^(save|safety_pre_restore)_[\w\-]+\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private string SavesDirOf(GameEntry game) => Path.Combine(_host.Paths.DataRoot, "Backups", game.Id);

    /// <summary>The save backups the PC keeps for each game (newest first); a flat list, as older phone apps expect.</summary>
    private async Task HandleSavesEndpointAsync(Dictionary<string, string> q, NetworkStream stream, CancellationToken ct)
    {
        var saves = new List<(DateTime When, object Row)>();
        foreach (var game in _host.Library.All())
        {
            var dir = new DirectoryInfo(SavesDirOf(game));
            if (!dir.Exists) continue;
            foreach (var f in dir.EnumerateFiles("*.zip").Where(f => SaveZipName.IsMatch(f.Name)))
            {
                saves.Add((f.LastWriteTimeUtc, new
                {
                    name = $"{game.Name} · {f.LastWriteTime:dd MMM yyyy, HH:mm}",
                    size = f.Length,
                    sizeFormatted = ByteSize.Format(f.Length),
                    modified = f.LastWriteTimeUtc,
                    gameId = game.Id,
                    gameName = game.Name,
                    file = f.Name,
                    kind = f.Name.StartsWith("safety_", StringComparison.OrdinalIgnoreCase) ? "safety" : "backup",
                    cloudSync = game.CloudSaveSyncEnabled
                }));
            }
        }
        await SendJsonAsync(stream, 200, "OK", saves.OrderByDescending(s => s.When).Select(s => s.Row).ToList(), ct);
    }

    private (GameEntry? Game, string? Zip, int Status, string? Error) FindSave(Dictionary<string, string> q, bool fileRequired)
    {
        var game = _host.Library.Get(q.GetValueOrDefault("gameId") ?? "");
        if (game is null) return (null, null, 404, "That game is not in the library on the PC.");
        var file = q.GetValueOrDefault("file");
        if (string.IsNullOrEmpty(file)) return fileRequired ? (game, null, 400, "Missing file.") : (game, null, 0, null);
        if (!SaveZipName.IsMatch(file) || !IsPlainName(file)) return (game, null, 400, "That is not a save backup.");
        var zip = Path.Combine(SavesDirOf(game), file);
        return File.Exists(zip) ? (game, zip, 0, null) : (game, null, 404, "That save backup is gone.");
    }

    private async Task HandleSaveDownloadAsync(Dictionary<string, string> q, Dictionary<string, string> headers, NetworkStream stream, string clientIp, CancellationToken ct)
    {
        var (_, zip, status, error) = FindSave(q, fileRequired: true);
        if (zip is null)
        {
            await SendJsonAsync(stream, status, StatusText(status), new { error }, ct);
            return;
        }
        await SendFileAsync(new FileInfo(zip), headers, stream, clientIp, "application/zip", "attachment", "download", ct);
    }

    /// <summary>Restores one backup (or, without "file", the latest from the cloud/PC). A safety copy of the current saves is made first.</summary>
    private async Task HandleSaveRestoreAsync(string method, Dictionary<string, string> q, NetworkStream stream, CancellationToken ct)
    {
        if (method != "POST")
        {
            await SendJsonAsync(stream, 405, "Method Not Allowed", new { error = "Use POST." }, ct);
            return;
        }
        var (game, zip, status, error) = FindSave(q, fileRequired: false);
        if (game is null || status != 0)
        {
            await SendJsonAsync(stream, status, StatusText(status), new { success = false, error }, ct);
            return;
        }
        if (_host.CurrentLaunch is { State: LaunchState.Running } l && l.Game.Id == game.Id)
        {
            await SendJsonAsync(stream, 409, "Conflict", new { success = false, error = $"{game.Name} is running. Close the game before restoring its saves." }, ct);
            return;
        }
        try
        {
            var r = await _host.SaveBackups.RestoreAsync(game, _host.Rclone, _host.RcloneConfig, _host.SaveRemoteFor(game), zip, ct);
            await SendJsonAsync(stream, r.Success ? 200 : 500, r.Success ? "OK" : "Internal Server Error",
                new { success = r.Success, message = r.Message, safetyBackup = r.SafetyBackup is { } s ? Path.GetFileName(s) : null }, ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await SendJsonAsync(stream, 500, "Internal Server Error", new { success = false, error = e.Message }, ct);
        }
    }
}
