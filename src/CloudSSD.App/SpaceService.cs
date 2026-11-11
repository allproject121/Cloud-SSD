using System.Diagnostics;
using System.Security.Cryptography;
using CloudSSD.Cache;
using CloudSSD.Core;
using CloudSSD.Mount;
using K = CloudSSD.App.SpaceActionKinds;

namespace CloudSSD.App;

/// <summary>
/// Everything the "Space &amp; RAM" page knows about this PC: where the disk space goes (live, from
/// <see cref="SpaceIndex"/>), what is in each folder, installed programs, startup apps, apps in RAM, what can be cleaned,
/// large and duplicate files — and doing what the ⋮ menus offer, measuring how much space it really freed. The same
/// answers go to the phone over the bridge. Windows' own folders, programs' folders and Cloud-SSD's own data are never
/// offered for deleting, and nothing is ever suggested from inside Downloads\Project's (the user's code).
/// </summary>
public sealed class SpaceService : IDisposable
{
    private readonly AppHost _host;
    private readonly object _cacheGate = new();
    private (DateTime At, IReadOnlyList<SpaceCleanup> List)? _cleanups;
    private (DateTime At, IReadOnlyList<IReadOnlyList<SpaceItem>> Groups)? _duplicates;
    private int _findingDuplicates;

    public SpaceService(AppHost host)
    {
        _host = host;
        Index = new SpaceIndex(Path.Combine(host.Paths.DataRoot, "space", "pc.json"), FixedDrives, () => GameActivity.IsPlaying(host));
        Phones = new PhoneSpaceStore(Path.Combine(host.Paths.DataRoot, "space"));
    }

    public SpaceIndex Index { get; }
    /// <summary>What the phones reported about themselves, and what the PC asked them to do.</summary>
    public PhoneSpaceStore Phones { get; }

    public void Start() => Index.Start(TimeSpan.FromSeconds(45));
    public void Dispose() => Index.Dispose();

    /// <summary>The PC's own disks: fixed drives, without the cloud drive letters.</summary>
    private IEnumerable<string> FixedDrives()
    {
        var cloud = _host.Mounts.Sessions.Select(s => char.ToUpperInvariant(s.DriveLetter)).ToHashSet();
        foreach (var d in DriveInfo.GetDrives())
        {
            bool ok;
            try { ok = d.IsReady && d.DriveType == DriveType.Fixed && d.DriveFormat is "NTFS" or "ReFS" or "exFAT" or "FAT32"; }
            catch (IOException) { ok = false; }
            if (ok && !cloud.Contains(char.ToUpperInvariant(d.Name[0]))) yield return d.RootDirectory.FullName;
        }
    }

    // ---- what may be touched ------------------------------------------------------------------------------------------

    private static readonly string[] RootSystemNames = ["pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log.tmp", "System Volume Information", "$Recycle.Bin", "Recovery", "$WinREAgent", "Config.Msi", "Boot", "bootmgr", "BOOTNXT"];

    /// <summary>
    /// True for what the ⋮ menu must not delete or move: disk roots, Windows, programs' folders (uninstall them
    /// instead), ProgramData, the Recycle Bin and page files, the user's profile and AppData roots themselves,
    /// Cloud-SSD's own program, data and cache, and the cloud drives.
    /// </summary>
    public bool IsProtected(string path)
    {
        string full;
        try { full = Path.GetFullPath(path).TrimEnd('\\'); } catch (Exception) { return true; }
        if (full.Length <= 2) return true;
        bool Under(string? root) => !string.IsNullOrEmpty(root) && (full.Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
        bool Is(string? p) => !string.IsNullOrEmpty(p) && full.Equals(p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        var parent = Path.GetDirectoryName(full);
        if (parent != null && parent.Length <= 3 && RootSystemNames.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase)) return true;
        foreach (var f in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
            if (Under(Environment.GetFolderPath(f))) return true;
        if (Under(_host.Paths.DataRoot) || Under(_host.Paths.CacheRootFor(_host.Settings)) || Under(AppPaths.AppDirectory)) return true;
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Is(profile) || Is(Path.Combine(profile, "AppData")) || Is(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            || Is(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)) || Is(Path.Combine(profile, "AppData", "LocalLow"))) return true;
        if (Is(Path.Combine(profile, "Desktop")) || Is(Path.Combine(profile, "Documents")) || Is(Path.Combine(profile, "Downloads"))) return true;
        if (parent != null && parent.Equals(Path.GetDirectoryName(profile), StringComparison.OrdinalIgnoreCase)) return true; // C:\Users\<anyone>
        foreach (var s in _host.Mounts.Sessions) if (Under(s.DriveLetter + ":\\")) return true;
        return false;
    }

    /// <summary>Never suggested by a clean-up or a list (the user's own code lives there); it can still be opened and handled by hand.</summary>
    internal static bool IsUserCode(string path) => path.Contains(@"\Downloads\Project's\", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(@"\Downloads\Project's", StringComparison.OrdinalIgnoreCase);

    private string[] ItemActions(string path, bool isDir) =>
        IsProtected(path) ? [K.Open] : [K.Open, K.Cloud, K.Recycle, K.Delete];

    // ---- overview -------------------------------------------------------------------------------------------------------

    public SpaceSummary Summary()
    {
        var volumes = new List<SpaceVolume>();
        foreach (var root in FixedDrives())
        {
            try
            {
                var d = new DriveInfo(root);
                volumes.Add(new SpaceVolume(root, string.IsNullOrEmpty(d.VolumeLabel) ? root.TrimEnd('\\') : $"{root.TrimEnd('\\')} {d.VolumeLabel}", d.TotalSize, d.AvailableFreeSpace));
            }
            catch (IOException) { }
        }
        var groups = new List<SpaceGroup>();
        string sys = Path.GetPathRoot(Environment.SystemDirectory)!;
        if (volumes.FirstOrDefault(v => v.Root.Equals(sys, StringComparison.OrdinalIgnoreCase)) is { } c) groups.AddRange(SystemDriveGroups(c));
        foreach (var v in volumes.Where(v => !v.Root.Equals(sys, StringComparison.OrdinalIgnoreCase)))
            groups.Add(new SpaceGroup("drive-" + v.Root[0], v.Label, v.Total - v.Free, 0, "Files on this disk", v.Root, [K.Open]));
        return new SpaceSummary(Environment.MachineName, "pc", DateTime.UtcNow, Index.ScannedAt, Index.Ready, Index.Progress, volumes, groups, Ram());
    }

    private long Size(string? path)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        if (Index.SizeOf(path) is { } s) return s.Bytes;
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; } catch (IOException) { return 0; }
    }

    private IEnumerable<SpaceGroup> SystemDriveGroups(SpaceVolume v)
    {
        string root = v.Root;
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appData = Path.Combine(profile, "AppData");
        string temp = Path.GetTempPath().TrimEnd('\\');
        string cache = _host.Paths.CacheRootFor(_host.Settings);
        string bin = Path.Combine(root, "$Recycle.Bin");

        long used = v.Total - v.Free;
        long windows = Size(win), programs = Size(pf) + (pf86 != pf ? Size(pf86) : 0), programData = Size(pd);
        long sysFiles = new[] { "pagefile.sys", "hiberfil.sys", "swapfile.sys" }.Sum(f => Size(Path.Combine(root, f)));
        long binBytes = RecycleBin.Size().Bytes;
        long tempBytes = temp.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? Size(temp) : 0;
        long cacheBytes = cache.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? Size(cache) : 0;
        long appDataBytes = Math.Max(0, Size(appData) - (Inside(temp, appData) ? tempBytes : 0) - (Inside(cache, appData) ? cacheBytes : 0));
        long userBytes = Math.Max(0, Size(profile) - Size(appData));
        long readable = Index.Total(root) ?? 0;
        long known = windows + programs + programData + sysFiles + Size(bin) + tempBytes + cacheBytes + appDataBytes + userBytes;
        long other = Math.Max(0, readable - known);
        long hidden = Math.Max(0, used - Math.Max(readable, known));

        yield return new SpaceGroup("windows", "Windows", windows, 0, "Windows itself. Use Windows' Disk Clean-up for old updates.", win, [K.Open]);
        yield return new SpaceGroup("programs", "Installed programs", programs, 0, "Uninstall what you do not use (Apps).", pf, [K.Open]);
        yield return new SpaceGroup("user", "Your files", userBytes, 0, "Desktop, Documents, Downloads, Pictures, Videos…", profile, [K.Open]);
        yield return new SpaceGroup("appdata", "App data", appDataBytes, 0, "Settings and caches of your apps.", appData, [K.Open]);
        yield return new SpaceGroup("cloud-cache", "Cloud-SSD cache", cacheBytes, 0, "Cloud files kept on the PC so they open fast. Size is set on the PC Storage page.", cache, []);
        yield return new SpaceGroup("temp", "Temporary files", tempBytes, 0, "Safe to clean; apps make them again when needed.", temp, [K.Open, K.Clean]);
        yield return new SpaceGroup("bin", "Recycle Bin", binBytes, 0, "Deleted files that can still be restored.", bin, [K.EmptyBin]);
        yield return new SpaceGroup("sysfiles", "Page and sleep files", sysFiles, 0, "Windows' virtual memory and sleep file. Leave them.", null, []);
        yield return new SpaceGroup("programdata", "Shared program data", programData, 0, "Data programs keep for all users.", pd, [K.Open]);
        yield return new SpaceGroup("other", "Other folders", other, 0, "Everything else on this disk.", root, [K.Open]);
        if (hidden > 0) yield return new SpaceGroup("hidden", "System and other users", hidden, 0, "Parts Windows lets only an administrator read.", null, []);
    }

    private static bool Inside(string path, string folder) => path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    public SpaceRam Ram()
    {
        var s = MemoryHealth.Read(_host.Paths.CacheRootFor(_host.Settings), topApps: 60);
        long used = s.PhysicalTotalBytes - s.PhysicalAvailableBytes;
        long apps = Math.Min(used, s.TopApps.Sum(a => a.WorkingSetBytes));
        long system = Math.Min(Math.Max(0, used - apps), s.SystemBytes);
        return new SpaceRam(s.PhysicalTotalBytes, s.PhysicalAvailableBytes,
        [
            new SpaceGroup("apps", "Apps", apps, s.TopApps.Count, "Programs you have open."),
            new SpaceGroup("windows", "Windows and services", system, s.SystemProcesses, "Windows itself, drivers and Cloud-SSD."),
            new SpaceGroup("other", "Shared and cached", Math.Max(0, used - apps - system), 0, "Memory Windows shares between apps or keeps for speed."),
            new SpaceGroup("free", "Free", s.PhysicalAvailableBytes, 0, "Ready to use."),
        ]);
    }

    public IReadOnlyList<SpaceRamApp> RamApps()
    {
        var s = MemoryHealth.Read(_host.Paths.CacheRootFor(_host.Settings), topApps: 60);
        return s.TopApps.Select(a => new SpaceRamApp(a.Name, a.Name, a.Title ?? a.Name, a.WorkingSetBytes, a.Processes,
            a.CanClose ? [K.Close, K.Kill] : [K.Kill])).ToList();
    }

    // ---- folders ----------------------------------------------------------------------------------------------------------

    /// <summary>What is inside a folder (biggest first); null when it does not exist.</summary>
    public SpaceFolder? Folder(string path, int max = 400, CancellationToken ct = default)
    {
        string full;
        try { full = Path.GetFullPath(path); } catch (Exception) { return null; }
        if (!Directory.Exists(full)) return null;
        if (!FixedDrives().Any(r => full.StartsWith(r, StringComparison.OrdinalIgnoreCase))) return null;
        var items = Index.List(full, ct);
        var shown = items.Take(max).Select(i => new SpaceItem(i.Name, i.Path, i.IsDir, i.Bytes, i.Files, i.Modified == DateTime.MinValue ? null : i.Modified,
            DiskUsage.NoteFor(i.Path), IsProtected(i.Path), ItemActions(i.Path, i.IsDir))).ToList();
        if (items.Count > max)
        {
            var rest = items.Skip(max).ToList();
            shown.Add(new SpaceItem($"{rest.Count} smaller items", full, false, rest.Sum(r => r.Bytes), rest.Sum(r => r.Files), null, "", true, []));
        }
        var trimmed = full.TrimEnd('\\');
        bool isRoot = trimmed.Length <= 2;
        string? parent = isRoot ? null : Path.GetDirectoryName(trimmed);   // "C:\Users" → "C:\"
        string name = isRoot ? trimmed + "\\" : Path.GetFileName(trimmed);
        return new SpaceFolder(full, name, parent, items.Sum(i => i.Bytes), DateTime.UtcNow, shown, IsProtected(full), DiskUsage.NoteFor(full));
    }

    // ---- programs and startup -----------------------------------------------------------------------------------------------

    public IReadOnlyList<SpaceApp> Apps() =>
        InstalledPrograms.Read().Select(p =>
        {
            long bytes = p.Bytes;
            if (bytes == 0 && !string.IsNullOrEmpty(p.Location) && Index.SizeOf(p.Location) is { } s) bytes = s.Bytes;
            return new SpaceApp(p.Id, p.Name, p.Publisher, bytes, p.Version, p.Installed, p.UninstallCommand != null ? [K.Uninstall] : []);
        }).OrderByDescending(a => a.Bytes).ToList();

    public IReadOnlyList<SpaceStartup> Startup() =>
        StartupApps.Read().Select(e => new SpaceStartup(e.Id, e.Name, e.Command, e.Enabled, e.Source + (e.CanChange ? "" : " (needs admin to change)"))).ToList();

    // ---- clean-ups ----------------------------------------------------------------------------------------------------------

    private sealed record CleanupDef(string Id, string Label, string Description, Func<IEnumerable<string>> Roots, Func<FileInfo, bool> Take, Func<string?> Blocked);

    private static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly (string Name, string Process, string Root)[] Browsers =
    [
        ("Microsoft Edge", "msedge", Path.Combine(Local, "Microsoft", "Edge", "User Data")),
        ("Google Chrome", "chrome", Path.Combine(Local, "Google", "Chrome", "User Data")),
        ("Brave", "brave", Path.Combine(Local, "BraveSoftware", "Brave-Browser", "User Data")),
    ];

    private static IEnumerable<string> BrowserCacheDirs(string userData)
    {
        if (!Directory.Exists(userData)) yield break;
        foreach (var profile in Directory.EnumerateDirectories(userData))
        {
            var n = Path.GetFileName(profile);
            if (n != "Default" && !n.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var c in new[] { Path.Combine("Cache", "Cache_Data"), "Code Cache", "GPUCache" })
                if (Directory.Exists(Path.Combine(profile, c))) yield return Path.Combine(profile, c);
        }
    }

    private IEnumerable<CleanupDef> CleanupDefs()
    {
        var now = DateTime.UtcNow;
        yield return new CleanupDef("temp", "Temporary files", "Files apps left in the temp folder more than a day ago.",
            () => [Path.GetTempPath()], f => now - f.LastWriteTimeUtc > TimeSpan.FromDays(1), () => null);
        yield return new CleanupDef("crash-dumps", "Crash reports", "Memory dumps and error reports of programs that crashed.",
            () => [TempCleanup.CrashDumpDirectory, Path.Combine(Local, "Microsoft", "Windows", "WER")], _ => true, () => null);
        foreach (var b in Browsers)
        {
            var browser = b;
            yield return new CleanupDef("browser-" + browser.Process, browser.Name + " cache", "Saved copies of web pages; the browser downloads them again. Your logins and history stay.",
                () => BrowserCacheDirs(browser.Root), _ => true,
                () => MemoryHealth.IsRunning(browser.Process) ? $"Close {browser.Name} first." : null);
        }
        yield return new CleanupDef("dev-cache", "Developer tool caches", "Download caches of npm, pip, NuGet, Yarn, Gradle and Go. They download again when needed.",
            () => new[]
            {
                Path.Combine(Local, "npm-cache"), Path.Combine(Local, "pip", "Cache"), Path.Combine(Local, "NuGet", "v3-cache"),
                Path.Combine(Local, "Yarn", "Cache"), Path.Combine(Profile, ".gradle", "caches"), Path.Combine(Local, "go-build"),
            }.Where(Directory.Exists), _ => true, () => null);
        yield return new CleanupDef("old-installers", "Old installers in Downloads", "Setup files (.exe, .msi) in Downloads older than 30 days. Programs already installed keep working.",
            () => [Path.Combine(Profile, "Downloads")],
            f => f.DirectoryName!.Equals(Path.Combine(Profile, "Downloads"), StringComparison.OrdinalIgnoreCase)
                 && f.Extension.ToLowerInvariant() is ".exe" or ".msi" or ".msix" or ".appx" && now - f.LastWriteTimeUtc > TimeSpan.FromDays(30),
            () => null);
    }

    /// <summary>Everything that can be cleaned in one go, with what it frees (measured, cached for a few minutes).</summary>
    public IReadOnlyList<SpaceCleanup> Cleanups(bool fresh = false)
    {
        lock (_cacheGate)
            if (!fresh && _cleanups is { } c && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(5)) return c.List;
        var list = new List<SpaceCleanup>();
        var (binBytes, binItems) = RecycleBin.Size();
        list.Add(new SpaceCleanup("bin", "Recycle Bin", "Files you deleted. After emptying they cannot be restored.", binBytes, binItems, false));
        foreach (var d in CleanupDefs())
        {
            long bytes = 0, count = 0;
            foreach (var f in Files(d)) { bytes += f.Length; count++; }
            var blocked = d.Blocked();
            list.Add(new SpaceCleanup(d.Id, d.Label, blocked ?? d.Description, bytes, count, blocked == null, d.Roots().FirstOrDefault()));
        }
        // Cache of cloud drives that are not connected: the drive cannot use it now.
        long off = 0;
        foreach (var m in _host.Settings.Mounts.Where(m => _host.Mounts.Get(m.Id) is not { State: MountState.Mounted }))
            off += Size(_host.Paths.MountCacheDir(_host.Settings, m.Id));
        list.Add(new SpaceCleanup("cloud-cache-off", "Cache of disconnected cloud drives", "Cloud files kept for drives that are switched off. They download again when needed.", off, 0, true));
        list.Add(new SpaceCleanup("windows-cleanup", "Windows Disk Clean-up", "Opens Windows' own tool for old updates and system files (asks for admin).", 0, 0, true));
        var result = list.OrderByDescending(x => x.Bytes).ToList();
        lock (_cacheGate) _cleanups = (DateTime.UtcNow, result);
        return result;
    }

    private static IEnumerable<FileInfo> Files(CleanupDef d)
    {
        foreach (var root in d.Roots())
        {
            if (!Directory.Exists(root) || IsUserCode(root)) continue;
            IEnumerable<FileInfo> files;
            try { files = new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var f in files)
            {
                bool take;
                try { take = d.Take(f); } catch (Exception) { take = false; }
                if (take) yield return f;
            }
        }
    }

    private SpaceResult RunCleanup(string id)
    {
        switch (id)
        {
            case "bin":
                return RecycleBin.Empty() ? new SpaceResult(true, "Recycle Bin emptied.") : new SpaceResult(false, "Windows could not empty the Recycle Bin.");
            case "windows-cleanup":
                try { Process.Start(new ProcessStartInfo("cleanmgr.exe") { UseShellExecute = true }); return new SpaceResult(true, "Disk Clean-up is open on the PC."); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception) { return new SpaceResult(false, "Could not open Disk Clean-up: " + e.Message); }
            case "cloud-cache-off":
            {
                long freed = 0;
                foreach (var m in _host.Settings.Mounts.Where(m => _host.Mounts.Get(m.Id) is not { State: MountState.Mounted }))
                {
                    var r = CacheMaintenance.ClearMountCache(_host.Paths.MountCacheDir(_host.Settings, m.Id), _host.Paths.CacheRootFor(_host.Settings));
                    freed += r.BytesFreed;
                }
                return new SpaceResult(true, "Cache of disconnected drives cleared.", freed);
            }
        }
        var def = CleanupDefs().FirstOrDefault(d => d.Id == id);
        if (def == null) return new SpaceResult(false, "Unknown clean-up.");
        if (def.Blocked() is { } why) return new SpaceResult(false, why);
        long bytes = 0;
        int files = 0, skipped = 0;
        foreach (var f in Files(def).ToList())
        {
            try
            {
                long len = f.Length;
                if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
                f.Delete();
                bytes += len;
                files++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
        }
        foreach (var root in def.Roots()) RemoveEmptyDirs(root);
        return new SpaceResult(true, $"Removed {files} files" + (skipped > 0 ? $" ({skipped} were in use and stay)." : "."), bytes);
    }

    private static void RemoveEmptyDirs(string root)
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .OrderByDescending(d => d.Length))
            {
                try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ---- large and duplicate files --------------------------------------------------------------------------------------------

    /// <summary>The biggest files on the PC (from the live map), without Windows' own and the user's code.</summary>
    public IReadOnlyList<SpaceItem> LargeFiles(int max = 100) =>
        Index.BigFiles(400).Where(f => !IsProtected(f.Path) && !IsUserCode(f.Path)).Take(max)
            .Select(f => new SpaceItem(Path.GetFileName(f.Path), f.Path, false, f.Bytes, 1, f.Modified, "", false, ItemActions(f.Path, false))).ToList();

    /// <summary>
    /// Files that exist more than once with the same content in the user's own folders (Desktop, Documents, Downloads,
    /// Pictures, Videos, Music), biggest waste first. Takes a while, so the last result is kept for half an hour;
    /// null while the first search still runs (it starts in the background).
    /// </summary>
    public IReadOnlyList<IReadOnlyList<SpaceItem>>? Duplicates(bool fresh = false)
    {
        lock (_cacheGate)
            if (!fresh && _duplicates is { } d && DateTime.UtcNow - d.At < TimeSpan.FromMinutes(30)) return d.Groups;
        if (Interlocked.Exchange(ref _findingDuplicates, 1) == 0)
            _ = Task.Run(() =>
            {
                try
                {
                    var found = FindDuplicates(TimeSpan.FromSeconds(90));
                    lock (_cacheGate) _duplicates = (DateTime.UtcNow, found);
                }
                finally { Interlocked.Exchange(ref _findingDuplicates, 0); }
            });
        lock (_cacheGate) return _duplicates?.Groups;
    }

    internal IReadOnlyList<IReadOnlyList<SpaceItem>> FindDuplicates(TimeSpan budget, IEnumerable<string>? roots = null)
    {
        var clock = Stopwatch.StartNew();
        roots ??= new[] { "Desktop", "Documents", "Downloads", "Pictures", "Videos", "Music" }.Select(n => Path.Combine(Profile, n));
        var bySize = new Dictionary<long, List<FileInfo>>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<FileInfo> files;
            try { files = new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System }); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var f in files)
            {
                if (clock.Elapsed > budget) break;
                if (f.Length < ByteSize.MiB || IsUserCode(f.FullName)) continue;
                (bySize.TryGetValue(f.Length, out var l) ? l : bySize[f.Length] = []).Add(f);
            }
        }
        var groups = new List<IReadOnlyList<SpaceItem>>();
        foreach (var (size, same) in bySize.Where(k => k.Value.Count > 1).OrderByDescending(k => k.Key))
        {
            if (clock.Elapsed > budget) break;
            foreach (var g in same.GroupBy(Hash).Where(g => g.Key != null && g.Count() > 1))
                groups.Add(g.OrderBy(f => f.FullName.Length).Select(f => new SpaceItem(f.Name, f.FullName, false, size, 1, f.LastWriteTimeUtc, "", false, ItemActions(f.FullName, false))).ToList());
        }
        return groups.OrderByDescending(g => g[0].Bytes * (g.Count - 1)).Take(200).ToList();
    }

    /// <summary>SHA-256 of the whole file (sizes are already equal); null when it cannot be read.</summary>
    private static string? Hash(FileInfo f)
    {
        try
        {
            using var s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
            return Convert.ToHexString(SHA256.HashData(s));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    // ---- doing it -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs one ⋮ action. <paramref name="target"/> is a path (recycle, delete, cloud, open), a clean-up id, a
    /// program id (uninstall), an app name (close, kill) or a startup id. FreedBytes is the disk's free space after
    /// minus before, so it is what really came free.
    /// </summary>
    public async Task<SpaceResult> ActionAsync(string kind, string target, bool leaveShortcut = true, CancellationToken ct = default)
    {
        try
        {
            switch (kind)
            {
                case K.Recycle:
                case K.Delete:
                case K.Cloud:
                case K.Open:
                {
                    string full = Path.GetFullPath(target);
                    if (!File.Exists(full) && !Directory.Exists(full)) return new SpaceResult(false, "It is not there any more.");
                    if (kind == K.Open)
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"") { UseShellExecute = true });
                        return new SpaceResult(true, "Shown in File Explorer on the PC.");
                    }
                    if (IsProtected(full)) return new SpaceResult(false, "Windows, programs and Cloud-SSD's own files cannot be removed here.");
                    if (kind == K.Cloud)
                    {
                        var moved = await _host.Offloader.MoveToCloudAsync(full, leaveShortcut, ct);
                        Index.MarkDirty(full);
                        return new SpaceResult(moved.Success, moved.Message, moved.Success ? moved.BytesFreed : 0);
                    }
                    string drive = Path.GetPathRoot(full)!;
                    long before = DiskInfo.FreeSpace(drive).FreeForUser;
                    if (kind == K.Recycle)
                    {
                        if (!await Task.Run(() => RecycleBin.Send(full), ct)) return new SpaceResult(false, "Windows could not put it in the Recycle Bin (it may be in use).");
                        Index.MarkDirty(full);
                        return new SpaceResult(true, "In the Recycle Bin. The space comes free when the bin is emptied; until then it can be restored.");
                    }
                    await Task.Run(() =>
                    {
                        if (Directory.Exists(full))
                        {
                            foreach (var f in new DirectoryInfo(full).EnumerateFiles("*", SearchOption.AllDirectories))
                                if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
                            Directory.Delete(full, recursive: true);
                        }
                        else
                        {
                            File.SetAttributes(full, File.GetAttributes(full) & ~FileAttributes.ReadOnly);
                            File.Delete(full);
                        }
                    }, ct);
                    Index.MarkDirty(full);
                    return new SpaceResult(true, "Deleted.", Math.Max(0, DiskInfo.FreeSpace(drive).FreeForUser - before));
                }
                case K.Clean:
                case K.EmptyBin:
                {
                    string sys = Path.GetPathRoot(Environment.SystemDirectory)!;
                    long before = DiskInfo.FreeSpace(sys).FreeForUser;
                    var r = await Task.Run(() => RunCleanup(kind == K.EmptyBin ? "bin" : target), ct);
                    lock (_cacheGate) _cleanups = null;
                    long freed = Math.Max(r.FreedBytes, DiskInfo.FreeSpace(sys).FreeForUser - before);
                    return r with { FreedBytes = Math.Max(0, freed) };
                }
                case K.Uninstall:
                {
                    var p = InstalledPrograms.Read().FirstOrDefault(x => x.Id == target);
                    if (p == null) return new SpaceResult(false, "That program is not installed any more.");
                    return InstalledPrograms.StartUninstall(p)
                        ? new SpaceResult(true, $"The uninstaller of {p.Name} is open on the PC. Finish it there.")
                        : new SpaceResult(false, $"{p.Name} has no uninstaller. Use Windows Settings → Apps.");
                }
                case K.Close:
                {
                    int asked = MemoryHealth.RequestClose(target);
                    return asked > 0 ? new SpaceResult(true, $"Asked {target} to close. If it has unsaved work it asks on the PC.")
                        : new SpaceResult(false, $"{target} has no window to close. Use End task.");
                }
                case K.Kill:
                {
                    long before = MemoryHealth.ReadRam().AvailableBytes;
                    int ended = MemoryHealth.Kill(target);
                    if (ended == 0) return new SpaceResult(false, $"{target} could not be ended (Windows or Cloud-SSD needs it, or it runs as administrator).");
                    await Task.Delay(1000, ct);
                    return new SpaceResult(true, $"Ended {target}.", Math.Max(0, MemoryHealth.ReadRam().AvailableBytes - before));
                }
                case K.StartupOff:
                case K.StartupOn:
                    return StartupApps.SetEnabled(target, kind == K.StartupOn)
                        ? new SpaceResult(true, kind == K.StartupOn ? "It starts with Windows again." : "It no longer starts with Windows.")
                        : new SpaceResult(false, "This one is set for all users; change it in Task Manager as administrator.");
                case K.Rescan:
                    Index.Rescan();
                    return new SpaceResult(true, "Reading the disks again.");
                default:
                    return new SpaceResult(false, "Unknown action.");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return new SpaceResult(false, e.Message);
        }
    }
}
