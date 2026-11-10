using System.Collections.Concurrent;
using CloudPlayX.Cache;
using CloudPlayX.Core;
using CloudPlayX.Diagnostics;
using CloudPlayX.Games;
using CloudPlayX.Mount;
using CloudPlayX.Streaming;
using CloudPlayX.Streaming.GameAware;
using Microsoft.Extensions.Logging;

namespace CloudPlayX.App;

/// <summary>
/// The CLOUDPLAY X application service: owns settings, rclone, mounts, the prediction engine, the game library and
/// metrics, and wires real telemetry from rclone into the engine. UI and CLI are thin layers over this class.
/// </summary>
public sealed class AppHost : IAsyncDisposable
{
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, string> _remoteTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (NamedPipeTelemetryServer Server, GameAwareController Controller)> _gameAware = new();
    private volatile bool _diskPressure;
    private Timer? _diskTimer;
    private FileStream? _instanceLock;

    private AppHost(AppPaths paths)
    {
        Paths = paths;
        SettingsStore = new JsonSettingsStore(paths.SettingsFile);
        Settings = SettingsStore.Load();
        LogSink = new MemoryLogSink();
        LogProvider = new AppLoggerProvider(paths.LogDirectory, LogSink);
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(LogProvider));
        _log = LoggerFactory.CreateLogger<AppHost>();
        RcloneConfig = new RcloneConfigService(paths, LoggerFactory.CreateLogger<RcloneConfigService>());
        Drive = new GoogleDriveConnector(RcloneConfig, LoggerFactory.CreateLogger<GoogleDriveConnector>());
        OneDrive = new OneDriveConnector(RcloneConfig, LoggerFactory.CreateLogger<OneDriveConnector>());
        WebDav = new WebDavConnector(RcloneConfig, LoggerFactory.CreateLogger<WebDavConnector>());
        Mounts = new RcloneMountService(paths, RcloneConfig, LoggerFactory.CreateLogger<RcloneMountService>());
        Profiles = new ProfileStore(paths.DatabaseFile);
        Library = new GameLibrary(paths.DatabaseFile);
        SaveBackups = new SaveBackupService(paths.DataRoot, LoggerFactory.CreateLogger<SaveBackupService>());
        CacheIndex = new CacheIndex();
        MobileBridge = new LocalMobileBridgeService(this, LoggerFactory.CreateLogger<LocalMobileBridgeService>());
        Offloader = new SmartStorageOffloaderService(this, LoggerFactory.CreateLogger<SmartStorageOffloaderService>());
        Engine = new StreamingEngine(Settings.Streaming, Settings.Network, Profiles,
            new MountReadExecutor(id => Mounts.Get(id) is { State: MountState.Mounted } s ? s.Root : null, id => Mounts.Get(id)?.Attribution),
            FileSize, _ => !_diskPressure, CacheIndex.IsCached, LoggerFactory.CreateLogger<StreamingEngine>());
        Launcher = new GameLauncher(paths.DiagnosticsDirectory);
        Preparer = new GamePreparer(Engine, Profiles, RcloneConfig, CacheIndex);
        Installer = new FullLocalInstaller(RcloneConfig);
        Metrics = new MetricsCollector(this);
        Pins = new PinKeeper(PinnedTargets);

        Archives = new ArchiveWarmer(
            id => Mounts.Get(id) is { State: MountState.Mounted } s ? s.Root : null, id => Mounts.Get(id)?.Attribution,
            // Never while a game runs (its reads come first) or while the cache disk is short of space.
            () => _diskPressure || CurrentLaunch is { State: LaunchState.Running }, LoggerFactory.CreateLogger<ArchiveWarmer>());
        Mounts.DirectoryListed += Archives.OnDirectoryListed;
        Mounts.ReadObserved += Engine.OnAccess;
        Mounts.LookupObserved += (mount, path, off, present, pre) => Engine.OnLookup(mount, path, off, present, pre);
        Mounts.StatusChanged += s =>
        {
            _log.LogInformation("Mount {Id}: {State} {Message}", s.MountId, s.State, s.Message);
            if (s.State is MountState.Stopped or MountState.Failed)
            {
                CacheIndex.Unregister(s.MountId);
                // rclone may have evicted or lost data while the mount was down; forget what we believed was cached.
                Engine.Queue.ForgetCached(k => k.Mount == s.MountId);
            }
            StateChanged?.Invoke();
        };
        Engine.ApplicationBlockRead += (session, block) =>
        {
            if (_gameAware.TryGetValue(session.GameId, out var ga)) ga.Controller.OnApplicationRead(block);
        };
    }

    public AppPaths Paths { get; }
    public ISettingsStore SettingsStore { get; }
    public AppSettings Settings { get; }
    public MemoryLogSink LogSink { get; }
    public AppLoggerProvider LogProvider { get; }
    public ILoggerFactory LoggerFactory { get; }
    public RcloneInfo? Rclone { get; private set; }
    public RcloneConfigService RcloneConfig { get; }
    public GoogleDriveConnector Drive { get; }
    public OneDriveConnector OneDrive { get; }
    public WebDavConnector WebDav { get; }
    public SaveBackupService SaveBackups { get; }
    public RcloneMountService Mounts { get; }
    public ProfileStore Profiles { get; }
    public GameLibrary Library { get; }
    public CacheIndex CacheIndex { get; }
    public StreamingEngine Engine { get; }
    public GameLauncher Launcher { get; }
    public GamePreparer Preparer { get; }
    public FullLocalInstaller Installer { get; }
    public MetricsCollector Metrics { get; }
    public PinKeeper Pins { get; }
    public ArchiveWarmer Archives { get; }
    public LocalMobileBridgeService MobileBridge { get; }
    public SmartStorageOffloaderService Offloader { get; }
    public IReadOnlyList<DependencyStatus> Dependencies { get; private set; } = [];
    public IReadOnlyList<RemoteEntry> Remotes { get; private set; } = [];
    /// <summary>Google Drive accounts still signed in through rclone's shared OAuth client, which rclone retires during 2026.</summary>
    public IReadOnlyList<string> SharedClientRemotes { get; private set; } = [];
    /// <summary>Combined drives (rclone unions): union remote name → the account remotes it joins.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Unions { get; private set; } = new Dictionary<string, IReadOnlyList<string>>();
    /// <summary>Live identity (Gmail address, name) and storage of each connected account; filled by <see cref="RefreshAccountsAsync"/>.</summary>
    public IReadOnlyDictionary<string, CloudAccountInfo> Accounts { get; private set; } = new Dictionary<string, CloudAccountInfo>();
    public DateTime AccountsRefreshedAt { get; private set; }
    private readonly SemaphoreSlim _accountsGate = new(1, 1);
    public LaunchSession? CurrentLaunch { get; private set; }
    public bool DiskPressure => _diskPressure;
    public string CpxPath => Path.Combine(AppPaths.AppDirectory, "cpx.exe");

    /// <summary>Cumulative prefetch accounting over finished sessions (plus live sessions in <see cref="StreamingEngine.PrefetchTotals"/>).</summary>
    public long FinishedUsefulBytes, FinishedWastedBytes;
    public SessionSummary? LastSession { get; private set; }

    public event Action? StateChanged;

    public static AppHost Create(AppPaths? paths = null) => new(paths ?? new AppPaths());

    /// <summary>
    /// Prevents two CLOUDPLAY X instances from owning the same data/cache directories (concurrent cache ownership
    /// would corrupt rclone's cache). Returns false if another instance holds the lock.
    /// </summary>
    public bool TryAcquireInstanceLock()
    {
        try
        {
            _instanceLock = new FileStream(Path.Combine(Paths.DataRoot, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
    }

    public async Task InitializeAsync(bool restoreMounts = true, CancellationToken ct = default)
    {
        _log.LogInformation("Cloud-SSD starting (data root {Root})", Paths.DataRoot);
        if (Settings.MobileBridge.Enabled) MobileBridge.Start();
        Rclone = await RcloneLocator.LocateAsync(Settings.RclonePathOverride, Paths.DataRoot, ct);
        if (Rclone != null)
        {
            Mounts.SetRclone(Rclone);
            _log.LogInformation("Using {Version} at {Path}", Rclone.Version, Rclone.Path);
        }
        else _log.LogError("rclone not found");
        RefreshDependencies();
        await RefreshRemotesAsync(ct);
        _diskTimer = new Timer(_ => CheckDisk(), null, TimeSpan.Zero, TimeSpan.FromSeconds(10));

        if (Rclone != null)
        {
            foreach (var s in await Mounts.AdoptExistingAsync(ct))
                CacheIndex.Register(s.Id, s.CacheDir, s.Definition.RemotePath);
            if (restoreMounts && Settings.RestoreMountsOnStartup)
            {
                foreach (var def in Settings.Mounts.Where(m => m.AutoMount))
                {
                    if (Mounts.Get(def.Id) != null) continue;
                    try { await MountAsync(def, ct); }
                    catch (Exception e) { _log.LogError("Could not restore mount {Name}: {Error}", def.DisplayName, e.Message); }
                }
            }
        }
        if (Settings.MobileBridge.Enabled) MobileBridge.Start();
        Metrics.Start();
        StateChanged?.Invoke();
        _ = RefreshAccountsAsync(CancellationToken.None);
    }

    public void RefreshDependencies()
    {
        Dependencies = DependencyChecker.CheckAll(Rclone?.Path, Rclone?.Version, Paths.CacheRootFor(Settings), Settings.Cache.MinFreeSpaceBytes);
    }

    public async Task RefreshRemotesAsync(CancellationToken ct = default)
    {
        if (Rclone is null) return;
        try
        {
            Remotes = await RcloneConfig.ListRemotesAsync(Rclone, ct);
            foreach (var r in Remotes) _remoteTypes[r.Name] = r.Type;
            var dump = await RcloneConfig.DumpAsync(Rclone, ct);
            SharedClientRemotes = RcloneConfigService.SharedClientDriveRemotes(dump);
            Unions = RcloneConfigService.Unions(dump).Concat(RcloneConfigService.Combines(dump))
                .ToDictionary(u => u.Name, u => RcloneConfigService.UpstreamRemotes(u.Upstreams));
            // Forget accounts that were disconnected; newly connected ones appear after RefreshAccountsAsync.
            Accounts = Accounts.Where(a => Remotes.Any(r => r.Name == a.Key)).ToDictionary(a => a.Key, a => a.Value);
        }
        catch (Exception e) { _log.LogWarning("Listing rclone remotes failed: {Error}", e.Message); }
    }

    /// <summary>
    /// Asks every connected account who it is (Gmail address) and how much storage it has. Takes a few seconds, so
    /// callers that only want fresh numbers start it without awaiting; <see cref="StateChanged"/> fires when it is done.
    /// </summary>
    public async Task RefreshAccountsAsync(CancellationToken ct = default, TimeSpan? maxAge = null)
    {
        if (Rclone is null) return;
        await _accountsGate.WaitAsync(ct);
        try
        {
            var remotes = Remotes;
            // A refresh that just finished (e.g. the one started at startup) is good enough for a caller that allows it.
            if (maxAge is { } age && DateTime.UtcNow - AccountsRefreshedAt < age && remotes.All(r => Accounts.ContainsKey(r.Name))) return;
            var infos = await Drive.AccountInfoAsync(Rclone, remotes, ct);
            Accounts = infos.ToDictionary(i => i.Remote);
            AccountsRefreshedAt = DateTime.UtcNow;
            // Remember the addresses, so drives and folders keep their names when the PC starts offline.
            var known = infos.Where(i => i.Email != null).ToDictionary(i => i.Remote, i => i.Email!);
            if (known.Any(k => Settings.AccountEmails.GetValueOrDefault(k.Key) != k.Value))
            {
                foreach (var (remote, email) in known) Settings.AccountEmails[remote] = email;
                SaveSettings();
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) { _log.LogWarning("Reading account details failed: {Error}", e.Message); }
        finally { _accountsGate.Release(); }
        StateChanged?.Invoke();
    }

    /// <summary>The account remotes behind a remote: itself, or for a combined drive every account it joins.</summary>
    public IReadOnlyList<string> AccountsBehind(string remote) => Unions.TryGetValue(remote, out var ups) ? ups : [remote];

    /// <summary>The sign-in address of an account (live, else last known), or its internal name when never seen.</summary>
    public string AccountName(string remote) =>
        Accounts.GetValueOrDefault(remote)?.Email ?? Settings.AccountEmails.GetValueOrDefault(remote) ?? remote;

    /// <summary>What to call a remote on screen: its Gmail address when known, for a combined drive all of them.</summary>
    public string AccountLabel(string remote) => string.Join(" + ", AccountsBehind(remote).Select(AccountName));

    /// <summary>True for the union/combine remotes Cloud-SSD generates behind its two standard drives (not real accounts).</summary>
    public static bool IsGeneratedType(string type) => type is "union" or "combine";

    public const string CombinedRemote = "CloudSSD", SingleRemote = "Accounts";
    public const string CombinedMountId = "cloudssd", SingleMountId = "accounts";

    /// <summary>
    /// Keeps exactly two cloud drives, whatever accounts are connected:
    /// "Each Account" — one folder per account, so every account can be seen on its own; and
    /// "All Accounts" — every account joined into one drive whose size is the sum of the connected accounts.
    /// Any other cloud drive (one letter per account, the same account mounted twice) is disconnected and removed,
    /// and the same Gmail signed in twice is kept once. Only local logins and drive entries change — never cloud files.
    /// </summary>
    public async Task SyncStandardDrivesAsync(CancellationToken ct = default)
    {
        if (Rclone is not { } rclone) return;
        if (CurrentLaunch is { State: LaunchState.Running } l)
            throw new InvalidOperationException($"{l.Game.Name} is running from a cloud drive. Close it, then try again.");

        await RefreshRemotesAsync(ct);
        // Entries without a type are leftovers of removed accounts; they must not show up as accounts.
        foreach (var broken in Remotes.Where(r => string.IsNullOrWhiteSpace(r.Type)).ToList())
            await RcloneConfig.DeleteRemoteAsync(rclone, broken.Name, ct);
        await RefreshRemotesAsync(ct);
        await RefreshAccountsAsync(ct, TimeSpan.FromSeconds(30));
        var dump = await RcloneConfig.DumpAsync(rclone, ct);
        var readOnly = RcloneConfigService.ReadOnlyDriveRemotes(dump);

        // Every real login once; of two sign-ins of the same Gmail the writable one stays.
        var logins = Remotes.Where(r => !IsGeneratedType(r.Type)).ToList();
        var accounts = logins.GroupBy(r => AccountName(r.Name), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(r => readOnly.Contains(r.Name)).First()).ToList();
        foreach (var twin in logins.Except(accounts))
        {
            _log.LogInformation("Removing second sign-in {Remote} of an account that is already connected", twin.Name);
            await RcloneConfig.DeleteRemoteAsync(rclone, twin.Name, ct);
        }

        // Drives other than the two standard ones go away. (":http…" style on-the-fly remotes are not accounts.)
        foreach (var m in Settings.Mounts.Where(m => m.Id is not (CombinedMountId or SingleMountId) && !m.RemoteName.StartsWith(':')).ToList())
        {
            if (Mounts.Get(m.Id) != null) await UnmountAsync(m.Id);
            Settings.Mounts.Remove(m);
        }
        foreach (var old in Remotes.Where(r => IsGeneratedType(r.Type) && r.Name is not (CombinedRemote or SingleRemote)).ToList())
            await RcloneConfig.DeleteRemoteAsync(rclone, old.Name, ct);

        if (accounts.Count == 0)
        {
            foreach (var m in Settings.Mounts.Where(m => m.Id is CombinedMountId or SingleMountId).ToList())
            {
                if (Mounts.Get(m.Id) != null) await UnmountAsync(m.Id);
                Settings.Mounts.Remove(m);
            }
            foreach (var name in new[] { CombinedRemote, SingleRemote }.Where(n => Remotes.Any(r => r.Name == n)))
                await RcloneConfig.DeleteRemoteAsync(rclone, name, ct);
            SaveSettings();
            await RefreshRemotesAsync(ct);
            StateChanged?.Invoke();
            return;
        }

        // Read-only sign-ins are shown in the joined drive but never chosen for new files (":ro").
        var unionUpstreams = string.Join(' ', accounts.Select(r => r.Name + (readOnly.Contains(r.Name) ? "::ro" : ":")));
        var combineUpstreams = string.Join(' ', accounts.Select(r => $"{FolderName(AccountName(r.Name))}={r.Name}:"));
        var unionChanged = RcloneConfigService.Unions(dump).FirstOrDefault(u => u.Name == CombinedRemote).Upstreams != unionUpstreams;
        var combineChanged = RcloneConfigService.Combines(dump).FirstOrDefault(u => u.Name == SingleRemote).Upstreams != combineUpstreams;
        if (unionChanged)
            await RcloneConfig.WriteRemoteAsync(rclone, CombinedRemote, "union",
                [("upstreams", unionUpstreams), ("action_policy", "epmfs"), ("create_policy", "mfs"), ("search_policy", "ff")], ct);
        if (combineChanged)
            await RcloneConfig.WriteRemoteAsync(rclone, SingleRemote, "combine", [("upstreams", combineUpstreams)], ct);
        await RefreshRemotesAsync(ct);

        var all = StandardDrive(CombinedMountId, "All Accounts", CombinedRemote, 'Z');
        all.ReadOnly = accounts.All(r => readOnly.Contains(r.Name));
        var each = StandardDrive(SingleMountId, "Each Account", SingleRemote, 'Y');
        if (Settings.Mounts.All(m => m.Id != Settings.SmartOffload.TargetMountId)) Settings.SmartOffload.TargetMountId = CombinedMountId;
        SaveSettings();

        foreach (var (def, changed) in new[] { (each, combineChanged), (all, unionChanged) })
        {
            try
            {
                // A running mount keeps the account list it started with, so it reconnects when that list changed.
                if (changed && Mounts.Get(def.Id) != null) await UnmountAsync(def.Id);
                if (Mounts.Get(def.Id) == null)
                {
                    var s = await MountAsync(def, ct);
                    def.DriveLetter = s.DriveLetter.ToString();
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogError("Could not connect drive {Name}: {Error}", def.DisplayName, e.Message);
            }
        }
        SaveSettings();
        StateChanged?.Invoke();
        _ = RefreshAccountsAsync(CancellationToken.None); // sizes of the two drives themselves
    }

    private MountDefinition StandardDrive(string id, string displayName, string remote, char preferredLetter)
    {
        var def = Settings.Mounts.FirstOrDefault(m => m.Id == id);
        if (def is null)
        {
            var free = !DriveInfo.GetDrives().Any(d => char.ToUpperInvariant(d.Name[0]) == preferredLetter);
            def = new MountDefinition { Id = id, AutoMount = true, DriveLetter = free ? preferredLetter.ToString() : null };
            Settings.Mounts.Add(def);
        }
        def.DisplayName = displayName;
        def.RemoteName = remote;
        def.RemotePath = "";
        return def;
    }

    /// <summary>An account name usable as a folder name inside the "Each Account" drive (no spaces or path characters).</summary>
    private static string FolderName(string account) =>
        new(account.Select(c => char.IsWhiteSpace(c) || c is '=' or ':' or '/' or '\\' or '"' or '*' or '?' or '<' or '>' or '|' ? '_' : c).ToArray());

    public void SaveSettings() => SettingsStore.Save(Settings);

    public string? RemoteTypeOf(string remoteName) =>
        remoteName.StartsWith(':') ? remoteName.TrimStart(':') : _remoteTypes.GetValueOrDefault(remoteName);

    /// <summary>Budget rclone may use for one mount, after protecting the free-space reserve.</summary>
    public CacheBudgetDecision BudgetFor(string mountId)
    {
        var root = Paths.CacheRootFor(Settings);
        Directory.CreateDirectory(root);
        var current = CacheInspector.Measure(Paths.MountCacheDir(Settings, mountId));
        var mounts = Math.Max(1, Settings.Mounts.Count(m => m.AutoMount || Mounts.Get(m.Id) != null));
        return DiskSafetyGuard.Decide(Settings.Cache.BudgetBytes, Settings.Cache.MinFreeSpaceBytes, current.VolumeFreeBytes, current.PhysicalBytes, mounts);
    }

    public async Task<MountSession> MountAsync(MountDefinition def, CancellationToken ct = default)
    {
        if (Rclone is null) throw new InvalidOperationException("rclone was not found. See Diagnostics.");
        var budget = BudgetFor(def.Id);
        foreach (var w in budget.Warnings) _log.LogWarning("{Warning}", w);
        var cacheDir = Paths.MountCacheDir(Settings, def.Id);
        var req = new MountRequest(def, Settings.Cache, Settings.Network, cacheDir, Math.Max(16 * ByteSize.MiB, budget.EffectiveMaxBytes),
            Settings.Streaming.ReadProfilingEnabled, RemoteTypeOf(def.RemoteName));
        var s = await Mounts.MountAsync(req, ct);
        CacheIndex.Register(def.Id, cacheDir, def.RemotePath);
        if (s.DriveLetter.ToString() != def.DriveLetter)
        {
            def.DriveLetter ??= s.DriveLetter.ToString();
        }
        StateChanged?.Invoke();
        return s;
    }

    public async Task UnmountAsync(string mountId)
    {
        await Mounts.UnmountAsync(mountId, TimeSpan.FromSeconds(60));
        CacheIndex.Unregister(mountId);
        StateChanged?.Invoke();
    }

    private long? FileSize(string mountId, string mountPath)
    {
        var s = Mounts.Get(mountId);
        if (s is null) return null;
        try
        {
            var fi = new FileInfo(Path.Combine(s.Root, mountPath.Replace('/', '\\')));
            return fi.Exists ? fi.Length : null;
        }
        catch (IOException) { return null; }
    }

    private IReadOnlyList<(MountSession, string)> PinnedTargets()
    {
        var list = new List<(MountSession, string)>();
        foreach (var p in Settings.Cache.PinnedPaths)
        {
            // Stored as "<mountId>:<path in mount>".
            var i = p.IndexOf(':');
            if (i <= 0) continue;
            if (Mounts.Get(p[..i]) is { } m) list.Add((m, p[(i + 1)..]));
        }
        return list;
    }

    private void CheckDisk()
    {
        try
        {
            var root = Paths.CacheRootFor(Settings);
            Directory.CreateDirectory(root);
            var (free, _) = DiskInfo.FreeSpace(root);
            var pressure = DiskSafetyGuard.Pressure(free, Settings.Cache.MinFreeSpaceBytes);
            var was = _diskPressure;
            _diskPressure = pressure != Cache.DiskPressure.Normal;
            if (_diskPressure && !was) _log.LogWarning("Disk space low ({Free} free, reserve {Reserve}); prefetching paused", ByteSize.Format(free), ByteSize.Format(Settings.Cache.MinFreeSpaceBytes));
            if (!_diskPressure && was) _log.LogInformation("Disk space recovered; prefetching resumed");

            // Smart Storage Saver check
            if (Settings.SmartOffload.Enabled)
            {
                _ = Offloader.CheckAndOffloadIfNeededAsync();
            }
        }
        catch (Exception) { }
    }

    // ---------------- games ----------------

    public async Task<GameEntry> AddGameAsync(string mountId, string rootInMount, string? name = null, CancellationToken ct = default)
    {
        var mount = Mounts.Get(mountId) ?? throw new InvalidOperationException("The drive is not mounted.");
        var root = Path.Combine(mount.Root, rootInMount.Replace('/', '\\'));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var scan = await Task.Run(() => GameScanner.Scan(root, ct: ct), ct);
        var g = new GameEntry
        {
            Name = name ?? Path.GetFileName(root.TrimEnd('\\')),
            MountId = mountId,
            RootPath = rootInMount.Replace('\\', '/').Trim('/'),
            Executable = scan.Executables.FirstOrDefault()?.RelativePath,
        };
        var summary = Profiles.Summary(g.Id, Settings.Streaming.StartupWindow);
        g.Compatibility = CompatibilityAnalyzer.Analyze(scan, g.Executable, Settings.Network.EffectiveMbps, null, summary.Sessions);
        if (g.Executable != null && PeInspector.TryRead(Path.Combine(root, g.Executable)) is { } pe)
        {
            g.Compatibility.Architecture = pe.Machine.ToString();
            CompatibilityAnalyzer.AddDependencyIssues(g.Compatibility, DependencyResolver.Check(pe, Path.GetDirectoryName(Path.Combine(root, g.Executable))!, root));
        }
        g.Mode = g.Compatibility.RecommendedMode == ExecutionMode.FullLocal ? ExecutionMode.CloudDirect : g.Compatibility.RecommendedMode;
        if (g.Mode == ExecutionMode.HybridOptimized && !DependencyChecker.CanCreateSymlinks()) g.Mode = ExecutionMode.CloudDirect;
        Library.Save(g);
        _log.LogInformation("Added game {Name} ({Files} files, {Size}); compatibility: {Level}", g.Name, scan.FileCount, ByteSize.Format(scan.TotalBytes), g.Compatibility.Level);
        return g;
    }

    public async Task<PrepareReport> PrepareAsync(GameEntry g, IProgress<IReadOnlyList<PrepareStep>>? steps, IProgress<PrepareProgress>? progress, CancellationToken ct)
    {
        if (Rclone is null) throw new InvalidOperationException("rclone was not found.");
        var mount = Mounts.Get(g.MountId);
        if (mount is null && Settings.Mounts.FirstOrDefault(m => m.Id == g.MountId) is { } def && g.Mode != ExecutionMode.FullLocal)
            mount = await MountAsync(def, ct);
        var report = await Preparer.PrepareAsync(g, mount, Settings, Rclone, steps, progress, ct);
        Library.Save(g);
        SaveSettings();
        _log.LogInformation("Prepare {Game}: {Status} - {Headline}", g.Name, report.Status, report.Headline);
        return report;
    }

    public async Task<LaunchSession> PlayAsync(GameEntry g, CancellationToken ct = default)
    {
        if (CurrentLaunch is { State: LaunchState.Running }) throw new InvalidOperationException($"{CurrentLaunch.Game.Name} is already running.");
        MountSession? mount = null;
        if (g.Mode != ExecutionMode.FullLocal)
        {
            mount = Mounts.Get(g.MountId);
            if (mount is not { State: MountState.Mounted })
            {
                var def = Settings.Mounts.FirstOrDefault(m => m.Id == g.MountId) ?? throw new InvalidOperationException("The game's cloud drive is not configured.");
                mount = await MountAsync(def, ct);
            }
        }
        GameSession? session = null;
        if (mount != null && Settings.Streaming.ReadProfilingEnabled)
        {
            session = Engine.BeginSession(g.Id, mount.Id, g.RootPath, g.Mode.ToString());
            if (g.GameAwareEnabled) StartGameAware(g, session);
        }
        LaunchSession launch;
        try
        {
            launch = Launcher.Launch(g, mount?.Root);
        }
        catch
        {
            if (session != null) Engine.EndSession(g.Id, save: false);
            StopGameAware(g.Id);
            throw;
        }
        CurrentLaunch = launch;
        g.LastPlayed = DateTime.Now;
        Library.Save(g);
        _log.LogInformation("Launched {Game} in {Mode} mode: {Exe}", g.Name, g.Mode, launch.Executable);
        StateChanged?.Invoke();
        _ = launch.Completion.ContinueWith(t => OnLaunchEnded(g, t.Result), TaskScheduler.Default);
        return launch;
    }

    private void OnLaunchEnded(GameEntry g, LaunchDiagnostics d)
    {
        StopGameAware(g.Id);
        var summary = Engine.EndSession(g.Id);
        if (summary != null)
        {
            LastSession = summary;
            Interlocked.Add(ref FinishedUsefulBytes, summary.UsefulPrefetchBytes);
            Interlocked.Add(ref FinishedWastedBytes, summary.WastedPrefetchBytes);
        }
        var fresh = Library.Get(g.Id) ?? g;
        var verified = !d.LikelyFailure && d.Runtime >= TimeSpan.FromSeconds(60);
        if (verified && !fresh.VerifiedModes.Contains(d.Mode)) fresh.VerifiedModes.Add(d.Mode);
        if (verified && fresh.Compatibility != null && fresh.Compatibility.Level < CompatibilityLevel.NeedsLocalInstall)
            fresh.Compatibility.Level = CompatibilityLevel.Verified;
        fresh.LastLaunchResult = d.LikelyFailure
            ? $"Failed {d.Started:g}: {string.Join(" ", d.Notes)}"
            : $"Ran {Bandwidth.FormatDuration(d.Runtime)} on {d.Started:g} ({d.Mode})" + (summary != null
                ? $"; reads hit {summary.Hits}/{summary.Hits + summary.Misses}, stalls {summary.StallMs / 1000:0.0} s"
                : "");
        Library.Save(fresh);
        if (fresh.CloudSaveSyncEnabled)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var mount = Mounts.Get(fresh.MountId);
                    var remote = mount?.Definition.RemoteName ?? Remotes.FirstOrDefault()?.Name;
                    var res = await SaveBackups.BackupAsync(fresh, Rclone, RcloneConfig, remote);
                    if (res.Success)
                    {
                        Library.Save(fresh);
                        _log.LogInformation("Automatic save backup completed for {Game}: {Msg}", fresh.Name, res.Message);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Automatic save backup failed for {Game}", fresh.Name);
                }
            });
        }
        _log.Log(d.LikelyFailure ? LogLevel.Warning : LogLevel.Information, "{Game} exited: {Result}", g.Name, fresh.LastLaunchResult);
        StateChanged?.Invoke();
    }

    private void StartGameAware(GameEntry g, GameSession session)
    {
        var server = new NamedPipeTelemetryServer(g.Id);
        var controller = new GameAwareController(new RegionAssetMap(), server,
            (p, tag) => Engine.ScheduleExternal(session, p, tag), prefix => Engine.CancelTagged(session, prefix));
        _gameAware[g.Id] = (server, controller);
        _log.LogInformation("Game-Aware telemetry listening on \\\\.\\pipe\\{Pipe}", server.PipeName);
    }

    private void StopGameAware(string gameId)
    {
        if (!_gameAware.TryRemove(gameId, out var ga)) return;
        ga.Controller.Dispose();
        _ = ga.Server.DisposeAsync();
    }

    public async Task ShutdownAsync(bool keepMounts)
    {
        Metrics.Dispose();
        if (keepMounts)
        {
            Mounts.DetachAll();
            _log.LogInformation("Exiting; drives stay mounted and will be re-attached on next start");
        }
        else
        {
            await Mounts.UnmountAllAsync(TimeSpan.FromSeconds(60));
            _log.LogInformation("Exiting; all drives unmounted");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _diskTimer?.Dispose();
        Archives.Dispose();
        Pins.Dispose();
        Metrics.Dispose();
        foreach (var id in _gameAware.Keys.ToList()) StopGameAware(id);
        await Engine.DisposeAsync();
        await MobileBridge.DisposeAsync();
        await Mounts.DisposeAsync();
        CacheIndex.Dispose();
        LoggerFactory.Dispose();
        _instanceLock?.Dispose();
    }
}
