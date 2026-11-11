using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using CloudSSD.App;
using CloudSSD.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using K = CloudSSD.App.SpaceActionKinds;

namespace CloudSSD.Desktop.ViewModels;

/// <summary>One entry of a ⋮ menu.</summary>
public sealed record MenuEntry(string Header, ICommand Command);

/// <summary>A device at the top of the page: this PC or one of the phones.</summary>
public sealed record SpaceDeviceChip(string Id, string Name, bool IsPc);

/// <summary>A bar row: a slice of a disk or of RAM, a folder, a file, an app.</summary>
public sealed class SpaceLine
{
    public string Name { get; init; } = "";
    public string Sub { get; init; } = "";
    public string Size { get; init; } = "";
    public double Fraction { get; init; }
    public bool IsFolder { get; init; }
    public string? Path { get; init; }
    public IReadOnlyList<MenuEntry> Menu { get; init; } = [];
    public bool HasMenu => Menu.Count > 0;
    public ICommand? Open { get; init; }
}

/// <summary>
/// Space &amp; RAM: where the disk space and the RAM of this PC — and of the paired phones — go, live, with a ⋮
/// menu on every line that says what an action frees before it is done. A phone's data comes from its own app;
/// when the phone is away or stopped sharing, its last report is shown with its time and without the menus.
/// </summary>
public sealed partial class SpaceViewModel : PageViewModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateTime _lastOverview;
    private volatile bool _indexChanged;
    private bool _busy;

    public SpaceViewModel(AppHost host) : base(host, "Space & RAM", "\uE9F9")
    {
        host.Space.Index.Changed += () => _indexChanged = true;
        host.Space.Phones.Changed += _ => Ui(() => { if (!SelectedDevice?.IsPc ?? false) Refresh(); });
        Devices.Add(new SpaceDeviceChip("pc", Environment.MachineName, true));
        SelectedDevice = Devices[0];
    }

    private static string L(string format, params object[] args) => string.Format(LocalizationManager.T(format), args);
    private static string Sz(long bytes) => bytes <= 0 ? "0 MB" : MemoryHealth.Size(bytes);

    public ObservableCollection<SpaceDeviceChip> Devices { get; } = new();
    public ObservableCollection<SpaceLine> Volumes { get; } = new();
    public ObservableCollection<SpaceLine> Groups { get; } = new();
    public ObservableCollection<SpaceLine> RamGroups { get; } = new();
    public ObservableCollection<SpaceLine> RamApps { get; } = new();
    public ObservableCollection<SpaceLine> FolderItems { get; } = new();
    public ObservableCollection<SpaceLine> Crumbs { get; } = new();
    public ObservableCollection<SpaceLine> Apps { get; } = new();
    public ObservableCollection<SpaceLine> Cleanups { get; } = new();
    public ObservableCollection<SpaceLine> LargeFiles { get; } = new();
    public ObservableCollection<SpaceLine> Duplicates { get; } = new();
    public ObservableCollection<SpaceLine> Startup { get; } = new();

    [ObservableProperty] private SpaceDeviceChip? _selectedDevice;
    [ObservableProperty] private string _deviceStatus = "";
    [ObservableProperty] private string _deviceTone = "muted";
    [ObservableProperty] private string _usedText = Fmt.NA;
    [ObservableProperty] private string _freeText = Fmt.NA;
    [ObservableProperty] private string _ramText = Fmt.NA;
    [ObservableProperty] private string _folderPath = "";
    [ObservableProperty] private string _folderNote = "";
    [ObservableProperty] private string _duplicatesText = "";
    [ObservableProperty] private bool _canAct = true;
    [ObservableProperty] private bool _isPc = true;

    public string MoreLabel => "⋮";

    public override void OnShown()
    {
        base.OnShown();
        foreach (var r in Host.Space.Phones.All())
            if (!Devices.Any(d => d.Id == r.DeviceId)) Devices.Add(new SpaceDeviceChip(r.DeviceId, r.Name, false));
        Refresh(full: true);
    }

    public override void OnMetrics(EngineMetrics m)
    {
        base.OnMetrics(m);
        if (DateTime.UtcNow - _lastOverview < TimeSpan.FromSeconds(3) || _busy) return;
        Refresh(full: _indexChanged);
    }

    partial void OnSelectedDeviceChanged(SpaceDeviceChip? value)
    {
        IsPc = value?.IsPc ?? true;
        FolderPath = "";
        Refresh(full: true);
    }

    [RelayCommand]
    private void SelectDevice(SpaceDeviceChip chip) => SelectedDevice = chip;

    // ---- reading ---------------------------------------------------------------------------------------------------------

    private void Refresh(bool full = false)
    {
        _lastOverview = DateTime.UtcNow;
        if (SelectedDevice is not { } d) return;
        if (d.IsPc) _ = RefreshPcAsync(full);
        else RefreshPhone(d.Id);
    }

    private async Task RefreshPcAsync(bool full)
    {
        _busy = true;
        try
        {
            var space = Host.Space;
            var summary = await Task.Run(space.Summary);
            ShowSummary(summary, live: true);
            CanAct = true;
            DeviceTone = summary.Ready ? "ok" : "accent";
            DeviceStatus = summary.Ready
                ? L("Live. Disks read {0}; changes since then are followed as they happen.", Ago(summary.ScannedAt))
                : summary.ScannedAt != null ? L("Reading the disks ({0:0}%). Numbers below are from {1}.", summary.Progress * 100, Ago(summary.ScannedAt))
                : L("Reading the disks for the first time ({0:0}%)…", summary.Progress * 100);
            var ramApps = await Task.Run(space.RamApps);
            Fill(RamApps, ramApps.Select(a => RamAppRow(a, summary.Ram.Total, pc: true)));
            if (!full && FolderItems.Count > 0) return;
            _indexChanged = false;
            if (string.IsNullOrEmpty(FolderPath)) FolderPath = summary.Volumes.FirstOrDefault()?.Root ?? "C:\\";
            await LoadPcFolderAsync(FolderPath);
            var apps = await Task.Run(space.Apps);
            long biggestApp = apps.Count > 0 ? Math.Max(1, apps.Max(a => a.Bytes)) : 1;
            Fill(Apps, apps.Select(a => AppRow(a, biggestApp, pc: true)));
            var cleanups = await Task.Run(() => space.Cleanups());
            Fill(Cleanups, cleanups.Select(c => CleanRow(c, pc: true)));
            Fill(LargeFiles, (await Task.Run(() => space.LargeFiles())).Select(f => ItemRow(f, Math.Max(1, f.Bytes), pc: true)));
            Fill(Startup, (await Task.Run(space.Startup)).Select(StartupRow));
            ShowDuplicates(space.Duplicates());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { Say(e.Message, "warn"); }
        finally { _busy = false; }
    }

    private void ShowSummary(SpaceSummary s, bool live)
    {
        long total = s.Volumes.Sum(v => v.Total), free = s.Volumes.Sum(v => v.Free);
        UsedText = total > 0 ? L("{0} used of {1}", Sz(total - free), Sz(total)) : Fmt.NA;
        FreeText = total > 0 ? L("{0} free", Sz(free)) : Fmt.NA;
        RamText = s.Ram.Total > 0 ? L("{0} of {1} RAM in use", Sz(s.Ram.Total - s.Ram.Available), Sz(s.Ram.Total)) : Fmt.NA;
        Fill(Volumes, s.Volumes.Select(v => new SpaceLine
        {
            Name = v.Label, Sub = L("{0} free of {1}", Sz(v.Free), Sz(v.Total)), Size = Sz(v.Total - v.Free),
            Fraction = v.Total > 0 ? (double)(v.Total - v.Free) / v.Total : 0, IsFolder = true, Path = v.Root,
            Open = new RelayCommand(() => OpenFolder(v.Root))
        }));
        long disk = Math.Max(1, s.Volumes.FirstOrDefault()?.Total ?? 1);
        Fill(Groups, s.Groups.Where(g => g.Bytes > 0).OrderByDescending(g => g.Bytes).Select(g => new SpaceLine
        {
            Name = LocalizationManager.T(g.Label), Sub = LocalizationManager.T(g.Note), Size = Sz(g.Bytes), Fraction = (double)g.Bytes / disk,
            IsFolder = g.Path != null, Path = g.Path,
            Menu = live ? GroupMenu(g) : [],
            Open = g.Path != null ? new RelayCommand(() => OpenFolder(g.Path!)) : null
        }));
        Fill(RamGroups, s.Ram.Groups.Select(g => new SpaceLine
        {
            Name = LocalizationManager.T(g.Label), Sub = LocalizationManager.T(g.Note), Size = Sz(g.Bytes),
            Fraction = s.Ram.Total > 0 ? (double)g.Bytes / s.Ram.Total : 0
        }));
    }

    private IReadOnlyList<MenuEntry> GroupMenu(SpaceGroup g)
    {
        var list = new List<MenuEntry>();
        foreach (var a in g.Actions ?? [])
        {
            switch (a)
            {
                case K.Open when g.Path != null: list.Add(new MenuEntry(L("Show what is inside"), new RelayCommand(() => OpenFolder(g.Path!)))); break;
                case K.EmptyBin: list.Add(Act(L("Empty the Recycle Bin (frees {0})", Sz(g.Bytes)), K.EmptyBin, "bin", g.Label, confirm: true)); break;
                case K.Clean: list.Add(Act(L("Clean old temporary files"), K.Clean, g.Key, g.Label, confirm: true)); break;
            }
        }
        return list;
    }

    private async Task LoadPcFolderAsync(string path)
    {
        var folder = await Task.Run(() => Host.Space.Folder(path));
        if (folder == null) { Say(L("That folder is not there any more."), "warn"); return; }
        ShowFolder(folder, pc: true);
    }

    private void ShowFolder(SpaceFolder folder, bool pc)
    {
        FolderPath = folder.Path;
        FolderNote = L("{0} in {1} items", Sz(folder.Bytes), folder.Items.Count) + (folder.Note.Length > 0 ? " · " + LocalizationManager.T(folder.Note) : "");
        long biggest = Math.Max(1, folder.Items.Count > 0 ? folder.Items.Max(i => i.Bytes) : 1);
        Fill(FolderItems, folder.Items.Select(i => ItemRow(i, biggest, pc)));
        // Crumbs: C:\ › Users › name
        var crumbs = new List<SpaceLine>();
        char sep = folder.Path.Contains('\\') ? '\\' : '/';
        var parts = folder.Path.TrimEnd(sep).Split(sep, StringSplitOptions.RemoveEmptyEntries);
        string acc = folder.Path.StartsWith(sep) ? sep.ToString() : "";
        foreach (var part in parts)
        {
            acc = acc.Length == 0 ? part + (sep == '\\' ? "\\" : "") : acc.TrimEnd(sep) + sep + part;
            var target = acc;
            crumbs.Add(new SpaceLine { Name = part, Path = target, Open = new RelayCommand(() => OpenFolder(target)) });
        }
        Fill(Crumbs, crumbs);
    }

    private void OpenFolder(string path)
    {
        if (SelectedDevice is { IsPc: true }) _ = LoadPcFolderAsync(path);
        else if (SelectedDevice is { } d) OpenPhoneFolder(d.Id, path);
    }

    [RelayCommand]
    private void FolderUp()
    {
        if (string.IsNullOrEmpty(FolderPath)) return;
        char sep = FolderPath.Contains('\\') ? '\\' : '/';
        var t = FolderPath.TrimEnd(sep);
        int cut = t.LastIndexOf(sep);
        if (cut <= 0) return;
        var parent = t[..cut];
        if (sep == '\\' && parent.Length == 2) parent += "\\";
        OpenFolder(parent);
    }

    // ---- rows and menus ------------------------------------------------------------------------------------------------

    private SpaceLine ItemRow(SpaceItem i, long biggest, bool pc)
    {
        var menu = new List<MenuEntry>();
        if (i.IsDir) menu.Add(new MenuEntry(L("Show what is inside"), new RelayCommand(() => OpenFolder(i.Path))));
        foreach (var a in i.Actions ?? [])
        {
            switch (a)
            {
                case K.Open: menu.Add(Act(pc ? L("Show in File Explorer") : L("Open on the phone"), K.Open, i.Path, i.Name)); break;
                case K.Cloud: menu.Add(Act(L("Move to the cloud drive (frees {0})", Sz(i.Bytes)), K.Cloud, i.Path, i.Name, confirm: true)); break;
                case K.Recycle: menu.Add(Act(pc ? L("Put in the Recycle Bin ({0}, can be restored)", Sz(i.Bytes)) : L("Move to the phone's trash ({0}, can be restored)", Sz(i.Bytes)), K.Recycle, i.Path, i.Name, confirm: true)); break;
                case K.Delete: menu.Add(Act(L("Delete for good (frees {0})", Sz(i.Bytes)), K.Delete, i.Path, i.Name, confirm: true, danger: true)); break;
            }
        }
        string when = i.Modified is { } m ? m.ToLocalTime().ToString("d MMM yyyy") : "";
        string sub = i.IsDir ? L("{0} files", i.Files) + (when.Length > 0 ? " · " + when : "") : when;
        if (i.Note.Length > 0) sub += " · " + LocalizationManager.T(i.Note);
        if (i.Protected) sub += " · " + L("Windows or programs need this");
        return new SpaceLine
        {
            Name = i.Name, Sub = sub, Size = Sz(i.Bytes), Fraction = (double)i.Bytes / biggest, IsFolder = i.IsDir, Path = i.Path,
            Menu = CanActOn(pc) ? menu : menu.Where(e => e.Header == L("Show what is inside")).ToList(),
            Open = i.IsDir ? new RelayCommand(() => OpenFolder(i.Path)) : null
        };
    }

    private SpaceLine AppRow(SpaceApp a, long biggest, bool pc)
    {
        var menu = new List<MenuEntry>();
        foreach (var k in a.Actions ?? [])
        {
            if (k == K.Uninstall) menu.Add(Act(L("Uninstall (frees about {0})", Sz(a.Bytes)), K.Uninstall, a.Id, a.Name, confirm: true, danger: true));
            if (k == K.AppInfo) menu.Add(Act(L("App info on the phone (clear cache, force stop)"), K.AppInfo, a.Id, a.Name));
        }
        return new SpaceLine
        {
            Name = a.Name, Sub = string.Join(" · ", new[] { a.Publisher, a.Version, a.Installed?.ToLocalTime().ToString("d MMM yyyy") ?? "" }.Where(s => s.Length > 0)),
            Size = a.Bytes > 0 ? Sz(a.Bytes) : "—", Fraction = (double)a.Bytes / biggest, Menu = CanActOn(pc) ? menu : []
        };
    }

    private SpaceLine RamAppRow(SpaceRamApp a, long total, bool pc)
    {
        var menu = new List<MenuEntry>();
        foreach (var k in a.Actions ?? [])
        {
            if (k == K.Close) menu.Add(Act(L("Close (frees about {0} of RAM)", Sz(a.Bytes)), K.Close, a.Id, a.Title));
            if (k == K.Kill) menu.Add(Act(L("End task now (unsaved work is lost)"), K.Kill, a.Id, a.Title, confirm: true, danger: true));
            if (k == K.AppInfo) menu.Add(Act(L("App info on the phone (force stop)"), K.AppInfo, a.Id, a.Title));
        }
        return new SpaceLine
        {
            Name = a.Title, Sub = a.Processes > 1 ? L("{0} processes", a.Processes) : a.Name, Size = Sz(a.Bytes),
            Fraction = total > 0 ? (double)a.Bytes / total : 0, Menu = CanActOn(pc) ? menu : []
        };
    }

    private SpaceLine CleanRow(SpaceCleanup c, bool pc) => new()
    {
        Name = LocalizationManager.T(c.Label), Sub = LocalizationManager.T(c.Description) + (c.Count > 0 ? " · " + L("{0} files", c.Count) : ""),
        Size = c.Bytes > 0 ? Sz(c.Bytes) : "",
        Menu = CanActOn(pc) && (c.Safe || c.Id == "bin") && (c.Bytes > 0 || c.Id == "windows-cleanup")
            ? [Act(c.Id == "windows-cleanup" ? L("Open Disk Clean-up") : L("Clean now (frees {0})", Sz(c.Bytes)), c.Id == "bin" ? K.EmptyBin : K.Clean, c.Id, c.Label, confirm: c.Id != "windows-cleanup")]
            : []
    };

    private SpaceLine StartupRow(SpaceStartup s) => new()
    {
        Name = s.Name, Sub = (s.Enabled ? L("Starts with Windows") : L("Switched off")) + " · " + LocalizationManager.T(s.Source) + " · " + s.Command,
        Size = s.Enabled ? L("On") : L("Off"),
        Menu = [Act(s.Enabled ? L("Do not start with Windows") : L("Start with Windows again"), s.Enabled ? K.StartupOff : K.StartupOn, s.Id, s.Name)]
    };

    private void ShowDuplicates(IReadOnlyList<IReadOnlyList<SpaceItem>>? groups)
    {
        if (groups == null) { DuplicatesText = L("Looking for files that exist twice… (this takes a minute)"); Duplicates.Clear(); return; }
        long waste = groups.Sum(g => g[0].Bytes * (g.Count - 1));
        DuplicatesText = groups.Count == 0 ? L("No duplicate files found in your folders.") : L("{0} files exist more than once; removing the extra copies frees {1}.", groups.Count, Sz(waste));
        long biggest = Math.Max(1, groups.Count > 0 ? groups.Max(g => g[0].Bytes) : 1);
        Fill(Duplicates, groups.Take(60).SelectMany(g => g.Skip(1).Select(i => ItemRow(i with { Name = i.Name + "  (" + L("copy of {0}", g[0].Path) + ")" }, biggest, pc: true))));
    }

    [RelayCommand]
    private void FindDuplicates() => ShowDuplicates(Host.Space.Duplicates(fresh: true));

    [RelayCommand]
    private void Rescan()
    {
        if (SelectedDevice is { IsPc: true }) { Host.Space.Index.Rescan(); Say(L("Reading the disks again; the numbers update as it goes."), "ok"); }
        else if (SelectedDevice is { } d) Send(d.Id, "report", "", L("a fresh report"));
    }

    private bool CanActOn(bool pc) => pc || CanAct;

    private MenuEntry Act(string header, string kind, string target, string what, bool confirm = false, bool danger = false) =>
        new(header, new AsyncRelayCommand(() => RunAsync(header, kind, target, what, confirm, danger)));

    private async Task RunAsync(string header, string kind, string target, string what, bool confirm, bool danger)
    {
        if (confirm && !Confirm(L("{0}\n\n{1}", what, header) + (danger ? "\n\n" + L("This cannot be undone.") : ""), L("Space & RAM"))) return;
        if (SelectedDevice is { IsPc: false } phone) { Send(phone.Id, kind, target, what); return; }
        Say(L("Working on {0}…", what), "info");
        var r = await Host.Space.ActionAsync(kind, target);
        Say(LocalizationManager.T(r.Message) + (r.FreedBytes > 0 ? " " + L("Freed {0}.", Sz(r.FreedBytes)) : ""), r.Ok ? "ok" : "warn");
        Refresh(full: true);
    }

    // ---- a phone ---------------------------------------------------------------------------------------------------------

    private void RefreshPhone(string id)
    {
        var r = Host.Space.Phones.Get(id);
        if (r == null)
        {
            DeviceStatus = L("This phone has not shared anything yet. Open the Cloud-SSD app on it.");
            DeviceTone = "muted";
            CanAct = false;
            return;
        }
        bool live = PhoneSpaceStore.IsLive(r);
        CanAct = live && r.AllowControl;
        DeviceTone = live ? "ok" : "muted";
        DeviceStatus = live
            ? (r.AllowControl ? L("Live from the phone. Actions are done on the phone; Android may ask there first.") : L("Live from the phone. It allows looking only (change that in the phone app's Settings)."))
            : L("Last seen {0}. This is the phone's last report; open the Cloud-SSD app on it for live numbers.", Ago(r.At));
        var d = r.Data;
        try
        {
            if (d["summary"]?.Deserialize<SpaceSummary>(Json) is { } s) ShowSummary(s, live: CanAct);
            var ram = d["ramApps"]?.Deserialize<List<SpaceRamApp>>(Json) ?? [];
            long ramTotal = d["summary"]?["ram"]?["total"]?.GetValue<long>() ?? 1;
            Fill(RamApps, ram.Select(a => RamAppRow(a, ramTotal, pc: false)));
            var apps = d["apps"]?.Deserialize<List<SpaceApp>>(Json) ?? [];
            long biggest = Math.Max(1, apps.Count > 0 ? apps.Max(a => a.Bytes) : 1);
            Fill(Apps, apps.Select(a => AppRow(a, biggest, pc: false)));
            Fill(Cleanups, (d["cleanups"]?.Deserialize<List<SpaceCleanup>>(Json) ?? []).Select(c => CleanRow(c, pc: false)));
            Fill(LargeFiles, (d["large"]?.Deserialize<List<SpaceItem>>(Json) ?? []).Select(f => ItemRow(f, Math.Max(1, f.Bytes), pc: false)));
            Startup.Clear();
            Duplicates.Clear();
            DuplicatesText = "";
            var folders = d["folders"] as JsonObject;
            var path = string.IsNullOrEmpty(FolderPath) || FolderPath.Contains('\\') ? (string?)d["root"] ?? folders?.Select(f => f.Key).OrderBy(k => k.Length).FirstOrDefault() ?? "" : FolderPath;
            if (folders?[path]?.Deserialize<SpaceFolder>(Json) is { } folder) ShowFolder(folder, pc: false);
            else { FolderPath = path; FolderNote = L("Not in the phone's last report yet."); FolderItems.Clear(); }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { DeviceStatus = L("The phone's report could not be read: {0}", e.Message); }
    }

    private void OpenPhoneFolder(string id, string path)
    {
        var folders = Host.Space.Phones.Get(id)?.Data["folders"] as JsonObject;
        if (folders?[path]?.Deserialize<SpaceFolder>(Json) is { } f) { ShowFolder(f, pc: false); return; }
        FolderPath = path;
        FolderItems.Clear();
        FolderNote = L("Asking the phone for this folder…");
        Send(id, "folder", path, path, quiet: true);
    }

    private void Send(string id, string kind, string target, string what, bool quiet = false)
    {
        var c = Host.Space.Phones.Enqueue(id, kind, target);
        if (c == null) { Say(L("The phone does not allow the PC to manage it. Turn that on in the phone app's Settings."), "warn"); return; }
        if (!quiet) Say(L("Sent to the phone: {0}. It is done when the Cloud-SSD app there picks it up.", what), "info");
        _ = WatchAsync(c.Id, what, quiet);
    }

    private async Task WatchAsync(string commandId, string what, bool quiet)
    {
        for (int i = 0; i < 90; i++)
        {
            await Task.Delay(2000);
            var c = Host.Space.Phones.Status(commandId);
            if (c is not { Finished: true }) continue;
            if (!quiet || !c.State.Equals("done")) Say((c.Message ?? what) + (c.FreedBytes > 0 ? " " + L("Freed {0}.", Sz(c.FreedBytes)) : ""), c.State == "done" ? "ok" : "warn");
            Refresh(full: true);
            return;
        }
        if (!quiet) Say(L("The phone has not answered yet. Open the Cloud-SSD app on it."), "warn");
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------

    private static string Ago(DateTime? at)
    {
        if (at is not { } t || t == DateTime.MinValue) return L("never");
        var s = DateTime.UtcNow - t;
        return s.TotalMinutes < 1 ? L("just now") : s.TotalHours < 1 ? L("{0} min ago", (int)s.TotalMinutes) : s.TotalDays < 1 ? L("{0} h ago", (int)s.TotalHours) : t.ToLocalTime().ToString("d MMM, HH:mm");
    }

    private static void Fill(ObservableCollection<SpaceLine> target, IEnumerable<SpaceLine> rows)
    {
        target.Clear();
        foreach (var r in rows) target.Add(r);
    }
}
