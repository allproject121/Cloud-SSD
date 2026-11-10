using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CloudPlayX.App;
using CloudPlayX.Cache;
using CloudPlayX.Core;
using CloudPlayX.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CloudPlayX.Desktop.ViewModels;

public sealed record CachedFileRow(string Mount, string Path, string Cached, string Physical, string Logical, string LastAccess, bool Dirty, double Fraction);

/// <summary>Smart Cache: budget, protected free space, usage (physical vs logical), pinning and safe cleanup.</summary>
public sealed partial class SmartCacheViewModel : PageViewModel
{
    public SmartCacheViewModel(AppHost host) : base(host, "Smart Cache", "") { }

    public ObservableCollection<CachedFileRow> Files { get; } = new();
    public ObservableCollection<string> Pinned { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();
    public ObservableCollection<KeyValueRow> Usage { get; } = new();
    public int[] Presets { get; } = [10, 20, 30, 40];
    public int[] OffloadPresets { get; } = [10, 15, 20, 30];

    [ObservableProperty] private string _budgetGb = "20";
    [ObservableProperty] private string _reserveGb = "8";
    [ObservableProperty] private string _maxAgeDays = "30";
    [ObservableProperty] private string _cacheRoot = "";
    [ObservableProperty] private string _volumeInfo = "";
    [ObservableProperty] private string _effective = "";
    [ObservableProperty] private CachedFileRow? _selectedFile;

    [ObservableProperty] private bool _autoOffloadEnabled = true;
    [ObservableProperty] private string _offloadThresholdGb = "15";
    [ObservableProperty] private string _laptopFreeGb = "-- GB";
    [ObservableProperty] private string _laptopTotalGb = "-- GB";
    [ObservableProperty] private string _laptopDiskSummary = "Checking storage...";
    [ObservableProperty] private double _laptopDiskPercent = 0;
    [ObservableProperty] private bool _isUnderThreshold;
    [ObservableProperty] private string _offloadStatusText = "Monitoring Active";
    [ObservableProperty] private string _offloadStatusTone = "ok";
    [ObservableProperty] private string _targetDriveDisplayName = "No cloud drive connected";
    [ObservableProperty] private string _lastOffloadFormatted = "Never";
    [ObservableProperty] private string _totalOffloadedFormatted = "0 B";
    [ObservableProperty] private string _uploadsPendingSummary = "0 files (0 B)";

    public override void OnShown()
    {
        var c = Host.Settings.Cache;
        BudgetGb = (c.BudgetBytes / (double)ByteSize.GiB).ToString("0.#", CultureInfo.InvariantCulture);
        ReserveGb = (c.MinFreeSpaceBytes / (double)ByteSize.GiB).ToString("0.#", CultureInfo.InvariantCulture);
        MaxAgeDays = c.MaxAge.TotalDays.ToString("0", CultureInfo.InvariantCulture);
        CacheRoot = Host.Paths.CacheRootFor(Host.Settings);
        Pinned.ReplaceWith(c.PinnedPaths);
        RefreshOffloadStatus();
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            var root = Host.Paths.CacheRootFor(Host.Settings);
            var (usage, files, vol) = await Task.Run(() =>
            {
                Directory.CreateDirectory(root);
                var rows = new List<CachedFileRow>();
                long phys = 0, logical = 0, cached = 0;
                int dirty = 0;
                foreach (var m in Host.Settings.Mounts)
                {
                    var dir = Host.Paths.MountCacheDir(Host.Settings, m.Id);
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in CacheInspector.Enumerate(dir, m.RemotePath))
                    {
                        phys += f.PhysicalBytes;
                        logical += f.LogicalSize;
                        cached += f.CachedBytes;
                        if (f.Dirty) dirty++;
                        rows.Add(new CachedFileRow(m.DisplayName, f.RemotePath, $"{f.CachedFraction:P0} ({ByteSize.Format(f.CachedBytes)})", ByteSize.Format(f.PhysicalBytes),
                            ByteSize.Format(f.LogicalSize), f.LastAccess == default ? "—" : f.LastAccess.ToString("g"), f.Dirty, f.CachedFraction));
                    }
                }
                var (free, total) = DiskInfo.FreeSpace(root);
                var (fs, sparse) = DiskInfo.VolumeFileSystem(root);
                return ((phys, logical, cached, dirty, free, total), rows, $"{DiskInfo.VolumeRoot(root)} · {fs}{(sparse ? " (sparse files supported)" : " — sparse files NOT supported")} · {ByteSize.Format(free)} free of {ByteSize.Format(total)}");
            });
            VolumeInfo = vol;
            Files.ReplaceWith(files.OrderByDescending(f => f.LastAccess).Take(500));
            var decision = DiskSafetyGuard.Decide(Host.Settings.Cache.BudgetBytes, Host.Settings.Cache.MinFreeSpaceBytes, usage.free, usage.phys,
                Math.Max(1, Host.Settings.Mounts.Count(m => m.AutoMount)));
            Effective = $"rclone limit per drive: {ByteSize.Format(decision.EffectiveMaxBytes)}" + (decision.BudgetReduced ? " (reduced to protect free space)" : "");
            Warnings.ReplaceWith(decision.Warnings.Append(
                "rclone's size limit is not an instantaneous quota: files that are open are never evicted and cleanup runs every " +
                $"{Host.Settings.Cache.PollInterval.TotalSeconds:0} s, so usage can briefly exceed it. Cloud Drive keeps a {DiskSafetyGuard.OverrunMargin:P0} margin and pauses its own prefetching under disk pressure."));
            Usage.ReplaceWith([
                new("On disk (physical)", ByteSize.Format(usage.phys)),
                new("Downloaded ranges", ByteSize.Format(usage.cached)),
                new("Logical size of cached files", ByteSize.Format(usage.logical)),
                new("Files with pending uploads", usage.dirty.ToString(), usage.dirty > 0 ? "warn" : "muted"),
                new("Space saved vs full files", usage.logical > 0 ? $"{1 - usage.phys / (double)Math.Max(1, usage.logical):P0}" : "—"),
            ]);
        });
    }

    [RelayCommand] private void Preset(int gb) => BudgetGb = gb.ToString(CultureInfo.InvariantCulture);

    [RelayCommand]
    private void ChooseLocation()
    {
        var d = new OpenFolderDialog { Title = "Choose a folder on an NTFS drive for the Cloud Drive cache", InitialDirectory = CacheRoot };
        if (d.ShowDialog() == true) CacheRoot = Path.Combine(d.FolderName, d.FolderName.EndsWith("Cloud Drive-cache", StringComparison.OrdinalIgnoreCase) ? "" : "Cloud Drive-cache");
    }

    [RelayCommand]
    private void Apply()
    {
        if (!double.TryParse(BudgetGb, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) || b < 1
            || !double.TryParse(ReserveGb, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) || r < 2
            || !int.TryParse(MaxAgeDays, out var age) || age < 1)
        {
            Say("Enter a budget of at least 1 GB, a reserve of at least 2 GB and a maximum age of at least 1 day.", "error");
            return;
        }
        var c = Host.Settings.Cache;
        var newRoot = CacheRoot.Trim();
        var rootChanged = !string.Equals(newRoot, Host.Paths.CacheRootFor(Host.Settings), StringComparison.OrdinalIgnoreCase);
        if (rootChanged && Host.Mounts.Sessions.Count > 0)
        {
            Say("Unmount all drives before moving the cache location (rclone owns the cache while mounted).", "warn");
            return;
        }
        if (rootChanged)
        {
            Directory.CreateDirectory(newRoot);
            var (fs, sparse) = DiskInfo.VolumeFileSystem(newRoot);
            if (!sparse && !Confirm($"The selected drive uses {fs} without sparse-file support; partially downloaded files will occupy their full size. Use it anyway?")) return;
            c.CacheRoot = newRoot;
        }
        c.BudgetBytes = (long)(b * ByteSize.GiB);
        c.MinFreeSpaceBytes = (long)(r * ByteSize.GiB);
        c.MaxAge = TimeSpan.FromDays(age);
        Host.SaveSettings();
        Host.RefreshDependencies();
        Say(Host.Mounts.Sessions.Count > 0 ? "Saved. Remount drives for rclone to apply the new limits." : "Saved.", "ok");
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void Pin()
    {
        if (SelectedFile is null) return;
        var m = Host.Settings.Mounts.FirstOrDefault(x => x.DisplayName == SelectedFile.Mount);
        if (m is null) return;
        var key = $"{m.Id}:{SelectedFile.Path}";
        if (!Host.Settings.Cache.PinnedPaths.Contains(key)) Host.Settings.Cache.PinnedPaths.Add(key);
        Host.SaveSettings();
        Pinned.ReplaceWith(Host.Settings.Cache.PinnedPaths);
        Say("Pinned. Cloud Drive will keep this file recently used so rclone evicts it last (best effort).", "ok");
    }

    [RelayCommand]
    private void Unpin(string? key)
    {
        if (key is null) return;
        Host.Settings.Cache.PinnedPaths.Remove(key);
        Host.SaveSettings();
        Pinned.ReplaceWith(Host.Settings.Cache.PinnedPaths);
    }

    [RelayCommand]
    private void ClearCache()
    {
        var mounted = Host.Mounts.Sessions.Select(s => s.Id).ToHashSet();
        var targets = Host.Settings.Mounts.Where(m => !mounted.Contains(m.Id)).ToList();
        if (targets.Count == 0)
        {
            Say("Unmount a drive first; its cache can only be cleared while rclone is not using it.", "warn");
            return;
        }
        if (!Confirm($"Delete the local cache of {targets.Count} unmounted drive(s)?\n\nOnly cached copies are removed. Cloud files and save games are never touched. Files with pending uploads block the cleanup.")) return;
        var msgs = new List<string>();
        foreach (var m in targets)
        {
            var r = CacheMaintenance.ClearMountCache(Host.Paths.MountCacheDir(Host.Settings, m.Id), Host.Paths.CacheRootFor(Host.Settings));
            msgs.Add($"{m.DisplayName}: {r.Message}");
        }
        Say(string.Join(" ", msgs), "ok");
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void WarmPinned()
    {
        Host.Pins.Touch();
        Say($"Touched {Host.Pins.LastTouched} pinned file(s).", "ok");
    }

    public void RefreshOffloadStatus()
    {
        var s = Host.Offloader.GetStatus();
        AutoOffloadEnabled = s.Enabled;
        OffloadThresholdGb = s.MinFreeSpaceThresholdGB.ToString("0.#", CultureInfo.InvariantCulture);
        LaptopFreeGb = $"{s.LaptopFreeSpaceGB} GB";
        LaptopTotalGb = $"{s.LaptopTotalSpaceGB} GB";
        LaptopDiskPercent = s.LaptopUsedPercentage;
        LaptopDiskSummary = $"{s.LaptopFreeSpaceGB} GB free of {s.LaptopTotalSpaceGB} GB ({s.LaptopUsedPercentage}% used)";
        IsUnderThreshold = s.IsUnderPressure;
        OffloadStatusText = s.IsUnderPressure 
            ? $"⚠️ Low Storage Alert: Free space below {s.MinFreeSpaceThresholdGB} GB limit!" 
            : $"✓ Storage Safe: {s.LaptopFreeSpaceGB} GB free (Threshold: {s.MinFreeSpaceThresholdGB} GB)";
        OffloadStatusTone = s.IsUnderPressure ? "warn" : "ok";
        TargetDriveDisplayName = s.TargetDriveName;
        LastOffloadFormatted = s.LastOffloadTime.HasValue ? s.LastOffloadTime.Value.ToLocalTime().ToString("MMM dd, HH:mm") : "None yet";
        TotalOffloadedFormatted = s.TotalBytesOffloadedFormatted;
        UploadsPendingSummary = $"{s.UploadsFolderFileCount} file(s) ({s.UploadsFolderSizeFormatted}) in Uploads folder";
    }

    [RelayCommand] private void PresetOffload(int gb) => OffloadThresholdGb = gb.ToString(CultureInfo.InvariantCulture);

    [RelayCommand]
    private async Task OffloadNowAsync()
    {
        Say("Starting Smart Offload to Cloud Drive…", "info");
        var res = await Host.Offloader.RunOffloadAsync(manual: true);
        Say(res.Message, res.Success ? "ok" : "warn");
        RefreshOffloadStatus();
    }

    [RelayCommand]
    private void SaveOffloadSettings()
    {
        if (!double.TryParse(OffloadThresholdGb, NumberStyles.Float, CultureInfo.InvariantCulture, out var th) || th < 1)
        {
            Say("Enter a valid threshold limit of at least 1 GB.", "error");
            return;
        }
        Host.Settings.SmartOffload.Enabled = AutoOffloadEnabled;
        Host.Settings.SmartOffload.MinFreeSpaceThresholdGB = Math.Round(th, 1);
        Host.SaveSettings();
        Say($"Saved. When laptop free space drops below {th:0.#} GB, files will auto-shift to Cloud Drive.", "ok");
        RefreshOffloadStatus();
    }
}

/// <summary>Settings: network, prediction, startup, rclone and advanced VFS tuning.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    public SmartCacheViewModel Storage { get; }
    public AcceleratorViewModel Accelerator { get; }
    public PerformanceViewModel Performance { get; }
    public DiagnosticsViewModel Diagnostics { get; }

    [ObservableProperty] private string _activeTab = "General";
    [ObservableProperty] private object? _currentContent;

    public SettingsViewModel(AppHost host) : base(host, "Settings", "")
    {
        Storage = new SmartCacheViewModel(host);
        Accelerator = new AcceleratorViewModel(host);
        Performance = new PerformanceViewModel(host);
        Diagnostics = new DiagnosticsViewModel(host);
        ActiveTab = "General";
    }

    [RelayCommand]
    public void SwitchSubTab(string tab)
    {
        ActiveTab = tab;
        CurrentContent = tab switch
        {
            "Storage" => Storage,
            "Speed" => Accelerator,
            "Performance" => Performance,
            "Advanced" => Diagnostics,
            _ => null
        };

        if (tab == "Storage") Storage.OnShown();
        else if (tab == "Speed") Accelerator.OnShown();
        else if (tab == "Performance") Performance.OnShown();
        else if (tab == "Advanced") Diagnostics.OnShown();
    }

    [ObservableProperty] private string _declaredMbps = "70";
    [ObservableProperty] private string _measured = "";
    [ObservableProperty] private string _backgroundShare = "60";
    [ObservableProperty] private string _hardLimitMbps = "0";
    [ObservableProperty] private string _tpsLimit = "10";
    [ObservableProperty] private string _maxConcurrency = "4";
    [ObservableProperty] private bool _predictionEnabled;
    [ObservableProperty] private bool _readProfiling;
    [ObservableProperty] private string _minConfidence = "0.40";
    [ObservableProperty] private string _sessionBudgetGb = "4";
    [ObservableProperty] private bool _restoreMounts;
    [ObservableProperty] private bool _keepMounts;
    [ObservableProperty] private bool _encryptConfig;
    [ObservableProperty] private string _rclonePath = "";
    [ObservableProperty] private string _rcloneInfo = "";
    [ObservableProperty] private string _localGamesRoot = "";
    [ObservableProperty] private string _bufferSize = "0";
    [ObservableProperty] private string _readAhead = "0";
    [ObservableProperty] private string _chunkSize = "4Mi";
    [ObservableProperty] private string _chunkStreams = "0";
    [ObservableProperty] private string _writeBackSeconds = "5";
    [ObservableProperty] private string _dataRoot = "";

    public override void OnShown()
    {
        var s = Host.Settings;
        DeclaredMbps = s.Network.DeclaredDownloadMbps.ToString(CultureInfo.InvariantCulture);
        Measured = s.Network.MeasuredDownloadMbps is { } m
            ? $"Last measured: {m:0.0} Mbps ({Bandwidth.BytesPerSecondToMBps(Bandwidth.MbpsToBytesPerSecond(m)):0.00} MB/s) on {s.Network.MeasuredAt:g} — used instead of the declared value."
            : "Not measured yet (Optimize & Prepare measures real provider throughput).";
        BackgroundShare = (s.Network.BackgroundShare * 100).ToString("0");
        HardLimitMbps = s.Network.HardLimitMbps.ToString(CultureInfo.InvariantCulture);
        TpsLimit = s.Network.TpsLimit.ToString(CultureInfo.InvariantCulture);
        MaxConcurrency = s.Network.MaxPrefetchConcurrency.ToString();
        PredictionEnabled = s.Streaming.PredictionEnabled;
        ReadProfiling = s.Streaming.ReadProfilingEnabled;
        MinConfidence = s.Streaming.MinConfidence.ToString("0.00", CultureInfo.InvariantCulture);
        SessionBudgetGb = (s.Streaming.SessionPrefetchBudgetBytes / (double)ByteSize.GiB).ToString("0.#", CultureInfo.InvariantCulture);
        RestoreMounts = s.RestoreMountsOnStartup;
        KeepMounts = s.KeepMountsAfterExit;
        EncryptConfig = s.EncryptRcloneConfig;
        RclonePath = s.RclonePathOverride ?? "";
        RcloneInfo = Host.Rclone is { } r ? $"{r.Version} · {r.Path} · config {(Host.RcloneConfig.IsEncrypted ? "encrypted (key protected by Windows DPAPI)" : "not encrypted")}" : "rclone not found";
        LocalGamesRoot = Host.Paths.LocalGamesRootFor(s);
        BufferSize = ByteSize.ToRclone(s.Cache.BufferSizeBytes);
        ReadAhead = ByteSize.ToRclone(s.Cache.ReadAheadBytes);
        ChunkSize = ByteSize.ToRclone(s.Cache.ReadChunkSizeBytes);
        ChunkStreams = s.Cache.ReadChunkStreams.ToString();
        WriteBackSeconds = s.Cache.WriteBack.TotalSeconds.ToString("0");
        DataRoot = Host.Paths.DataRoot;
        Storage.OnShown();
        Accelerator.OnShown();
        Performance.OnShown();
        Diagnostics.OnShown();
    }

    public override void OnStateChanged()
    {
        Storage.OnStateChanged();
        Accelerator.OnStateChanged();
        Performance.OnStateChanged();
        Diagnostics.OnStateChanged();
    }

    public override void OnMetrics(EngineMetrics m)
    {
        Storage.OnMetrics(m);
        Accelerator.OnMetrics(m);
        Performance.OnMetrics(m);
        Diagnostics.OnMetrics(m);
    }

    [RelayCommand]
    private async Task Save()
    {
        var s = Host.Settings;
        try
        {
            s.Network.DeclaredDownloadMbps = Parse(DeclaredMbps, 1, 100_000, "Internet speed");
            s.Network.BackgroundShare = Parse(BackgroundShare, 5, 100, "Background share") / 100.0;
            s.Network.HardLimitMbps = Parse(HardLimitMbps, 0, 100_000, "Bandwidth limit");
            s.Network.TpsLimit = Parse(TpsLimit, 0, 1000, "API transactions/s");
            s.Network.MaxPrefetchConcurrency = (int)Parse(MaxConcurrency, 1, 16, "Prefetch concurrency");
            s.Streaming.PredictionEnabled = PredictionEnabled;
            s.Streaming.ReadProfilingEnabled = ReadProfiling;
            s.Streaming.MinConfidence = Parse(MinConfidence, 0.05, 0.99, "Minimum confidence");
            s.Streaming.SessionPrefetchBudgetBytes = (long)(Parse(SessionBudgetGb, 0, 1000, "Session prefetch budget") * ByteSize.GiB);
            s.RestoreMountsOnStartup = RestoreMounts;
            s.KeepMountsAfterExit = KeepMounts;
            s.EncryptRcloneConfig = EncryptConfig;
            s.RclonePathOverride = string.IsNullOrWhiteSpace(RclonePath) ? null : RclonePath.Trim();
            s.LocalGamesRoot = string.IsNullOrWhiteSpace(LocalGamesRoot) ? null : LocalGamesRoot.Trim();
            if (!ByteSize.TryParse(BufferSize, out var buf) || !ByteSize.TryParse(ReadAhead, out var ra) || !ByteSize.TryParse(ChunkSize, out var cs))
                throw new FormatException("Sizes must look like 0, 512Ki, 4Mi or 1Gi.");
            s.Cache.BufferSizeBytes = Math.Clamp(buf, 0, 256 * ByteSize.MiB);
            s.Cache.ReadAheadBytes = Math.Clamp(ra, 0, 4 * ByteSize.GiB);
            s.Cache.ReadChunkSizeBytes = Math.Clamp(cs, 256 * ByteSize.KiB, 2 * ByteSize.GiB);
            s.Cache.ReadChunkStreams = (int)Parse(ChunkStreams, 0, 64, "Chunk streams");
            s.Cache.WriteBack = TimeSpan.FromSeconds(Parse(WriteBackSeconds, 0, 3600, "Write-back delay"));
        }
        catch (FormatException e)
        {
            Say(e.Message, "error");
            return;
        }
        Host.Engine.Governor.MaxConcurrency = s.Network.MaxPrefetchConcurrency;
        Host.Engine.Governor.BackgroundShare = s.Network.BackgroundShare;
        Host.Engine.Governor.DeclaredBytesPerSec = Bandwidth.MbpsToBytesPerSecond(s.Network.EffectiveMbps);
        Host.SaveSettings();
        if (EncryptConfig && Host.Rclone != null && !Host.RcloneConfig.IsEncrypted && File.Exists(Host.RcloneConfig.ConfigPath))
            await Host.RcloneConfig.EnsureEncryptedAsync(Host.Rclone, Host.CpxPath);
        foreach (var s2 in Host.Mounts.Sessions) await Host.Mounts.SetBandwidthLimitAsync(s2.Id, s.Network.HardLimitMbps);
        OnShown();
        Say("Settings saved. VFS tuning and profiling changes apply the next time a drive is mounted; bandwidth limits apply now.", "ok");
    }

    private static double Parse(string text, double min, double max, string name)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < min || v > max)
            throw new FormatException($"{name} must be a number between {min} and {max}.");
        return v;
    }

    [RelayCommand]
    private void OpenDataFolder() => Process.Start(new ProcessStartInfo("explorer.exe", Host.Paths.DataRoot) { UseShellExecute = true });
}

public sealed record LogRow(string Time, string Level, string Tone, string Source, string Message);

/// <summary>Diagnostics: dependency status, warnings/errors, rclone logs and sanitized report export.</summary>
public sealed partial class DiagnosticsViewModel : PageViewModel
{
    public DiagnosticsViewModel(AppHost host) : base(host, "Diagnostics", "") { }

    public ObservableCollection<KeyValueRow> Dependencies { get; } = new();
    public ObservableCollection<LogRow> Logs { get; } = new();
    public ObservableCollection<string> RcloneLog { get; } = new();
    public ObservableCollection<KeyValueRow> Suggestions { get; } = new();

    [ObservableProperty] private bool _errorsOnly = true;
    [ObservableProperty] private string _summary = "";

    partial void OnErrorsOnlyChanged(bool value) => LoadLogs();

    public override void OnShown()
    {
        Host.RefreshDependencies();
        Dependencies.ReplaceWith(Host.Dependencies.Select(d => new KeyValueRow(d.Name, d.Detail + (d.Remedy != null ? "  →  " + d.Remedy : ""),
            d.State switch { DependencyState.Ok => "ok", DependencyState.Warning => "warn", _ => "error" })));
        LoadLogs();
        var lines = new List<string>();
        foreach (var s in Host.Mounts.Sessions)
            lines.AddRange(Mount.RcloneMountService.SafeTail(s.LogFile, 400).Split('\n')
                .Where(l => l.Contains("ERROR") || l.Contains("NOTICE") || l.Contains("INFO") || l.Contains("CRITICAL"))
                .Select(l => $"[{s.Definition.DisplayName}] {Redactor.Redact(l.TrimEnd())}").TakeLast(60));
        RcloneLog.ReplaceWith(lines.Count > 0 ? lines : ["No rclone mount is running."]);
        BuildSuggestions();
        Summary = $"{Host.LogSink.ErrorCount} error(s), {Host.LogSink.WarningCount} warning(s) since start. Logs: {Host.Paths.LogDirectory}";
    }

    private void LoadLogs()
    {
        var entries = Host.LogSink.Snapshot().Where(e => !ErrorsOnly || e.Level >= LogLevel.Warning).Reverse().Take(300);
        Logs.ReplaceWith(entries.Select(e => new LogRow(e.Timestamp.ToString("HH:mm:ss"), e.Level.ToString(),
            e.Level >= LogLevel.Error ? "error" : e.Level == LogLevel.Warning ? "warn" : "muted", e.Category, e.Message)));
    }

    private void BuildSuggestions()
    {
        var list = new List<KeyValueRow>();
        foreach (var s in Host.Mounts.Sessions)
        {
            if (s.AuthErrors > 0) list.Add(new("Cloud authentication required", $"{s.Definition.DisplayName}: the login expired or was revoked. Reconnect the account in Cloud Drives.", "error"));
            if (s.RateLimitEvents > 0) list.Add(new("Provider rate limiting", $"{s.Definition.DisplayName}: {s.RateLimitEvents} rate-limit responses. rclone retries automatically; lower 'API transactions/s' in Settings if this persists.", "warn"));
            if (s.NetworkErrors > 0) list.Add(new("Network errors", $"{s.Definition.DisplayName}: {s.NetworkErrors} network errors (last {s.LastNetworkError:T}). Uncached reads fail while offline; cached data stays readable.", "warn"));
            if (s.State is MountState.Failed or MountState.Degraded) list.Add(new("Drive problem", $"{s.Definition.DisplayName}: {s.Message}", "error"));
        }
        if (Host.SharedClientRemotes.Count > 0)
            list.Add(new("Google login is being retired", $"{string.Join(", ", Host.SharedClientRemotes)} still use rclone's shared Google login, which rclone retires during 2026. " +
                                                          "Cloud Drives → Switch to my own Google key.", "warn"));
        if (Host.DiskPressure) list.Add(new("Low disk space", "Prefetching is paused and rclone is evicting cached data. Free space or move the cache (Smart Cache).", "warn"));
        if (Host.Dependencies.Any(d => d.Name == "WinFsp" && d.State != DependencyState.Ok)) list.Add(new("Install WinFsp", "Without WinFsp no virtual drive can be created.", "error"));
        if (list.Count == 0) list.Add(new("No problems detected", "Everything Cloud Drive can check looks healthy.", "ok"));
        Suggestions.ReplaceWith(list);
    }

    [RelayCommand]
    private void Export()
    {
        var dlg = new SaveFileDialog { FileName = $"cloudssd-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.zip", Filter = "ZIP archive|*.zip" };
        if (dlg.ShowDialog() != true) return;
        var sections = new Dictionary<string, string>
        {
            ["system.txt"] = $"OS: {Environment.OSVersion}\n.NET: {Environment.Version}\nCPU cores: {Environment.ProcessorCount}\nCloud Drive: {typeof(App).Assembly.GetName().Version}\n" +
                             $"rclone: {Host.Rclone?.Version}\nWinFsp: {WinFspDetector.Detect().Version}\nData root: {Host.Paths.DataRoot}\n",
            ["settings.json"] = JsonSerializer.Serialize(Host.Settings, JsonSettingsStore.Options),
            ["dependencies.txt"] = string.Join("\n", Host.Dependencies.Select(d => $"[{d.State}] {d.Name}: {d.Detail} {d.Remedy}")),
            ["mounts.txt"] = string.Join("\n", Host.Mounts.Sessions.Select(s => $"{s.Id} {s.DriveLetter}: {s.State} {s.Message} errors={s.ProviderErrors} retries={s.Retries} auth={s.AuthErrors}")),
            ["metrics.json"] = JsonSerializer.Serialize(Host.Metrics.Latest, JsonSettingsStore.Options),
            ["games.json"] = JsonSerializer.Serialize(Host.Library.All(), JsonSettingsStore.Options),
        };
        var logs = Directory.EnumerateFiles(Host.Paths.LogDirectory, "*.log").Concat(Directory.Exists(Host.Paths.DiagnosticsDirectory)
            ? Directory.EnumerateFiles(Host.Paths.DiagnosticsDirectory, "*.json") : []);
        DiagnosticReport.Create(dlg.FileName, sections, logs);
        Say($"Sanitized report saved to {dlg.FileName}. Tokens, secrets and passwords are redacted; the rclone config is never included.", "ok");
    }

    [RelayCommand] private void OpenLogs() => Process.Start(new ProcessStartInfo("explorer.exe", Host.Paths.LogDirectory) { UseShellExecute = true });

    [RelayCommand] private void Refresh() => OnShown();
}
