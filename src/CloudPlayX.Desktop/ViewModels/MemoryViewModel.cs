using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CloudPlayX.App;
using CloudPlayX.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudPlayX.Desktop.ViewModels;

/// <summary>One row of "Apps using the most RAM". Name is the process name; Display is what the user reads.</summary>
public sealed record MemoryAppRow(string Name, string Display, string Title, string Size, bool CanClose);

/// <summary>
/// PC Memory: what Windows reports about RAM, commit and the page file, and the apps holding the most RAM.
/// It changes no Windows setting. Its actions are asking an app to close when the user says so, and auto mode,
/// which moves idle background apps out of RAM when RAM runs low.
/// </summary>
public sealed partial class MemoryViewModel : PageViewModel
{
    private readonly MetricHistory _ramHistory = new(300);
    private readonly MemoryRelief _relief = new();
    private DateTime _readAt;
    private bool _gameWasRunning;

    public MemoryViewModel(AppHost host) : base(host, "PC Memory (RAM)", "")
    {
        // Sampled from app start so the chart already has history when the page is first opened.
        host.Metrics.Updated += _ =>
        {
            try
            {
                var (total, available) = MemoryHealth.ReadRam();
                _ramHistory.Add(total > 0 ? 1 - (double)available / total : null);
                // Auto mode works whether or not this page is open. A game that has just started gets room at once.
                var gameRunning = host.CurrentLaunch is { State: Games.LaunchState.Running };
                var gameJustStarted = gameRunning && !_gameWasRunning;
                _gameWasRunning = gameRunning;
                if (_relief.Tick(total, available, gameJustStarted) && host.Settings.Memory.AutoRelief) Task.Run(() => _relief.Run(GameToKeep()));
            }
            catch (Win32Exception) { _ramHistory.Add(null); }
        };
    }

    /// <summary>A running game is never closed or moved out of RAM from here.</summary>
    private string[] GameToKeep() =>
        Host.CurrentLaunch is { State: Games.LaunchState.Running } l ? [Path.GetFileNameWithoutExtension(l.Executable)] : [];

    /// <summary>
    /// Text made here changes every few seconds, so the window's one-time translation pass cannot reach it;
    /// it is translated as it is built, with the numbers filled in afterwards.
    /// </summary>
    private static string L(string format, params object[] args) => string.Format(LocalizationManager.T(format), args);

    public ObservableCollection<KeyValueRow> Advice { get; } = new();
    public ObservableCollection<KeyValueRow> PageFile { get; } = new();
    public ObservableCollection<MemoryAppRow> Apps { get; } = new();

    [ObservableProperty] private string _ramInUse = Fmt.NA;
    [ObservableProperty] private string _ramDetail = "";
    [ObservableProperty] private string _ramTone = "";
    [ObservableProperty] private double _ramFraction;
    [ObservableProperty] private string _ramFree = Fmt.NA;
    [ObservableProperty] private string _commit = Fmt.NA;
    [ObservableProperty] private string _commitDetail = "";
    [ObservableProperty] private string _commitTone = "";
    [ObservableProperty] private double _commitFraction;
    [ObservableProperty] private string _pageFileSize = Fmt.NA;
    [ObservableProperty] private string _pageFileUse = "";
    [ObservableProperty] private string _ramNow = Fmt.NA;
    [ObservableProperty] private double?[] _ramHistoryValues = [];
    [ObservableProperty] private bool _autoRelief;
    [ObservableProperty] private string _reliefStatus = "";
    [ObservableProperty] private string _reliefTone = "muted";

    partial void OnAutoReliefChanged(bool value)
    {
        if (Host.Settings.Memory.AutoRelief == value) return;
        Host.Settings.Memory.AutoRelief = value;
        Host.SaveSettings();
        ShowReliefStatus();
    }

    public Func<double, string> PercentFormat { get; } = v => $"{v:P0}";

    /// <summary>Row buttons are rebuilt on every refresh, so their caption is bound instead of translated in place.</summary>
    public string CloseLabel => LocalizationManager.T("Close");

    public override void OnShown()
    {
        _readAt = default;
        AutoRelief = Host.Settings.Memory.AutoRelief;
        OnPropertyChanged(nameof(CloseLabel));
        OnMetrics(Host.Metrics.Latest);
    }

    public override void OnMetrics(EngineMetrics m)
    {
        RamHistoryValues = _ramHistory.Snapshot();
        RamNow = _ramHistory.Latest is { } f ? L("{0} in use", $"{f:P0}") : Fmt.NA;
        ShowReliefStatus();
        _ = RefreshAsync();
    }

    private void ShowReliefStatus()
    {
        var parts = new List<string>();
        var free = 1 - (_ramHistory.Latest ?? 0);
        if (_relief.Last is { } r)
        {
            parts.Add(L("Last run {0}: moved {1} idle app(s) out of RAM, free RAM {2} → {3}.", r.At.ToString("HH:mm"), r.Apps, MemoryHealth.Size(r.AvailableBefore), MemoryHealth.Size(r.AvailableAfter)));
            if (!r.ReachedTarget && free < MemoryRelief.FreeFraction)
                parts.Add(L("Still below {0} free: the apps you are using need the RAM, so auto mode waits longer before it tries again. Close an app from the list to free more.", $"{MemoryRelief.FreeFraction:P0}"));
        }
        parts.Add(!AutoRelief ? L("Auto mode is off.")
            : free >= MemoryRelief.FreeFraction ? L("Watching: {0} of RAM is free, auto mode acts below {1}.", $"{free:P0}", $"{MemoryRelief.FreeFraction:P0}")
            : _relief.Wait > TimeSpan.Zero ? L("RAM is low ({0} free); auto mode can act in {1} min.", $"{free:P0}", Math.Ceiling(_relief.Wait.TotalMinutes).ToString("0"))
            : L("RAM is low ({0} free); auto mode is acting now.", $"{free:P0}"));
        ReliefStatus = string.Join(" ", parts);
        ReliefTone = _relief.Last is { ReachedTarget: true } ? "ok" : "muted";
    }

    /// <summary>Listing every process takes a moment, so it runs off the UI thread and at most every 5 s.</summary>
    private async Task RefreshAsync()
    {
        if (DateTime.UtcNow - _readAt < TimeSpan.FromSeconds(5)) return;
        _readAt = DateTime.UtcNow;
        try
        {
            var cacheRoot = Host.Paths.CacheRootFor(Host.Settings);
            var s = await Task.Run(() => MemoryHealth.Read(cacheRoot));
            RamInUse = MemoryHealth.Size(s.PhysicalTotalBytes - s.PhysicalAvailableBytes);
            RamDetail = L("of {0} · {1}", MemoryHealth.Size(s.PhysicalTotalBytes), $"{s.RamUsedFraction:P0}");
            RamFraction = s.RamUsedFraction;
            RamTone = s.RamUsedFraction >= MemoryHealth.RamAlmostFullFraction ? "warn" : "";
            RamFree = MemoryHealth.Size(s.PhysicalAvailableBytes);
            Commit = MemoryHealth.Size(s.CommitUsedBytes);
            CommitDetail = L("of {0} limit (RAM + page file)", MemoryHealth.Size(s.CommitLimitBytes));
            CommitFraction = Math.Min(1, s.CommitUsedFraction);
            CommitTone = s.CommitUsedFraction >= MemoryHealth.CommitNearLimitFraction ? "error" : "";
            PageFileSize = s.PageFiles.Count == 0 ? L("None") : MemoryHealth.Size(s.PageFiles.Sum(f => f.AllocatedBytes));
            PageFileUse = s.PageFiles.Count == 0 ? "" : L("{0} in use", MemoryHealth.Size(s.PageFiles.Sum(f => f.UsedBytes)));

            var rows = new List<KeyValueRow>();
            if (s.PageFiles.Count == 0) rows.Add(new(L("File"), L("None"), "error"));
            foreach (var f in s.PageFiles)
            {
                rows.Add(new(L("File"), f.Path, ""));
                rows.Add(new(L("Size"), s.PageFileSystemManaged switch
                {
                    true => L("{0} · sized by Windows", MemoryHealth.Size(f.AllocatedBytes)),
                    false => L("{0} · fixed size", MemoryHealth.Size(f.AllocatedBytes)),
                    _ => MemoryHealth.Size(f.AllocatedBytes)
                }, s.PageFileSystemManaged == false ? "warn" : ""));
                rows.Add(new(L("In use"), L("{0} · peak {1}", MemoryHealth.Size(f.UsedBytes), MemoryHealth.Size(f.PeakBytes)), ""));
            }
            if (s.PageFileVolumeFreeBytes is { } free)
                rows.Add(new(L("Free on {0}", s.PageFileVolume ?? ""),
                    s.CacheSharesPageFileVolume ? L("{0} · shared with the Cloud-SSD cache", MemoryHealth.Size(free)) : MemoryHealth.Size(free),
                    free < MemoryHealth.PageFileRoomBytes ? "warn" : ""));
            PageFile.ReplaceWith(rows);

            var game = GameToKeep().FirstOrDefault();
            var apps = s.TopApps.Select(a =>
            {
                var display = a.Title ?? a.Name;
                return new MemoryAppRow(a.Name, display, a.Processes > 1 ? L("{0} ({1} processes)", display, a.Processes) : display,
                    MemoryHealth.Size(a.WorkingSetBytes), a.CanClose && !string.Equals(a.Name, game, StringComparison.OrdinalIgnoreCase));
            }).ToList();
            // Windows and Cloud-SSD itself: shown as one line so the total adds up, but there is nothing to do about it.
            if (s.SystemBytes > 0)
                apps.Add(new MemoryAppRow("", "", L("Windows and Cloud-SSD ({0} processes) — cannot be closed", s.SystemProcesses), MemoryHealth.Size(s.SystemBytes), false));
            Apps.ReplaceWith(apps);
            Advice.ReplaceWith(MemoryHealth.Advise(s).Select(a => new KeyValueRow("", L(a.Format, a.Args), a.Tone)));
        }
        catch (Win32Exception)
        {
            Advice.ReplaceWith([new("", L("Windows did not report memory figures."), "muted")]);
        }
    }

    /// <summary>A result message that goes away by itself, so an old result is not mistaken for the current state.</summary>
    private void SayBriefly(string text, string tone)
    {
        Say(text, tone);
        _ = ClearLaterAsync(text);
    }

    private async Task ClearLaterAsync(string text)
    {
        await Task.Delay(TimeSpan.FromSeconds(20));
        if (Message == text) Message = null;
    }

    [RelayCommand]
    private async Task CloseApp(MemoryAppRow app)
    {
        if (!Confirm(L("Close {0}?\n\nCloud Drive asks it to close the normal way, like pressing X. If it asks you to save your work, answer it — unsaved work can be lost.", app.Display)))
            return;
        await RunAsync(async () =>
        {
            var before = MemoryHealth.ReadRam().AvailableBytes;
            var asked = await Task.Run(() => MemoryHealth.RequestClose(app.Name));
            if (asked == 0)
            {
                SayBriefly(L("{0} has no window Cloud Drive can close. Close it yourself or use Task Manager.", app.Display), "warn");
                return;
            }
            await Task.Delay(3000);
            _readAt = default;
            await RefreshAsync();
            if (MemoryHealth.IsRunning(app.Name))
                SayBriefly(L("{0} was asked to close but is still running. It may be waiting for you to save.", app.Display), "warn");
            else
                SayBriefly(L("{0} closed. Free RAM went from {1} to {2}.", app.Display, MemoryHealth.Size(before), MemoryHealth.Size(MemoryHealth.ReadRam().AvailableBytes)), "ok");
        });
    }

    /// <summary>The same step auto mode takes, on request.</summary>
    [RelayCommand]
    private async Task RunRelief()
    {
        await RunAsync(async () =>
        {
            var r = await Task.Run(() => _relief.Run(GameToKeep()));
            _readAt = default;
            await RefreshAsync();
            ShowReliefStatus();
            if (r is null) SayBriefly(L("Auto mode is already running."), "muted");
            else if (r.Apps == 0) SayBriefly(L("No idle background app to move: everything open was used in the last few minutes or belongs to Windows."), "muted");
            else SayBriefly(L("Moved {0} idle app(s) out of RAM. Free RAM went from {1} to {2}.", r.Apps, MemoryHealth.Size(r.AvailableBefore), MemoryHealth.Size(r.AvailableAfter)), r.FreedBytes > 0 ? "ok" : "muted");
        });
    }

    [RelayCommand]
    private void OpenPageFileSettings()
    {
        try { Process.Start(new ProcessStartInfo("SystemPropertiesPerformance.exe") { UseShellExecute = true }); }
        catch (Win32Exception e) { Say(L("Could not open the Windows settings: {0}", e.Message), "error"); }
    }
}
