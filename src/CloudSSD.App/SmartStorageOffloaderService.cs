using System.Diagnostics;
using CloudSSD.Core;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

public sealed class OffloadResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public long BytesFreed { get; set; }
    public string BytesFreedFormatted { get; set; } = "0 B";
    public int FilesMovedCount { get; set; }
    public List<string> FilesMoved { get; set; } = new();
    public string TargetPath { get; set; } = "";
    public double LaptopFreeSpaceGB { get; set; }
    public double LaptopTotalSpaceGB { get; set; }
    public bool TriggeredByThreshold { get; set; }
    /// <summary>True for a pass the auto mode started by itself, false for one the user asked for.</summary>
    public bool Automatic { get; set; }
}

/// <summary>One thing the storage auto mode did: What is the file moved to the cloud, or empty for a clean-up of old junk.</summary>
public sealed record OffloadAction(DateTimeOffset At, string What, long Bytes)
{
    public bool IsCleanup => What.Length == 0;
}

public sealed class SmartOffloadStatus
{
    public bool Enabled { get; set; }
    public double MinFreeSpaceThresholdGB { get; set; }
    public string TargetDriveName { get; set; } = "No cloud drive connected";
    public string TargetDriveLetter { get; set; } = "";
    public double LaptopFreeSpaceGB { get; set; }
    public double LaptopTotalSpaceGB { get; set; }
    public double LaptopUsedPercentage { get; set; }
    public bool IsUnderPressure { get; set; }
    public DateTimeOffset? LastOffloadTime { get; set; }
    public long TotalBytesOffloaded { get; set; }
    public string TotalBytesOffloadedFormatted { get; set; } = "0 B";
    public int UploadsFolderFileCount { get; set; }
    public long UploadsFolderBytes { get; set; }
    public string UploadsFolderSizeFormatted { get; set; } = "0 B";
}

/// <summary>
/// Monitors laptop SSD free space. When local free space drops below the configured threshold,
/// automatically shifts local files (received uploads & cache) to the mounted Cloud Drive (Z:),
/// freeing physical disk space while keeping files transparently accessible via the virtual drive.
/// </summary>
public sealed class SmartStorageOffloaderService
{
    private readonly AppHost _host;
    private readonly ILogger<SmartStorageOffloaderService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastAutoRun = DateTimeOffset.MinValue;
    private DateTimeOffset _lastTempClean = DateTimeOffset.MinValue;

    /// <summary>Old temporary files pile up slowly, so going through the whole folder more often gains nothing.</summary>
    private static readonly TimeSpan TempCleanInterval = TimeSpan.FromHours(6);

    public event Action<OffloadResult>? OffloadCompleted;
    /// <summary>Raised after an automatic clean-up removed something: number of files and bytes freed.</summary>
    public event Action<int, long>? JunkCleaned;
    /// <summary>The last thing the storage auto mode did (a move or a clean-up); null until it has done something.</summary>
    public OffloadAction? LastAction { get; private set; }

    public SmartStorageOffloaderService(AppHost host, ILogger<SmartStorageOffloaderService> log)
    {
        _host = host;
        _log = log;
    }

    public SmartOffloadStatus GetStatus()
    {
        var settings = _host.Settings.SmartOffload;
        string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var (free, total) = DiskInfo.FreeSpace(userFolder);

        double freeGb = Math.Round(free / (1024.0 * 1024.0 * 1024.0), 1);
        double totalGb = Math.Round(total / (1024.0 * 1024.0 * 1024.0), 1);
        double usedPct = total > 0 ? Math.Round(((total - free) / (double)total) * 100.0, 1) : 0;
        bool underPressure = freeGb < settings.MinFreeSpaceThresholdGB;

        // Count uploads folder
        string uploadsDir = _host.Settings.MobileBridge.UploadDirectory 
            ?? Path.Combine(userFolder, "Downloads", "Cloud-SSD_Uploads");
        int uploadCount = 0;
        long uploadBytes = 0;
        if (Directory.Exists(uploadsDir))
        {
            try
            {
                var di = new DirectoryInfo(uploadsDir);
                foreach (var f in di.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly))
                {
                    uploadCount++;
                    uploadBytes += f.Length;
                }
            }
            catch { }
        }

        // Active mount identification
        var mount = _host.Mounts.Sessions.FirstOrDefault(s => s.Id == settings.TargetMountId && s.State == MountState.Mounted)
            ?? _host.Mounts.Sessions.FirstOrDefault(s => s.State == MountState.Mounted && !s.Definition.ReadOnly);

        string driveName = mount != null ? $"{mount.Definition.DisplayName} ({mount.DriveLetter}:)" : "No cloud drive connected";
        string driveLetter = mount != null ? mount.DriveLetter.ToString() : "";

        return new SmartOffloadStatus
        {
            Enabled = settings.Enabled,
            MinFreeSpaceThresholdGB = settings.MinFreeSpaceThresholdGB,
            TargetDriveName = driveName,
            TargetDriveLetter = driveLetter,
            LaptopFreeSpaceGB = freeGb,
            LaptopTotalSpaceGB = totalGb,
            LaptopUsedPercentage = usedPct,
            IsUnderPressure = underPressure,
            LastOffloadTime = settings.LastOffloadTime,
            TotalBytesOffloaded = settings.TotalBytesOffloaded,
            TotalBytesOffloadedFormatted = ByteSize.Format(settings.TotalBytesOffloaded),
            UploadsFolderFileCount = uploadCount,
            UploadsFolderBytes = uploadBytes,
            UploadsFolderSizeFormatted = ByteSize.Format(uploadBytes)
        };
    }

    public async Task<OffloadResult> RunOffloadAsync(bool manual = false, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct))
        {
            return new OffloadResult { Success = false, Message = "An offload operation is already currently in progress." };
        }

        try
        {
            var settings = _host.Settings.SmartOffload;
            string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var (freeBefore, total) = DiskInfo.FreeSpace(userFolder);
            double freeGbBefore = Math.Round(freeBefore / (1024.0 * 1024.0 * 1024.0), 1);
            double totalGb = Math.Round(total / (1024.0 * 1024.0 * 1024.0), 1);

            bool triggered = freeGbBefore < settings.MinFreeSpaceThresholdGB;
            if (!triggered && !manual)
            {
                return new OffloadResult
                {
                    Success = true,
                    Message = $"Laptop free space ({freeGbBefore} GB) is above the safety limit ({settings.MinFreeSpaceThresholdGB} GB). No offload required.",
                    LaptopFreeSpaceGB = freeGbBefore,
                    LaptopTotalSpaceGB = totalGb,
                    TriggeredByThreshold = false
                };
            }

            // Find target mounted cloud drive
            var mount = TargetMount();

            if (mount == null || !Directory.Exists(mount.Root))
            {
                _log.LogWarning("Smart Offload: No writable Cloud Drive is mounted. Trying to mount default session...");
                return new OffloadResult
                {
                    Success = false,
                    Message = "No active Cloud Drive mounted to receive offloaded files. Please mount Cloud-SSD (Z:) first.",
                    LaptopFreeSpaceGB = freeGbBefore,
                    LaptopTotalSpaceGB = totalGb
                };
            }

            string subfolder = string.IsNullOrWhiteSpace(settings.TargetSubfolder) ? "Offloaded_Files" : settings.TargetSubfolder.Trim();
            string targetDir = Path.Combine(mount.Root, subfolder);
            Directory.CreateDirectory(targetDir);

            long bytesFreed = 0;
            var movedFiles = new List<string>();
            string? lastDestination = null;

            // An automatic pass starts no upload during a game. One already running carries on, slowed by the app.
            bool Playing() => !manual && GameActivity.IsPlaying(_host);

            async Task MoveAsync(FileInfo file, string destDir, bool leaveShortcut = false)
            {
                try
                {
                    string destFile = Path.Combine(destDir, file.Name);
                    if (File.Exists(destFile))
                    {
                        string baseName = Path.GetFileNameWithoutExtension(file.Name);
                        string ext = file.Extension;
                        destFile = Path.Combine(destDir, $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
                    }

                    long len = file.Length;
                    if (mount.Rc is { } rc)
                    {
                        // Straight to the cloud, not through the drive letter: a copy made there first lands in the
                        // drive's cache on this same disk, so nothing comes free until the cache lets go of it.
                        bool sent = await RcMoveAsync(rc, file.DirectoryName!, file.Name, mount.Definition.RemoteSpec, Path.GetRelativePath(mount.Root, destFile).Replace('\\', '/'), ct);
                        if (!sent || File.Exists(file.FullName))
                        {
                            _log.LogWarning("Could not offload file {File}; it stays on the PC", file.Name);
                            return;
                        }
                    }
                    else
                    {
                        // Copy to Cloud Drive (triggers VFS background upload) and delete local copy
                        File.Copy(file.FullName, destFile, overwrite: true);
                        File.Delete(file.FullName);
                    }

                    bytesFreed += len;
                    movedFiles.Add(file.Name);
                    lastDestination = destFile;
                    _log.LogInformation("Offloaded file '{Name}' ({Size}) to Cloud Drive {Target}", file.Name, ByteSize.Format(len), destFile);
                    // So the file can still be opened from where it was.
                    if (leaveShortcut) ShellShortcut.TryCreate(file.FullName + ".lnk", destFile, "Moved to the cloud drive by Cloud-SSD");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning(ex, "Failed to offload file {File}", file.Name);
                }
            }

            // 1. Move files from local Uploads folder to Cloud Drive
            if (settings.OffloadUploadsFolder)
            {
                string uploadsDir = _host.Settings.MobileBridge.UploadDirectory
                    ?? Path.Combine(userFolder, "Downloads", "Cloud-SSD_Uploads");

                if (Directory.Exists(uploadsDir))
                {
                    var di = new DirectoryInfo(uploadsDir);
                    foreach (var file in di.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly).ToList())
                    {
                        if (ct.IsCancellationRequested || Playing()) break;
                        await MoveAsync(file, targetDir);
                    }
                }
            }

            // 2. Old big files from Downloads: only below the limit, and only as many as it takes to get back over it
            if (settings.OffloadDownloadsFolder && triggered && mount.Rc is not null)
            {
                long needed = (long)(settings.MinFreeSpaceThresholdGB * ByteSize.GiB) - DiskInfo.FreeSpace(userFolder).FreeForUser;
                foreach (var pick in PickDownloads(DownloadsFiles(userFolder), DateTime.UtcNow, needed))
                {
                    if (ct.IsCancellationRequested || Playing()) break;
                    if (IsInUse(pick.Path)) continue;
                    await MoveAsync(new FileInfo(pick.Path), Path.Combine(targetDir, "Downloads"), leaveShortcut: true);
                }
            }

            // An automatic pass that moved nothing must leave no trace: no history line and no dropped folder caches.
            if (!manual && movedFiles.Count == 0)
            {
                _lastAutoRun = DateTimeOffset.Now;
                return new OffloadResult
                {
                    Success = true,
                    Message = "Nothing to move.",
                    LaptopFreeSpaceGB = freeGbBefore,
                    LaptopTotalSpaceGB = totalGb,
                    TriggeredByThreshold = triggered
                };
            }

            // 3. VFS Cache Eviction & Memory Purge on low space
            if (settings.EvictVfsCacheOnLowSpace)
            {
                foreach (var session in _host.Mounts.Sessions)
                {
                    try
                    {
                        if (session.Rc != null)
                        {
                            // Tell rclone to forget metadata caches and refresh VFS
                            _ = session.Rc.CallAsync("vfs/forget", null, ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug(ex, "RC vfs/forget notification ignored");
                    }
                }
            }

            // Check space after
            var (freeAfter, _) = DiskInfo.FreeSpace(userFolder);
            double freeGbAfter = Math.Round(freeAfter / (1024.0 * 1024.0 * 1024.0), 1);

            // Update persistent metrics
            settings.LastOffloadTime = DateTimeOffset.Now;
            settings.TotalBytesOffloaded += bytesFreed;
            _host.SaveSettings();

            string statusMsg = movedFiles.Count > 0
                ? $"Successfully offloaded {movedFiles.Count} file(s) ({ByteSize.Format(bytesFreed)}) to Cloud Drive {mount.DriveLetter}:. Laptop free space: {freeGbAfter} GB."
                : $"Optimized cache and cloud sync. Laptop free space: {freeGbAfter} GB.";

            var result = new OffloadResult
            {
                Success = true,
                Message = statusMsg,
                BytesFreed = bytesFreed,
                BytesFreedFormatted = ByteSize.Format(bytesFreed),
                FilesMovedCount = movedFiles.Count,
                FilesMoved = movedFiles,
                TargetPath = targetDir,
                LaptopFreeSpaceGB = freeGbAfter,
                LaptopTotalSpaceGB = totalGb,
                TriggeredByThreshold = triggered,
                Automatic = !manual
            };
            if (movedFiles.Count > 0) LastAction = new OffloadAction(DateTimeOffset.Now, HistoryTitle(movedFiles), bytesFreed);

            // Record in transfer history
            _host.MobileBridge.RecordTransfer(
                HistoryTitle(movedFiles),
                movedFiles.Count == 1 ? lastDestination : targetDir,
                bytesFreed,
                "offload",
                "Smart Storage Saver",
                "127.0.0.1",
                0,
                $"Completed • Free space {freeGbAfter} GB"
            );

            _lastAutoRun = DateTimeOffset.Now;
            OffloadCompleted?.Invoke(result);
            _log.LogInformation("Smart Offload Completed: {Message}", statusMsg);

            return result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error executing smart storage offload");
            return new OffloadResult { Success = false, Message = "Offload error: " + ex.Message };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>True when the folder holds at least one file the offload would move (top level only, like the offload).</summary>
    internal static bool HasFiles(string directory)
    {
        try { return Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly).Any(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>A Downloads file smaller than this stays on the PC: moving it frees too little to matter.</summary>
    public const long DownloadsMinBytes = 100 * ByteSize.MiB;
    /// <summary>A Downloads file downloaded or changed more recently than this stays on the PC: it is probably still needed.</summary>
    public static readonly TimeSpan DownloadsMinAge = TimeSpan.FromDays(7);

    /// <summary>Browsers and download managers keep a download under one of these names until it is complete.</summary>
    private static readonly HashSet<string> UnfinishedDownload = new(StringComparer.OrdinalIgnoreCase) { ".crdownload", ".part", ".partial", ".download", ".tmp", ".opdownload" };

    /// <summary>One file lying directly in Downloads. LastChangeUtc is the later of "downloaded" and "last written".</summary>
    internal sealed record LocalFile(string Path, long Bytes, DateTime LastChangeUtc);

    /// <summary>
    /// The Downloads files to move so that <paramref name="bytesNeeded"/> come free: big and old ones only, the
    /// biggest first, and no more of them than needed. Empty when nothing is needed.
    /// </summary>
    internal static IReadOnlyList<LocalFile> PickDownloads(IEnumerable<LocalFile> files, DateTime nowUtc, long bytesNeeded)
    {
        var picked = new List<LocalFile>();
        foreach (var f in files.Where(f => f.Bytes >= DownloadsMinBytes && nowUtc - f.LastChangeUtc >= DownloadsMinAge
                                           && !UnfinishedDownload.Contains(System.IO.Path.GetExtension(f.Path))).OrderByDescending(f => f.Bytes))
        {
            if (bytesNeeded <= 0) break;
            picked.Add(f);
            bytesNeeded -= f.Bytes;
        }
        return picked;
    }

    /// <summary>The visible files lying directly in Downloads. Its subfolders hold projects and installed things and are never touched.</summary>
    private static List<LocalFile> DownloadsFiles(string userFolder)
    {
        var list = new List<LocalFile>();
        try
        {
            var dir = new DirectoryInfo(System.IO.Path.Combine(userFolder, "Downloads"));
            if (!dir.Exists) return list;
            foreach (var f in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                list.Add(new LocalFile(f.FullName, f.Length, f.CreationTimeUtc > f.LastWriteTimeUtc ? f.CreationTimeUtc : f.LastWriteTimeUtc));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return list;
    }

    /// <summary>True when another program has the file open (a running installer, a video being played).</summary>
    private static bool IsInUse(string path)
    {
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>
    /// Has rclone move one file between a folder on the PC and the cloud (either way) and waits until it is done.
    /// rclone removes the source only after the copy is complete and checked. The move runs as an rclone job
    /// because a big file takes far longer than one rc call may. True when rclone reports success.
    /// </summary>
    private static async Task<bool> RcMoveAsync(RcClient rc, string fromFs, string fromPath, string toFs, string toPath, CancellationToken ct)
    {
        var job = await rc.CallAsync("operations/movefile", new { srcFs = fromFs, srcRemote = fromPath, dstFs = toFs, dstRemote = toPath, _async = true }, ct);
        long id = job["jobid"]?.GetValue<long>() ?? throw new InvalidOperationException("rclone did not start the move.");
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            var status = await rc.CallAsync("job/status", new { jobid = id }, ct);
            if (status["finished"]?.GetValue<bool>() == true) return status["success"]?.GetValue<bool>() == true;
        }
    }

    /// <summary>
    /// Moves one file or folder the user picked (Space &amp; RAM page) from the PC to "Offloaded_Files\From PC" on the
    /// cloud drive, straight to the cloud like the auto mode does, and can leave a shortcut where it was. The space is
    /// free on the PC as soon as this returns.
    /// </summary>
    public async Task<OffloadResult> MoveToCloudAsync(string localPath, bool leaveShortcut, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct))
            return new OffloadResult { Success = false, Message = "Another move to the cloud is still running. Try again when it is done." };
        try
        {
            var full = Path.GetFullPath(localPath).TrimEnd('\\');
            bool isDir = Directory.Exists(full);
            if (!isDir && !File.Exists(full)) return new OffloadResult { Success = false, Message = "That file or folder is not there any more." };
            if (TargetMount() is not { } mount || !Directory.Exists(mount.Root))
                return new OffloadResult { Success = false, Message = "No cloud drive is connected. Connect one first." };
            if (full.StartsWith(mount.Root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                return new OffloadResult { Success = false, Message = "That is already on the cloud drive." };

            var settings = _host.Settings.SmartOffload;
            string subfolder = string.IsNullOrWhiteSpace(settings.TargetSubfolder) ? "Offloaded_Files" : settings.TargetSubfolder.Trim();
            string destDir = Path.Combine(mount.Root, subfolder, "From PC");
            Directory.CreateDirectory(destDir);
            string name = Path.GetFileName(full);
            string dest = Path.Combine(destDir, name);
            if (File.Exists(dest) || Directory.Exists(dest))
                dest = Path.Combine(destDir, $"{Path.GetFileNameWithoutExtension(name)}_{DateTime.Now:yyyyMMdd_HHmmss}{(isDir ? "" : Path.GetExtension(name))}");
            string rel = Path.GetRelativePath(mount.Root, dest).Replace('\\', '/');

            long bytes = isDir ? new DirectoryInfo(full).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : new FileInfo(full).Length;
            var (freeBefore, _) = DiskInfo.FreeSpace(full);

            if (mount.Rc is { } rc)
            {
                bool ok;
                if (isDir)
                {
                    string spec = mount.Definition.RemoteSpec;
                    string dstFs = spec + (spec.EndsWith(':') ? "" : "/") + rel;
                    ok = await RcJobAsync(rc, "sync/move", new { srcFs = full, dstFs, deleteEmptySrcDirs = true, _async = true }, ct);
                    if (ok && Directory.Exists(full) && !Directory.EnumerateFileSystemEntries(full).Any()) Directory.Delete(full);
                    ok = ok && !Directory.Exists(full);
                }
                else ok = await RcMoveAsync(rc, Path.GetDirectoryName(full)!, name, mount.Definition.RemoteSpec, rel, ct) && !File.Exists(full);
                _ = rc.CallAsync("vfs/forget", null, ct);
                if (!ok) return new OffloadResult { Success = false, Message = $"Could not move {name} to the cloud. It stays on the PC." };
            }
            else if (isDir)
            {
                CopyTree(full, dest);
                Directory.Delete(full, recursive: true);
            }
            else
            {
                File.Copy(full, dest);
                File.Delete(full);
            }

            if (leaveShortcut) ShellShortcut.TryCreate(full + ".lnk", dest, "Moved to the cloud drive by Cloud-SSD");
            long freed = Math.Max(0, DiskInfo.FreeSpace(Path.GetPathRoot(full)!).FreeForUser - freeBefore);
            settings.TotalBytesOffloaded += bytes;
            _host.SaveSettings();
            LastAction = new OffloadAction(DateTimeOffset.Now, name, bytes);
            _log.LogInformation("Moved '{Name}' ({Size}) to the cloud: {Target}", name, ByteSize.Format(bytes), dest);
            _host.MobileBridge.RecordTransfer(name, dest, bytes, "offload", "Space & RAM", "127.0.0.1", 0, $"Completed • {ByteSize.Format(bytes)} moved to the cloud");
            return new OffloadResult
            {
                Success = true, Message = $"{name} is on the cloud drive now ({ByteSize.Format(bytes)}).", BytesFreed = Math.Max(freed, bytes),
                BytesFreedFormatted = ByteSize.Format(Math.Max(freed, bytes)), FilesMovedCount = 1, FilesMoved = [name], TargetPath = dest
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or RcException or InvalidOperationException)
        {
            _log.LogWarning(ex, "Could not move {Path} to the cloud", localPath);
            return new OffloadResult { Success = false, Message = "Could not move it to the cloud: " + ex.Message };
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }

    /// <summary>Runs one rclone command as a job and waits for it; true when rclone reports success.</summary>
    private static async Task<bool> RcJobAsync(RcClient rc, string command, object args, CancellationToken ct)
    {
        var job = await rc.CallAsync(command, args, ct);
        long id = job["jobid"]?.GetValue<long>() ?? throw new InvalidOperationException("rclone did not start the job.");
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            var status = await rc.CallAsync("job/status", new { jobid = id }, ct);
            if (status["finished"]?.GetValue<bool>() == true) return status["success"]?.GetValue<bool>() == true;
        }
    }

    /// <summary>The cloud drive files are moved to: the one chosen in the settings, else any writable mounted drive.</summary>
    private MountSession? TargetMount()
    {
        var settings = _host.Settings.SmartOffload;
        return _host.Mounts.Sessions.FirstOrDefault(s => s.Id == settings.TargetMountId && s.State == MountState.Mounted)
            ?? _host.Mounts.Sessions.FirstOrDefault(s => s.State == MountState.Mounted && !s.Definition.ReadOnly);
    }

    /// <summary>What the transfer history shows for one pass: the file's own name, not just a count.</summary>
    public static string HistoryTitle(IReadOnlyList<string> movedFiles) => movedFiles.Count switch
    {
        0 => "Storage Saver Optimization",
        1 => movedFiles[0],
        _ => $"{movedFiles[0]} + {movedFiles.Count - 1} more",
    };

    /// <summary>The folder on the cloud drive that holds what was moved out of Downloads, or null without a drive.</summary>
    private string? MovedDownloadsDirectory()
    {
        if (TargetMount() is not { } mount) return null;
        var settings = _host.Settings.SmartOffload;
        string subfolder = string.IsNullOrWhiteSpace(settings.TargetSubfolder) ? "Offloaded_Files" : settings.TargetSubfolder.Trim();
        return Path.Combine(mount.Root, subfolder, "Downloads");
    }

    /// <summary>The files that were moved from Downloads to the cloud and can be brought back, newest first.</summary>
    public IReadOnlyList<SpaceUse> ListMovedDownloads()
    {
        try
        {
            if (MovedDownloadsDirectory() is not { } dir || !Directory.Exists(dir)) return [];
            return new DirectoryInfo(dir).EnumerateFiles().OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new SpaceUse(f.FullName, f.Length)).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>
    /// Brings one file back from the cloud to Downloads and removes the shortcut that stood in for it. The file
    /// counts as new again, so the auto mode leaves it on the PC for a week.
    /// </summary>
    public async Task<OffloadResult> BringBackAsync(string cloudPath, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct))
            return new OffloadResult { Success = false, Message = "Another move is still running. Try again when it is done." };
        try
        {
            var mount = TargetMount();
            var cloud = new FileInfo(cloudPath);
            if (mount is null || MovedDownloadsDirectory() is not { } dir || !cloud.Exists
                || !string.Equals(cloud.DirectoryName, dir, StringComparison.OrdinalIgnoreCase))
                return new OffloadResult { Success = false, Message = "That file is not in the cloud folder for moved downloads any more." };

            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string local = Path.Combine(downloads, cloud.Name);
            if (File.Exists(local))
                return new OffloadResult { Success = false, Message = $"Downloads already has a file named {cloud.Name}. Rename or remove it first." };
            long bytes = cloud.Length;
            if (DiskInfo.FreeSpace(downloads).FreeForUser < bytes + ByteSize.GiB)
                return new OffloadResult { Success = false, Message = $"Not enough free space to bring back {cloud.Name} ({ByteSize.Format(bytes)})." };

            if (mount.Rc is { } rc)
            {
                string from = Path.GetRelativePath(mount.Root, cloud.FullName).Replace('\\', '/');
                if (!await RcMoveAsync(rc, mount.Definition.RemoteSpec, from, downloads, cloud.Name, ct) || !File.Exists(local))
                    return new OffloadResult { Success = false, Message = $"Could not bring back {cloud.Name}. It is still on the cloud drive." };
                _ = rc.CallAsync("vfs/forget", null, ct);
            }
            else File.Move(cloud.FullName, local);

            File.SetCreationTimeUtc(local, DateTime.UtcNow);
            if (File.Exists(local + ".lnk")) File.Delete(local + ".lnk");
            _log.LogInformation("Brought '{Name}' ({Size}) back from the cloud to {Target}", cloud.Name, ByteSize.Format(bytes), local);
            _host.MobileBridge.RecordTransfer(cloud.Name, local, bytes, "offload", "Smart Storage Saver", "127.0.0.1", 0, "Brought back to the PC");
            return new OffloadResult { Success = true, Message = $"{cloud.Name} is back in Downloads.", TargetPath = local };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or RcException or InvalidOperationException)
        {
            _log.LogWarning(ex, "Could not bring back {File}", cloudPath);
            return new OffloadResult { Success = false, Message = "Could not bring the file back: " + ex.Message };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Below the limit, junk goes first: it costs nothing to lose, unlike a file moved to the cloud. Old temporary
    /// files and crash dumps, and, when the user allows it, what has been in the Recycle Bin for a month.
    /// Runs off the caller's thread because the folders can hold tens of thousands of files.
    /// </summary>
    private async Task CleanJunkAsync()
    {
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            _lastTempClean = DateTimeOffset.Now;
            bool temp = _host.Settings.SmartOffload.CleanTempFiles, recycleBin = _host.Settings.SmartOffload.CleanRecycleBin;
            var (files, bytes) = await Task.Run(() =>
            {
                var now = DateTime.UtcNow;
                var parts = new List<TempCleanupResult>();
                if (temp)
                {
                    parts.Add(TempCleanup.Clean(TempCleanup.UserTempDirectory, now));
                    parts.Add(TempCleanup.Clean(TempCleanup.CrashDumpDirectory, now));
                }
                if (recycleBin && RecycleBinCleanup.UserBinDirectory is { } bin) parts.Add(RecycleBinCleanup.Clean(bin, now));
                return (parts.Sum(p => p.Files), parts.Sum(p => p.Bytes));
            });
            if (files == 0) return;
            _log.LogInformation("Smart Offload: removed {Files} old junk file(s), {Size}", files, ByteSize.Format(bytes));
            if (bytes == 0) return; // empty leftovers only: nothing worth a history line
            LastAction = new OffloadAction(DateTimeOffset.Now, "", bytes);
            JunkCleaned?.Invoke(files, bytes);
            _host.MobileBridge.RecordTransfer($"Auto clean-up ({files} old files)", TempCleanup.UserTempDirectory, bytes,
                "offload", "Smart Storage Saver", "127.0.0.1", 0, $"Completed • {ByteSize.Format(bytes)} freed");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CheckAndOffloadIfNeededAsync()
    {
        var settings = _host.Settings.SmartOffload;
        if (!settings.Enabled) return;
        if (_gate.CurrentCount == 0) return; // a move is still uploading

        // Enforce 3 minute cooldown between automated passes
        if (DateTimeOffset.Now - _lastAutoRun < TimeSpan.FromMinutes(3)) return;

        try
        {
            string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var (free, _) = DiskInfo.FreeSpace(userFolder);
            double freeGb = free / (1024.0 * 1024.0 * 1024.0);

            if (freeGb < settings.MinFreeSpaceThresholdGB)
            {
                // Cleaning and uploading take disk and internet a running game or stream needs; they can wait.
                if (GameActivity.IsPlaying(_host)) return;

                if ((settings.CleanTempFiles || settings.CleanRecycleBin) && DateTimeOffset.Now - _lastTempClean >= TempCleanInterval)
                {
                    await CleanJunkAsync();
                    if (DiskInfo.FreeSpace(userFolder).FreeForUser / (1024.0 * 1024.0 * 1024.0) >= settings.MinFreeSpaceThresholdGB) return;
                }

                // Below the limit with nothing to move: stay quiet. Running anyway would drop the drives' folder
                // caches and add a history line every few minutes without freeing a single byte.
                string uploadsDir = _host.Settings.MobileBridge.UploadDirectory ?? Path.Combine(userFolder, "Downloads", "Cloud-SSD_Uploads");
                bool uploads = settings.OffloadUploadsFolder && HasFiles(uploadsDir);
                bool downloads = settings.OffloadDownloadsFolder && PickDownloads(DownloadsFiles(userFolder), DateTime.UtcNow, 1).Count > 0;
                if (!uploads && !downloads) return;

                _log.LogWarning("Laptop free disk space ({Free:0.0} GB) is below threshold ({Limit:0.0} GB). Triggering automated Smart Offload to Cloud Drive...",
                    freeGb, settings.MinFreeSpaceThresholdGB);
                
                await RunOffloadAsync(manual: false);
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Error in background smart offload check");
        }
    }
}
