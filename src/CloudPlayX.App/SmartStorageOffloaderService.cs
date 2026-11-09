using System.Diagnostics;
using CloudPlayX.Core;
using CloudPlayX.Mount;
using Microsoft.Extensions.Logging;

namespace CloudPlayX.App;

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
}

public sealed class SmartOffloadStatus
{
    public bool Enabled { get; set; }
    public double MinFreeSpaceThresholdGB { get; set; }
    public string TargetDriveName { get; set; } = "Cloud-SSD (15TB) (Z:)";
    public string TargetDriveLetter { get; set; } = "Z";
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

    public event Action<OffloadResult>? OffloadCompleted;

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

        string driveName = mount != null ? $"{mount.Definition.DisplayName} ({mount.DriveLetter}:)" : "Cloud-SSD (15TB) (Z:)";
        string driveLetter = mount != null ? mount.DriveLetter.ToString() : "Z";

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
            var mount = _host.Mounts.Sessions.FirstOrDefault(s => s.Id == settings.TargetMountId && s.State == MountState.Mounted)
                ?? _host.Mounts.Sessions.FirstOrDefault(s => s.State == MountState.Mounted && !s.Definition.ReadOnly);

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

            // 1. Move files from local Uploads folder to Cloud Drive
            if (settings.OffloadUploadsFolder)
            {
                string uploadsDir = _host.Settings.MobileBridge.UploadDirectory 
                    ?? Path.Combine(userFolder, "Downloads", "Cloud-SSD_Uploads");

                if (Directory.Exists(uploadsDir))
                {
                    var di = new DirectoryInfo(uploadsDir);
                    foreach (var file in di.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly))
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            string destFile = Path.Combine(targetDir, file.Name);
                            if (File.Exists(destFile))
                            {
                                string baseName = Path.GetFileNameWithoutExtension(file.Name);
                                string ext = file.Extension;
                                destFile = Path.Combine(targetDir, $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
                            }

                            long len = file.Length;
                            // Copy to Cloud Drive (triggers VFS background upload) and delete local copy
                            File.Copy(file.FullName, destFile, overwrite: true);
                            File.Delete(file.FullName);

                            bytesFreed += len;
                            movedFiles.Add(file.Name);
                            _log.LogInformation("Offloaded file '{Name}' ({Size}) to Cloud Drive {Target}", file.Name, ByteSize.Format(len), destFile);
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Failed to offload file {File}", file.Name);
                        }
                    }
                }
            }

            // 2. VFS Cache Eviction & Memory Purge on low space
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
                TriggeredByThreshold = triggered
            };

            // Record in transfer history
            _host.MobileBridge.RecordTransfer(
                movedFiles.Count > 0 ? $"Smart Offload ({movedFiles.Count} files)" : "Storage Saver Optimization",
                targetDir,
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

    public async Task CheckAndOffloadIfNeededAsync()
    {
        var settings = _host.Settings.SmartOffload;
        if (!settings.Enabled) return;

        // Enforce 3 minute cooldown between automated passes
        if (DateTimeOffset.Now - _lastAutoRun < TimeSpan.FromMinutes(3)) return;

        try
        {
            string userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var (free, _) = DiskInfo.FreeSpace(userFolder);
            double freeGb = free / (1024.0 * 1024.0 * 1024.0);

            if (freeGb < settings.MinFreeSpaceThresholdGB)
            {
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
