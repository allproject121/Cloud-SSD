using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CloudPlayX.App;
using CloudPlayX.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudPlayX.Desktop.ViewModels;

public sealed record MemoryAppRow(string Name, string Title, string Size, bool CanClose);

/// <summary>
/// PC Memory: what Windows reports about RAM, commit and the page file, and the apps holding the most RAM.
/// It changes no Windows setting; the only action is asking an app to close when the user says so.
/// </summary>
public sealed partial class MemoryViewModel : PageViewModel
{
    private readonly MetricHistory _ramHistory = new(300);
    private DateTime _readAt;

    public MemoryViewModel(AppHost host) : base(host, "PC Memory (RAM)", "")
    {
        // Sampled from app start so the chart already has history when the page is first opened.
        host.Metrics.Updated += _ =>
        {
            try
            {
                var (total, available) = MemoryHealth.ReadRam();
                _ramHistory.Add(total > 0 ? 1 - (double)available / total : null);
            }
            catch (Win32Exception) { _ramHistory.Add(null); }
        };
    }

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

    public Func<double, string> PercentFormat { get; } = v => $"{v:P0}";

    /// <summary>Row buttons are rebuilt on every refresh, so their caption is bound instead of translated in place.</summary>
    public string CloseLabel => LocalizationManager.T("Close");

    public override void OnShown()
    {
        _readAt = default;
        OnPropertyChanged(nameof(CloseLabel));
        OnMetrics(Host.Metrics.Latest);
    }

    public override void OnMetrics(EngineMetrics m)
    {
        RamHistoryValues = _ramHistory.Snapshot();
        RamNow = _ramHistory.Latest is { } f ? $"{f:P0} in use" : Fmt.NA;
        _ = RefreshAsync();
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
            RamInUse = ByteSize.Format(s.PhysicalTotalBytes - s.PhysicalAvailableBytes);
            RamDetail = $"of {ByteSize.Format(s.PhysicalTotalBytes)} · {s.RamUsedFraction:P0}";
            RamFraction = s.RamUsedFraction;
            RamTone = s.RamUsedFraction >= MemoryHealth.RamAlmostFullFraction ? "warn" : "";
            RamFree = ByteSize.Format(s.PhysicalAvailableBytes);
            Commit = ByteSize.Format(s.CommitUsedBytes);
            CommitDetail = $"of {ByteSize.Format(s.CommitLimitBytes)} limit (RAM + page file)";
            CommitFraction = Math.Min(1, s.CommitUsedFraction);
            CommitTone = s.CommitUsedFraction >= MemoryHealth.CommitNearLimitFraction ? "error" : "";
            PageFileSize = s.PageFiles.Count == 0 ? "None" : ByteSize.Format(s.PageFiles.Sum(f => f.AllocatedBytes));
            PageFileUse = s.PageFiles.Count == 0 ? "" : $"{ByteSize.Format(s.PageFiles.Sum(f => f.UsedBytes))} in use";

            var rows = new List<KeyValueRow>();
            if (s.PageFiles.Count == 0) rows.Add(new("File", "None", "error"));
            foreach (var f in s.PageFiles)
            {
                rows.Add(new("File", f.Path, ""));
                rows.Add(new("Size", ByteSize.Format(f.AllocatedBytes) + s.PageFileSystemManaged switch { true => " · sized by Windows", false => " · fixed size", _ => "" },
                    s.PageFileSystemManaged == false ? "warn" : ""));
                rows.Add(new("In use", $"{ByteSize.Format(f.UsedBytes)} · peak {ByteSize.Format(f.PeakBytes)}", ""));
            }
            if (s.PageFileVolumeFreeBytes is { } free)
                rows.Add(new($"Free on {s.PageFileVolume}", ByteSize.Format(free) + (s.CacheSharesPageFileVolume ? " · shared with the Cloud-SSD cache" : ""),
                    free < MemoryHealth.PageFileRoomBytes ? "warn" : ""));
            PageFile.ReplaceWith(rows);

            // A running game is never offered for closing from here.
            var game = Host.CurrentLaunch is { State: Games.LaunchState.Running } l ? Path.GetFileNameWithoutExtension(l.Executable) : null;
            Apps.ReplaceWith(s.TopApps.Select(a => new MemoryAppRow(a.Name, a.Processes > 1 ? $"{a.Name} ({a.Processes} processes)" : a.Name,
                ByteSize.Format(a.WorkingSetBytes), a.CanClose && !string.Equals(a.Name, game, StringComparison.OrdinalIgnoreCase))));
            Advice.ReplaceWith(MemoryHealth.Advise(s).Select(a => new KeyValueRow("", a.Text, a.Tone)));
        }
        catch (Win32Exception)
        {
            Advice.ReplaceWith([new("", "Windows did not report memory figures.", "muted")]);
        }
    }

    [RelayCommand]
    private async Task CloseApp(MemoryAppRow app)
    {
        if (!Confirm($"Close {app.Name}?\n\nCloud Drive asks it to close the normal way, like pressing X. If it asks you to save your work, answer it — unsaved work can be lost."))
            return;
        await RunAsync(async () =>
        {
            var before = MemoryHealth.ReadRam().AvailableBytes;
            var asked = await Task.Run(() => MemoryHealth.RequestClose(app.Name));
            if (asked == 0)
            {
                Say($"{app.Name} has no window Cloud Drive can close. Close it yourself or use Task Manager.", "warn");
                return;
            }
            await Task.Delay(3000);
            _readAt = default;
            await RefreshAsync();
            if (MemoryHealth.IsRunning(app.Name))
                Say($"{app.Name} was asked to close but is still running. It may be waiting for you to save.", "warn");
            else
                Say($"{app.Name} closed. Free RAM went from {ByteSize.Format(before)} to {ByteSize.Format(MemoryHealth.ReadRam().AvailableBytes)}.", "ok");
        });
    }

    [RelayCommand]
    private void OpenPageFileSettings()
    {
        try { Process.Start(new ProcessStartInfo("SystemPropertiesPerformance.exe") { UseShellExecute = true }); }
        catch (Win32Exception e) { Say("Could not open the Windows settings: " + e.Message, "error"); }
    }
}
