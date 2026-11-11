using System.IO.Compression;
using System.Text.RegularExpressions;
using CloudSSD.Core;
using CloudSSD.Diagnostics;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging;

namespace CloudSSD.Games;

public sealed record SaveBackupInfo(string FileName, DateTime Timestamp, long SizeBytes, bool IsCloud);

public sealed record BackupResult(bool Success, string Message, string? BackupFile = null, int FileCount = 0, long TotalBytes = 0)
{
    public static BackupResult Skipped(string reason) => new(false, reason);
    public static BackupResult Ok(string file, int files, long bytes) => new(true, $"Successfully backed up {files} save file(s) ({ByteSize.Format(bytes)})", file, files, bytes);
}

public sealed record RestoreResult(bool Success, string Message, string? SafetyBackup = null)
{
    public static RestoreResult Failed(string reason) => new(false, reason);
    public static RestoreResult Ok(string msg, string? safety) => new(true, msg, safety);
}

/// <summary>
/// Manages automatic detection, snapshotting, cloud synchronization, and restoration of game save data.
/// Always protects local saves with safety snapshots before any restore operation.
/// </summary>
public sealed class SaveBackupService
{
    private readonly string _backupRoot;
    private readonly ILogger _log;

    public SaveBackupService(string appDataDirectory, ILogger<SaveBackupService> log)
    {
        _backupRoot = Path.Combine(appDataDirectory, "Backups");
        _log = log;
        Directory.CreateDirectory(_backupRoot);
    }

    public static string SanitizeName(string name) =>
        Regex.Replace(name, @"[^\w\-\.]+", "_").Trim('_');

    /// <summary>
    /// Scans standard Windows gaming save directories to detect the active save directory for this game.
    /// </summary>
    public string? DetectSavePath(GameEntry game)
    {
        if (!string.IsNullOrWhiteSpace(game.CustomSavePath) && Directory.Exists(game.CustomSavePath))
            return game.CustomSavePath;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { game.Name };
        if (!string.IsNullOrWhiteSpace(game.Executable))
        {
            names.Add(Path.GetFileNameWithoutExtension(game.Executable));
        }
        var cleanName = SanitizeName(game.Name);
        names.Add(cleanName);
        names.Add(game.Name.Replace(" ", ""));

        var candidateBases = new[]
        {
            Path.Combine(userProfile, "Saved Games"),
            Path.Combine(documents, "My Games"),
            documents,
            localAppData,
            appData
        };

        foreach (var baseDir in candidateBases)
        {
            if (!Directory.Exists(baseDir)) continue;
            foreach (var name in names)
            {
                var candidate = Path.Combine(baseDir, name);
                if (Directory.Exists(candidate) && HasSaveFiles(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static bool HasSaveFiles(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true })
                .Any(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext is ".sav" or ".dat" or ".json" or ".bin" or ".ini" or ".db" or ".xml" or ".save" || f.Contains("save", StringComparison.OrdinalIgnoreCase);
                });
        }
        catch { return false; }
    }

    /// <summary>
    /// Backs up save files into a compressed local archive and uploads it to the connected cloud storage.
    /// Keeps a rotating window of the last 5 backups.
    /// </summary>
    public async Task<BackupResult> BackupAsync(GameEntry game, RcloneInfo? rclone, RcloneConfigService? rcloneConfig, string? remoteName, CancellationToken ct = default)
    {
        var savePath = DetectSavePath(game);
        if (string.IsNullOrWhiteSpace(savePath) || !Directory.Exists(savePath))
            return BackupResult.Skipped("No save directory detected or configured.");

        var gameBackupDir = Path.Combine(_backupRoot, game.Id);
        Directory.CreateDirectory(gameBackupDir);

        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipName = $"save_{game.Id}_{ts}.zip";
        var localZip = Path.Combine(gameBackupDir, zipName);

        int fileCount = 0;
        long totalBytes = 0;

        try
        {
            var files = Directory.GetFiles(savePath, "*", SearchOption.AllDirectories);
            fileCount = files.Length;
            totalBytes = files.Sum(f => new FileInfo(f).Length);
            if (fileCount == 0) return BackupResult.Skipped("Save directory is empty.");

            ZipFile.CreateFromDirectory(savePath, localZip, CompressionLevel.Optimal, false);
            _log.LogInformation("Created local save backup for {Game} at {Path} ({Files} files, {Bytes} bytes)", game.Name, localZip, fileCount, totalBytes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to create local zip backup for {Game}", game.Name);
            return BackupResult.Skipped($"Failed to compress save files: {ex.Message}");
        }

        // Upload to cloud remote if rclone is available
        if (rclone != null && rcloneConfig != null && !string.IsNullOrWhiteSpace(remoteName))
        {
            try
            {
                var safeGame = SanitizeName(game.Name);
                var runner = rcloneConfig.CreateRunner(rclone);
                var remoteTarget = $"{remoteName}:Cloud-SSD-Saves/{safeGame}/{zipName}";
                var latestTarget = $"{remoteName}:Cloud-SSD-Saves/{safeGame}/latest.zip";

                var r1 = await runner.RunAsync(["copyto", localZip, remoteTarget], TimeSpan.FromMinutes(3), ct);
                if (r1.Success)
                {
                    await runner.RunAsync(["copyto", localZip, latestTarget], TimeSpan.FromMinutes(1), ct);
                    _log.LogInformation("Uploaded save backup to {RemoteTarget}", remoteTarget);
                }
                else
                {
                    _log.LogWarning("Cloud save upload stderr: {Err}", Redactor.Redact(r1.StdErr));
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not upload save backup to cloud remote");
            }
        }

        // Cleanup local older backups (keep last 5)
        try
        {
            var older = Directory.GetFiles(gameBackupDir, "save_*.zip")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .Skip(5);
            foreach (var old in older)
            {
                try { old.Delete(); } catch { }
            }
        }
        catch { }

        game.LastSaveBackup = DateTime.Now;
        game.SaveBackupCount++;
        return BackupResult.Ok(localZip, fileCount, totalBytes);
    }

    /// <summary>
    /// Restores the most recent save game archive. Takes a safety snapshot of the local save folder first.
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(GameEntry game, RcloneInfo? rclone, RcloneConfigService? rcloneConfig, string? remoteName, string? specificZipPath = null, CancellationToken ct = default)
    {
        var savePath = DetectSavePath(game);
        if (string.IsNullOrWhiteSpace(savePath))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            savePath = Path.Combine(userProfile, "Saved Games", game.Name);
            Directory.CreateDirectory(savePath);
        }

        var gameBackupDir = Path.Combine(_backupRoot, game.Id);
        Directory.CreateDirectory(gameBackupDir);

        string? safetyZip = null;
        if (Directory.Exists(savePath) && Directory.GetFileSystemEntries(savePath).Length > 0)
        {
            safetyZip = Path.Combine(gameBackupDir, $"safety_pre_restore_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            try
            {
                ZipFile.CreateFromDirectory(savePath, safetyZip, CompressionLevel.Fastest, false);
                _log.LogInformation("Safety snapshot saved before restore: {Path}", safetyZip);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to create safety snapshot before restore");
            }
        }

        string sourceZip = specificZipPath ?? "";
        if (string.IsNullOrWhiteSpace(sourceZip) || !File.Exists(sourceZip))
        {
            // Try downloading latest.zip from cloud
            if (rclone != null && rcloneConfig != null && !string.IsNullOrWhiteSpace(remoteName))
            {
                var safeGame = SanitizeName(game.Name);
                var runner = rcloneConfig.CreateRunner(rclone);
                var downloadTarget = Path.Combine(gameBackupDir, "cloud_latest.zip");
                var remoteSource = $"{remoteName}:Cloud-SSD-Saves/{safeGame}/latest.zip";

                var r = await runner.RunAsync(["copyto", remoteSource, downloadTarget], TimeSpan.FromMinutes(3), ct);
                if (!r.Success || !File.Exists(downloadTarget))
                {
                    // Fallback to legacy path
                    var legacySource = $"{remoteName}:CloudPlayX-Saves/{safeGame}/latest.zip";
                    r = await runner.RunAsync(["copyto", legacySource, downloadTarget], TimeSpan.FromMinutes(3), ct);
                }
                if (r.Success && File.Exists(downloadTarget))
                {
                    sourceZip = downloadTarget;
                }
            }

            // Fallback to latest local backup
            if (string.IsNullOrWhiteSpace(sourceZip) || !File.Exists(sourceZip))
            {
                var latestLocal = Directory.GetFiles(gameBackupDir, "save_*.zip")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .FirstOrDefault();
                if (latestLocal != null) sourceZip = latestLocal.FullName;
            }
        }

        if (string.IsNullOrWhiteSpace(sourceZip) || !File.Exists(sourceZip))
            return RestoreResult.Failed("No save backup archive found to restore.");

        try
        {
            Directory.CreateDirectory(savePath);
            ZipFile.ExtractToDirectory(sourceZip, savePath, overwriteFiles: true);
            _log.LogInformation("Restored save backup from {Zip} to {Target}", sourceZip, savePath);
            return RestoreResult.Ok($"Save files successfully restored from backup to {savePath}", safetyZip);
        }
        catch (Exception ex)
        {
            return RestoreResult.Failed($"Failed to extract save archive: {ex.Message}");
        }
    }

    /// <summary>
    /// Lists all local and cloud backup points available for a game.
    /// </summary>
    public async Task<IReadOnlyList<SaveBackupInfo>> ListBackupsAsync(GameEntry game, RcloneInfo? rclone, RcloneConfigService? rcloneConfig, string? remoteName, CancellationToken ct = default)
    {
        var list = new List<SaveBackupInfo>();
        var gameBackupDir = Path.Combine(_backupRoot, game.Id);

        if (Directory.Exists(gameBackupDir))
        {
            foreach (var f in Directory.GetFiles(gameBackupDir, "save_*.zip"))
            {
                var fi = new FileInfo(f);
                list.Add(new SaveBackupInfo(fi.Name, fi.CreationTime, fi.Length, false));
            }
        }

        if (rclone != null && rcloneConfig != null && !string.IsNullOrWhiteSpace(remoteName))
        {
            try
            {
                var safeGame = SanitizeName(game.Name);
                var items = await rcloneConfig.ListDirectoryAsync(rclone, remoteName, $"Cloud-SSD-Saves/{safeGame}", ct);
                if (items.Count == 0)
                {
                    items = await rcloneConfig.ListDirectoryAsync(rclone, remoteName, $"CloudPlayX-Saves/{safeGame}", ct);
                }
                foreach (var item in items.Where(x => !x.IsDir && x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!list.Any(x => x.FileName == item.Name))
                    {
                        list.Add(new SaveBackupInfo(item.Name, item.ModTime?.DateTime ?? DateTime.Now, item.Size, true));
                    }
                }
            }
            catch { }
        }

        return list.OrderByDescending(x => x.Timestamp).ToList();
    }
}
