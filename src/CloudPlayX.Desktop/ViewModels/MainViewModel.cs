using System.Collections.ObjectModel;
using CloudPlayX.App;
using CloudPlayX.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CloudPlayX.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(AppHost host)
    {
        Host = host;
        Pages = new ObservableCollection<PageViewModel>
        {
            new DashboardViewModel(host, Navigate),
            new CloudDrivesViewModel(host),
            new MobileHubViewModel(host),
            new GameLibraryViewModel(host),
            new MemoryViewModel(host),
            new SettingsViewModel(host),
        };
        _selected = Pages[0];
        host.StateChanged += () => Ui(() =>
        {
            Selected.OnStateChanged();
            UpdateStatus(host.Metrics.Latest);
        });
        host.Metrics.Updated += m => Ui(() =>
        {
            Selected.OnMetrics(m);
            UpdateStatus(m);
        });
    }

    public AppHost Host { get; }
    public ObservableCollection<PageViewModel> Pages { get; }

    [ObservableProperty] private PageViewModel _selected;
    [ObservableProperty] private string _storageSummary = "";
    [ObservableProperty] private string _drivesStatus = "No drives connected";
    [ObservableProperty] private string _rateStatus = "";
    [ObservableProperty] private string _engineStatus = "";
    [ObservableProperty] private string _gameStatus = "";
    [ObservableProperty] private string _version = "v" + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0");

    partial void OnSelectedChanged(PageViewModel value)
    {
        value?.OnShown();
        Ui(() => LocalizationManager.TranslateTree(System.Windows.Application.Current.MainWindow));
    }

    public void Navigate(string title)
    {
        if (title.Contains("Cache", StringComparison.OrdinalIgnoreCase) || title.Contains("Storage", StringComparison.OrdinalIgnoreCase))
        {
            var s = Pages.OfType<SettingsViewModel>().FirstOrDefault();
            if (s != null)
            {
                s.SwitchSubTab("Storage");
                Selected = s;
                return;
            }
        }
        if (title.Contains("Accelerator", StringComparison.OrdinalIgnoreCase) || title.Contains("Speed", StringComparison.OrdinalIgnoreCase))
        {
            var s = Pages.OfType<SettingsViewModel>().FirstOrDefault();
            if (s != null)
            {
                s.SwitchSubTab("Speed");
                Selected = s;
                return;
            }
        }
        if (title.Contains("Performance", StringComparison.OrdinalIgnoreCase))
        {
            var s = Pages.OfType<SettingsViewModel>().FirstOrDefault();
            if (s != null)
            {
                s.SwitchSubTab("Performance");
                Selected = s;
                return;
            }
        }
        if (title.Contains("Diagnostics", StringComparison.OrdinalIgnoreCase) || title.Contains("Log", StringComparison.OrdinalIgnoreCase))
        {
            var s = Pages.OfType<SettingsViewModel>().FirstOrDefault();
            if (s != null)
            {
                s.SwitchSubTab("Advanced");
                Selected = s;
                return;
            }
        }
        var p = Pages.FirstOrDefault(x => x.Title.Equals(title, StringComparison.OrdinalIgnoreCase) || x.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
        if (p != null) Selected = p;
    }

    private void UpdateStatus(EngineMetrics m)
    {
        // Real total over the connected accounts (asked from the provider), not a fixed marketing figure.
        StorageSummary = Host.Remotes.Count == 0 ? "No cloud account connected"
            : AccountText.Totals(Host) is { } t ? $"{ByteSize.Format(t.Total)} cloud · {t.Accounts} account(s)"
            : $"{Host.Remotes.Count(r => !AppHost.IsGeneratedType(r.Type))} account(s) connected";
        var sessions = Host.Mounts.Sessions;
        DrivesStatus = sessions.Count == 0 ? "No drives connected"
            : string.Join("  ", sessions.Select(s => $"{s.DriveLetter}: {s.State}"));
        RateStatus = m.DownloadBytesPerSec is { } r ? $"↓ {Bandwidth.BytesPerSecondToMbps(r):0.0} Mbps" : "";
        EngineStatus = Host.DiskPressure ? "Prefetch paused (disk space)" : Host.Engine.Scheduler.Active > 0 ? $"Prefetching ({Host.Engine.Queue.Count} queued)" : "";
        GameStatus = Host.CurrentLaunch is { State: Games.LaunchState.Running } l ? $"▶ {l.Game.Name}" : "";
    }

    private static void Ui(Action a)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d is null) return;
        if (d.CheckAccess()) a();
        else d.InvokeAsync(a);
    }
}
