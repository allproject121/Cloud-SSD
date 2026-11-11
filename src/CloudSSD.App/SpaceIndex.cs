using System.Text.Json;
using CloudSSD.Core;

namespace CloudSSD.App;

/// <summary>
/// A live map of where the space on the PC's own disks goes: every folder of at least <see cref="KeepDirBytes"/> with
/// its size, plus every file of at least <see cref="KeepFileBytes"/>. The first scan reads the whole disk in the
/// background (a minute or two); after that Windows tells us what changes and only those folders are measured again,
/// so the numbers stay current without reading the disk over and over. The last scan is saved, so the page has
/// numbers at once after a restart, and links (junctions) are never followed.
/// </summary>
public sealed class SpaceIndex : IDisposable
{
    /// <summary>Smaller folders are counted in their parent but not remembered on their own (they are measured when opened).</summary>
    public const long KeepDirBytes = ByteSize.MiB;
    /// <summary>Files this big are remembered on their own (the "Large files" list).</summary>
    public const long KeepFileBytes = 100 * ByteSize.MiB;
    /// <summary>A changed folder is measured again once nothing has changed in it for this long.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(8);
    /// <summary>Even with the watcher a full scan runs now and then (it catches what the watcher missed).</summary>
    public static readonly TimeSpan FullRescanEvery = TimeSpan.FromHours(6);

    internal sealed class Node(string name, Node? parent, bool isDir)
    {
        public string Name { get; } = name;
        public Node? Parent { get; set; } = parent;
        public bool IsDir { get; } = isDir;
        public long Bytes { get; set; }
        public long Files { get; set; }
        public DateTime Modified { get; set; }
        public Dictionary<string, Node>? Children { get; set; }

        public string FullPath
        {
            get
            {
                var parts = new List<string>();
                for (var n = this; n != null; n = n.Parent) parts.Add(n.Name);
                parts.Reverse();
                return parts.Count == 1 ? parts[0] : Path.Combine([.. parts]);
            }
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Node> _roots = new(StringComparer.OrdinalIgnoreCase);   // "C:\" → tree
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _snapshotFile;
    private readonly Func<IEnumerable<string>> _drives;
    private readonly Func<bool> _pause;
    private Timer? _timer;
    private DateTime _lastEvent = DateTime.MinValue;
    private DateTime _lastFullScan = DateTime.MinValue;
    private DateTime _lastSave = DateTime.MinValue;
    private long _scannedDirs, _expectedDirs = 1;
    private int _scanning;
    private bool _changedSinceSave;
    private CancellationTokenSource _cts = new();

    /// <param name="snapshotFile">Where the last scan is kept (DataRoot\space\pc.json).</param>
    /// <param name="drives">The disks to map ("C:\", "D:\"); cloud drive letters must not be in it.</param>
    /// <param name="pause">True while scanning should wait (a game is running).</param>
    public SpaceIndex(string snapshotFile, Func<IEnumerable<string>> drives, Func<bool>? pause = null)
    {
        _snapshotFile = snapshotFile;
        _drives = drives;
        _pause = pause ?? (() => false);
    }

    /// <summary>When the numbers were last complete (the end of the last full scan, or of the saved one).</summary>
    public DateTime? ScannedAt { get; private set; }
    /// <summary>True once a full scan of this run finished; before that the numbers are the saved ones.</summary>
    public bool Ready { get; private set; }
    public bool Scanning => _scanning != 0;
    public double Progress => Ready && !Scanning ? 1 : Math.Clamp((double)Interlocked.Read(ref _scannedDirs) / Math.Max(1, _expectedDirs), 0, 0.99);
    /// <summary>Raised (on a worker thread) when sizes changed after a scan or a re-measure.</summary>
    public event Action? Changed;

    /// <summary>Loads the saved map, then starts the first scan after <paramref name="delay"/> and keeps the map live.</summary>
    public void Start(TimeSpan delay)
    {
        LoadSnapshot();
        _timer = new Timer(_ => Tick(), null, delay, TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer?.Dispose();
        lock (_gate)
        {
            foreach (var w in _watchers.Values) w.Dispose();
            _watchers.Clear();
        }
        SaveSnapshot();
    }

    /// <summary>Starts a full scan now (unless one runs).</summary>
    public void Rescan() => _ = Task.Run(FullScan);

    private void Tick()
    {
        try
        {
            if (_scanning == 0 && (!Ready || DateTime.UtcNow - _lastFullScan > FullRescanEvery) && !_pause())
            {
                _ = Task.Run(FullScan);
                return;
            }
            if (Ready && _dirty.Count > 0 && DateTime.UtcNow - _lastEvent > Settle && !_pause()) MeasureDirty();
            if (_changedSinceSave && DateTime.UtcNow - _lastSave > TimeSpan.FromMinutes(10)) SaveSnapshot();
        }
        catch (Exception) { /* a failing pass must not stop the timer */ }
    }

    // ---- full scan ------------------------------------------------------------------------------------------------

    internal void FullScan()
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1) return;
        try
        {
            var prio = Thread.CurrentThread.Priority;
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            try
            {
                Interlocked.Exchange(ref _scannedDirs, 0);
                var drives = _drives().Select(Root).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var d in drives)
                {
                    if (_cts.IsCancellationRequested) return;
                    WatchDrive(d);
                    var tree = MeasureTree(new DirectoryInfo(d), null, d, _cts.Token, waitWhilePaused: true);
                    lock (_gate) { _roots[d] = tree; _dirty.RemoveWhere(p => p.StartsWith(d, StringComparison.OrdinalIgnoreCase)); }
                    Changed?.Invoke();
                }
                lock (_gate)
                {
                    foreach (var gone in _roots.Keys.Where(k => !drives.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList()) _roots.Remove(gone);
                }
                _expectedDirs = Math.Max(1, Interlocked.Read(ref _scannedDirs));
                _lastFullScan = DateTime.UtcNow;
                ScannedAt = DateTime.UtcNow;
                Ready = true;
                _changedSinceSave = true;
            }
            finally { Thread.CurrentThread.Priority = prio; }
            SaveSnapshot();
            Changed?.Invoke();
        }
        catch (Exception) { /* the next tick tries again */ }
        finally { Interlocked.Exchange(ref _scanning, 0); }
    }

    /// <summary>A mapped place as a key: "C:\" for a disk, "C:\Some\Folder\" for a folder (tests map a temp folder).</summary>
    private static string Root(string d) => Path.GetFullPath(d).TrimEnd('\\') + "\\";

    /// <summary>The mapped place a path lies in (the longest match), or null.</summary>
    private string? RootOf(string path)
    {
        var p = path.TrimEnd('\\') + "\\";
        return _roots.Keys.Where(k => p.StartsWith(k, StringComparison.OrdinalIgnoreCase)).MaxBy(k => k.Length);
    }

    /// <summary>Measures a folder and everything below it; keeps folders and files big enough to remember.</summary>
    private Node MeasureTree(DirectoryInfo dir, Node? parent, string name, CancellationToken ct, bool waitWhilePaused)
    {
        var node = new Node(name, parent, true) { Modified = SafeTime(dir) };
        Interlocked.Increment(ref _scannedDirs);
        if (waitWhilePaused) while (_pause() && !ct.IsCancellationRequested) Thread.Sleep(2000);
        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
            {
                if (ct.IsCancellationRequested) break;
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (entry is DirectoryInfo sub)
                {
                    var child = MeasureTree(sub, node, sub.Name, ct, waitWhilePaused);
                    node.Bytes += child.Bytes;
                    node.Files += child.Files;
                    if (child.Bytes >= KeepDirBytes) (node.Children ??= new(StringComparer.OrdinalIgnoreCase))[sub.Name] = child;
                }
                else if (entry is FileInfo file)
                {
                    long size = DiskUsage.OnDisk(file);
                    node.Bytes += size;
                    node.Files++;
                    if (size >= KeepFileBytes)
                        (node.Children ??= new(StringComparer.OrdinalIgnoreCase))[file.Name] = new Node(file.Name, node, false) { Bytes = size, Files = 1, Modified = SafeTime(file) };
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return node;
    }

    private static DateTime SafeTime(FileSystemInfo i)
    {
        try { return i.LastWriteTimeUtc; } catch (Exception) { return DateTime.MinValue; }
    }

    // ---- staying live -----------------------------------------------------------------------------------------------

    private void WatchDrive(string root)
    {
        lock (_gate)
        {
            if (_watchers.ContainsKey(root)) return;
            try
            {
                var w = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024
                };
                FileSystemEventHandler onChange = (_, e) => MarkDirty(e.FullPath);
                w.Created += onChange;
                w.Deleted += onChange;
                w.Changed += onChange;
                w.Renamed += (_, e) => { MarkDirty(e.OldFullPath); MarkDirty(e.FullPath); };
                // Too many changes at once (a big copy): Windows dropped some, so the drive is read again later.
                w.Error += (_, _) => _lastFullScan = DateTime.UtcNow - FullRescanEvery + TimeSpan.FromMinutes(10);
                w.EnableRaisingEvents = true;
                _watchers[root] = w;
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Notes that something below this path changed; its folder is measured again once things settle.</summary>
    public void MarkDirty(string path)
    {
        var dir = Path.GetDirectoryName(path.TrimEnd('\\'));
        if (string.IsNullOrEmpty(dir)) dir = path;
        lock (_gate)
        {
            var node = Nearest(dir, out _);
            if (node != null) _dirty.Add(node.FullPath);
            _lastEvent = DateTime.UtcNow;
        }
    }

    /// <summary>The deepest remembered folder that contains <paramref name="path"/>; exact is true when it is that folder.</summary>
    private Node? Nearest(string path, out bool exact)
    {
        exact = false;
        var root = RootOf(path);
        if (root == null || !_roots.TryGetValue(root, out var node)) return null;
        var rest = path.Length > root.Length ? path[root.Length..] : "";
        foreach (var part in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node.Children == null || !node.Children.TryGetValue(part, out var next) || !next.IsDir) return node;
            node = next;
        }
        exact = true;
        return node;
    }

    private void MeasureDirty()
    {
        List<string> paths;
        lock (_gate)
        {
            paths = _dirty.ToList();
            _dirty.Clear();
        }
        // An outer folder that is measured again also covers changes inside it.
        foreach (var p in paths.OrderBy(p => p.Length))
        {
            if (_cts.IsCancellationRequested) return;
            Remeasure(p);
        }
        _changedSinceSave = true;
        Changed?.Invoke();
    }

    /// <summary>Measures one folder again: its own files fresh, remembered sub-folders as they are, the others fresh.</summary>
    internal void Remeasure(string path)
    {
        Node? node;
        lock (_gate)
        {
            node = Nearest(path, out bool exact);
            if (node == null) return;
            if (!exact) path = node.FullPath;
        }
        var dir = new DirectoryInfo(path);
        if (!dir.Exists)
        {
            lock (_gate)
            {
                if (node.Parent?.Children != null)
                {
                    node.Parent.Children.Remove(node.Name);
                    Propagate(node.Parent, -node.Bytes, -node.Files);
                }
            }
            return;
        }
        long bytes = 0, files = 0;
        var keep = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Node>? known;
        lock (_gate) known = node.Children == null ? null : new(node.Children, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (entry is DirectoryInfo sub)
                {
                    Node child = known != null && known.TryGetValue(sub.Name, out var k) && k.IsDir ? k : MeasureTree(sub, node, sub.Name, _cts.Token, waitWhilePaused: false);
                    bytes += child.Bytes;
                    files += child.Files;
                    if (child.Bytes >= KeepDirBytes) keep[sub.Name] = child;
                }
                else if (entry is FileInfo file)
                {
                    long size = DiskUsage.OnDisk(file);
                    bytes += size;
                    files++;
                    if (size >= KeepFileBytes) keep[file.Name] = new Node(file.Name, node, false) { Bytes = size, Files = 1, Modified = SafeTime(file) };
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return; }
        lock (_gate)
        {
            long db = bytes - node.Bytes, df = files - node.Files;
            node.Children = keep.Count > 0 ? keep : null;
            foreach (var c in keep.Values) c.Parent = node;
            node.Modified = SafeTime(dir);
            node.Bytes = bytes;
            node.Files = files;
            if (node.Parent != null) Propagate(node.Parent, db, df);
            // A folder that shrank below the limit is no longer remembered on its own.
            if (node.Parent?.Children != null && node.Bytes < KeepDirBytes) node.Parent.Children.Remove(node.Name);
        }
    }

    private static void Propagate(Node? n, long bytes, long files)
    {
        for (; n != null; n = n.Parent)
        {
            n.Bytes += bytes;
            n.Files += files;
        }
    }

    // ---- reading ------------------------------------------------------------------------------------------------------

    /// <summary>Size and file count of a folder or file, from the map when it is remembered (else null).</summary>
    public (long Bytes, long Files, DateTime Modified)? SizeOf(string path)
    {
        lock (_gate)
        {
            var full = Path.GetFullPath(path).TrimEnd('\\');
            if (full.Length == 2) full += "\\";
            var dir = Nearest(full, out bool exact);
            if (dir != null && exact) return (dir.Bytes, dir.Files, dir.Modified);
            if (dir?.Children != null && dir.Children.TryGetValue(Path.GetFileName(full), out var f) && f.FullPath.Equals(full, StringComparison.OrdinalIgnoreCase))
                return (f.Bytes, f.Files, f.Modified);
            return null;
        }
    }

    /// <summary>
    /// What is inside a folder, biggest first: sub-folders with their sizes (from the map, or measured now when they
    /// are small), and the files themselves. Read live, so a file deleted a moment ago is gone.
    /// </summary>
    public IReadOnlyList<(string Name, string Path, bool IsDir, long Bytes, long Files, DateTime Modified)> List(string folder, CancellationToken ct = default)
    {
        var dir = new DirectoryInfo(folder);
        var list = new List<(string, string, bool, long, long, DateTime)>();
        Dictionary<string, Node>? known = null;
        lock (_gate)
        {
            var n = Nearest(dir.FullName, out bool exact);
            if (n != null && exact && n.Children != null) known = new(n.Children, StringComparer.OrdinalIgnoreCase);
        }
        foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
        {
            if (ct.IsCancellationRequested) break;
            bool link = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
            if (entry is DirectoryInfo sub)
            {
                if (link) { list.Add((sub.Name, sub.FullName, true, 0, 0, SafeTime(sub))); continue; }
                var k = known != null && known.TryGetValue(sub.Name, out var kn) && kn.IsDir ? kn : MeasureTree(sub, null, sub.Name, ct, waitWhilePaused: false);
                list.Add((sub.Name, sub.FullName, true, k.Bytes, k.Files, k.Modified));
            }
            else if (entry is FileInfo file)
            {
                list.Add((file.Name, file.FullName, false, link ? 0 : DiskUsage.OnDisk(file), 1, SafeTime(file)));
            }
        }
        return list.OrderByDescending(i => i.Item4).ThenBy(i => i.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The biggest remembered files (at least <see cref="KeepFileBytes"/>), biggest first.</summary>
    public IReadOnlyList<(string Path, long Bytes, DateTime Modified)> BigFiles(int max = 200)
    {
        var all = new List<(string, long, DateTime)>();
        lock (_gate)
        {
            var stack = new Stack<Node>(_roots.Values);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n.Children == null) continue;
                foreach (var c in n.Children.Values)
                {
                    if (c.IsDir) stack.Push(c);
                    else all.Add((c.FullPath, c.Bytes, c.Modified));
                }
            }
        }
        return all.OrderByDescending(f => f.Item2).Take(max).ToList();
    }

    /// <summary>Total bytes the map has for a disk (what could be read without admin rights).</summary>
    public long? Total(string root)
    {
        lock (_gate) return _roots.TryGetValue(Root(root), out var n) ? n.Bytes : null;
    }

    // ---- saved map ------------------------------------------------------------------------------------------------------

    private sealed record Saved(DateTime At, long Dirs, List<SavedNode> Roots);
    private sealed record SavedNode(string N, long B, long F, long M, bool D, List<SavedNode>? C);

    /// <summary>The map down to this many levels is saved; that is what a restart shows until the new scan is done.</summary>
    internal const int SavedDepth = 6;

    internal void SaveSnapshot()
    {
        try
        {
            Saved s;
            lock (_gate)
            {
                if (_roots.Count == 0 || ScannedAt == null) return;
                s = new Saved(ScannedAt.Value, _expectedDirs, _roots.Values.Select(r => Save(r, 0)).ToList());
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_snapshotFile)!);
            var tmp = _snapshotFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s));
            File.Move(tmp, _snapshotFile, overwrite: true);
            _lastSave = DateTime.UtcNow;
            _changedSinceSave = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static SavedNode Save(Node n, int depth) =>
        new(n.Name, n.Bytes, n.Files, n.Modified.Ticks, n.IsDir,
            depth >= SavedDepth || n.Children == null ? null : n.Children.Values.Select(c => Save(c, depth + 1)).ToList());

    private void LoadSnapshot()
    {
        try
        {
            if (!File.Exists(_snapshotFile)) return;
            var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(_snapshotFile));
            if (s == null) return;
            lock (_gate)
            {
                foreach (var r in s.Roots) if (!_roots.ContainsKey(r.N)) _roots[r.N] = Load(r, null);
            }
            ScannedAt = s.At;
            _expectedDirs = Math.Max(1, s.Dirs);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }

    private static Node Load(SavedNode s, Node? parent)
    {
        var n = new Node(s.N, parent, s.D) { Bytes = s.B, Files = s.F, Modified = new DateTime(Math.Max(0, s.M), DateTimeKind.Utc) };
        if (s.C != null) n.Children = s.C.ToDictionary(c => c.N, c => Load(c, n), StringComparer.OrdinalIgnoreCase);
        return n;
    }
}
