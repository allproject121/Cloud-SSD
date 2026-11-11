using System.Collections.ObjectModel;
using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudSSD.Desktop.ViewModels;

public sealed record SetupStep(string Text, bool Done, string Hint);

public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly Action<string> _navigate;

    public DashboardViewModel(AppHost host, Action<string> navigate) : base(host, "Dashboard", "") 
    {
        _navigate = navigate;
        // The storage auto mode tells what it did in a note, so nobody has to open a page to see that it worked.
        // Not during a game: the note would sit on top of it.
        host.Offloader.OffloadCompleted += r =>
        {
            if (r.Automatic && r.FilesMovedCount > 0 && !GameActivity.IsPlaying(host))
                Notifier.Show(L("Storage auto mode"), L("Moved to the cloud: {0} ({1}).", SmartStorageOffloaderService.HistoryTitle(r.FilesMoved), MemoryHealth.Size(r.BytesFreed)));
        };
        host.Offloader.JunkCleaned += (files, bytes) =>
        {
            if (!GameActivity.IsPlaying(host))
                Notifier.Show(L("Storage auto mode"), L("Removed {0} old junk file(s): {1} freed.", files, MemoryHealth.Size(bytes)));
        };
    }

    /// <summary>Text made here changes with the numbers, so it is translated as it is built (the window's one-time pass cannot reach it).</summary>
    private static string L(string format, params object[] args) => string.Format(LocalizationManager.T(format), args);

    [ObservableProperty] private string _autoRam = "";
    [ObservableProperty] private string _autoStorage = "";
    [ObservableProperty] private string _autoGame = "";

    /// <summary>One line each for RAM auto mode, storage auto mode and game mode: on or off, and what it last did.</summary>
    private void ShowAutoModes()
    {
        try
        {
            var (total, available) = MemoryHealth.ReadRam();
            string free = total > 0 ? $"{(double)available / total:P0}" : Fmt.NA;
            AutoRam = !Host.Settings.Memory.AutoRelief ? L("Off.")
                : Host.MemoryRelief.Last is { } r ? L("On. Last run {0}: moved {1} idle app(s), free RAM {2} → {3}. Now {4} free.", r.At.ToString("HH:mm"), r.Apps, MemoryHealth.Size(r.AvailableBefore), MemoryHealth.Size(r.AvailableAfter), free)
                : L("On. Has not had to act yet: {0} of RAM is free (acts below {1}).", free, $"{MemoryRelief.FreeFraction:P0}");
        }
        catch (System.ComponentModel.Win32Exception) { AutoRam = Fmt.NA; }

        var off = Host.Settings.SmartOffload;
        double freeGb = Math.Round(DiskInfo.FreeSpace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).FreeForUser / (double)ByteSize.GiB, 1);
        var storage = !off.Enabled ? L("Off.")
            : freeGb >= off.MinFreeSpaceThresholdGB ? L("On. {0} GB free, limit {1} GB: nothing to do.", freeGb, off.MinFreeSpaceThresholdGB)
            : L("On. {0} GB free: {1} GB short of the {2} GB limit.", freeGb, Math.Round(off.MinFreeSpaceThresholdGB - freeGb, 1), off.MinFreeSpaceThresholdGB);
        if (off.Enabled && Host.Offloader.LastAction is { } a)
            storage += " " + (a.IsCleanup ? L("Last {0}: removed {1} of old junk.", a.At.ToString("HH:mm"), MemoryHealth.Size(a.Bytes))
                : L("Last {0}: moved {1} to the cloud ({2}).", a.At.ToString("HH:mm"), a.What, MemoryHealth.Size(a.Bytes)));
        AutoStorage = storage;

        var mem = Host.Settings.Memory;
        var game = !mem.GameMode ? L("Off.")
            : mem.CloseForGame.Count == 0 ? L("On. Frees RAM when a game starts. No app is set to close.")
            : L("On. When a game starts it frees RAM and closes: {0}.", string.Join(", ", mem.CloseForGame));
        if (mem.GameMode && Host.GameMode.Active) game += " " + L("A game is running now.");
        if (mem.GameMode && Host.GameMode.Last is { } g)
            game += " " + L("Last game {0}: closed {1} app(s), free RAM {2} → {3}.", g.At.ToString("HH:mm"), g.Closed.Count, MemoryHealth.Size(g.AvailableBefore), MemoryHealth.Size(g.AvailableAfter));
        AutoGame = game;
    }

    public ObservableCollection<AccountRow> Accounts { get; } = new();
    public ObservableCollection<KeyValueRow> Drives { get; } = new();
    public ObservableCollection<SetupStep> Setup { get; } = new();
    public ObservableCollection<KeyValueRow> Warnings { get; } = new();

    [ObservableProperty] private string _transferRate = Fmt.NA;
    [ObservableProperty] private string _cacheUsage = Fmt.NA;
    [ObservableProperty] private string _cacheDetail = "";
    [ObservableProperty] private double _cacheFraction;
    [ObservableProperty] private string _freeSpace = Fmt.NA;
    [ObservableProperty] private string _cloudStorage = "";
    [ObservableProperty] private string _engineState = "Idle";
    [ObservableProperty] private string _engineDetail = "";
    [ObservableProperty] private string _hitRate = Fmt.NA;
    [ObservableProperty] private string _gameState = "No game running";
    [ObservableProperty] private string _health = "No drives connected";
    [ObservableProperty] private string _healthTone = "muted";
    [ObservableProperty] private double?[] _rateHistory = [];
    [ObservableProperty] private bool _setupComplete;

    public override void OnShown()
    {
        OnStateChanged();
        // Storage figures come live from the provider; refresh them when the page opens (at most once a minute).
        if (Host.Remotes.Count > 0 && DateTime.UtcNow - Host.AccountsRefreshedAt > TimeSpan.FromMinutes(1)) _ = Host.RefreshAccountsAsync();
    }

    public override void OnStateChanged()
    {
        // Real identity and storage of each account, asked from the provider — not the internal remote names.
        Accounts.ReplaceWith(AccountText.Rows(Host));
        CloudStorage = Host.Remotes.Count == 0 ? "No cloud account connected"
            : AccountText.Totals(Host) is { } t ? $"Cloud: {ByteSize.Format(t.Used)} used of {ByteSize.Format(t.Total)} · {t.Accounts} account(s)"
            : Host.AccountsRefreshedAt == default ? "Cloud: checking accounts…" : "Cloud: " + Fmt.NA;
        var sessions = Host.Mounts.Sessions;
        Drives.ReplaceWith(Host.Settings.Mounts.Select(d =>
        {
            var s = sessions.FirstOrDefault(x => x.Id == d.Id);
            var state = s?.State ?? MountState.Stopped;
            return new KeyValueRow($"{(s != null ? s.DriveLetter + ":" : (d.DriveLetter ?? "?") + ":")}  {d.DisplayName}",
                $"{state} · {Host.AccountLabel(d.RemoteName)}{(AccountText.Storage(Host.Accounts.GetValueOrDefault(d.RemoteName)) is { } st ? " · " + st : "")}" +
                $"{(s?.Message is { } m && state != MountState.Mounted ? " · " + m : "")}", Fmt.Tone(state));
        }));
        var mounted = sessions.Count(s => s.State == MountState.Mounted);
        var degraded = sessions.Count(s => s.State is MountState.Degraded or MountState.Failed);
        (Health, HealthTone) = degraded > 0 ? ($"{degraded} drive(s) need attention", "error")
            : mounted > 0 ? ($"{mounted} drive(s) connected and active", "ok")
            : ("No drives connected", "muted");

        var deps = Host.Dependencies;
        Warnings.ReplaceWith(deps.Where(d => d.State != DependencyState.Ok).Select(d => new KeyValueRow(d.Name, d.Detail + (d.Remedy != null ? " " + d.Remedy : ""),
            d.State == DependencyState.Warning ? "warn" : "error")));
        var games = Host.Library.All();
        Setup.ReplaceWith([
            new("WinFsp installed", deps.Any(d => d.Name == "WinFsp" && d.State == DependencyState.Ok), "Required for the virtual drive"),
            new("rclone available", Host.Rclone != null, "Bundled with Cloud Drive"),
            new("Cloud account connected", Host.Remotes.Count > 0, "Cloud Drives → Connect Google Drive"),
            new("Drive connected", mounted > 0, "Cloud Drives → Add drive"),
            new("Game added", games.Count > 0, "Game Library → Add game"),
        ]);
        SetupComplete = Setup.All(s => s.Done);
        OnMetrics(Host.Metrics.Latest);
    }

    public override void OnMetrics(EngineMetrics m)
    {
        TransferRate = Fmt.Rate(m.DownloadBytesPerSec);
        RateHistory = Host.Metrics.TransferRate.Snapshot();
        var budget = Host.Settings.Cache.BudgetBytes;
        CacheUsage = m.CacheBytesOnDisk is { } used ? $"{ByteSize.Format(used)} of {ByteSize.Format(budget)}" : Fmt.NA;
        CacheFraction = m.CacheBytesOnDisk is { } u && budget > 0 ? Math.Min(1, u / (double)budget) : 0;
        CacheDetail = m.CacheLogicalBytes is { } l ? $"Files touched: {ByteSize.Format(l)} logical size (sparse — only downloaded ranges use disk)" : "";
        FreeSpace = m.CacheVolumeFreeBytes is { } f ? $"{ByteSize.Format(f)} free on cache drive (reserve {ByteSize.Format(Host.Settings.Cache.MinFreeSpaceBytes)})" : Fmt.NA;
        HitRate = Fmt.Pct(m.HitRate);
        GameState = m.GameProcessState ?? "No game running";
        var e = Host.Engine;
        var sessions = e.Sessions.Count;
        EngineState = !Host.Settings.Streaming.PredictionEnabled ? "Prediction off"
            : Host.DiskPressure ? "Paused (low disk space)"
            : e.Scheduler.Active > 0 ? "Prefetching"
            : sessions > 0 ? "Profiling game"
            : "Idle";
        EngineDetail = $"Queue {e.Queue.Count} · active {e.Scheduler.Active} · completed {e.Scheduler.CompletedCount:N0} · fetched {ByteSize.Format(e.Scheduler.BytesFetched)}";
        ShowAutoModes();
    }

    [RelayCommand] private void Go(string page) => _navigate(page);
}
