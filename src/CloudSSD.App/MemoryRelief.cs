namespace CloudSSD.App;

/// <summary>Outcome of one auto-mode run: how many apps were moved out of RAM and free RAM before and after.</summary>
public sealed record ReliefResult(DateTime At, int Apps, long AvailableBefore, long AvailableAfter, long TotalBytes)
{
    public long FreedBytes => Math.Max(0, AvailableAfter - AvailableBefore);

    /// <summary>True when the run brought free RAM back over the auto-mode threshold.</summary>
    public bool ReachedTarget => TotalBytes > 0 && (double)AvailableAfter / TotalBytes >= MemoryRelief.FreeFraction;
}

/// <summary>
/// Auto mode for low RAM. When less than <see cref="FreeFraction"/> of RAM is free it moves the memory of idle
/// background apps out of RAM, which makes room for the app in front. Nothing is closed and nothing is lost; a
/// moved app is a little slow the next time it is used, while Windows reads its memory back.
/// </summary>
public sealed class MemoryRelief
{
    /// <summary>Auto mode acts when less than this share of RAM is free.</summary>
    public const double FreeFraction = MemoryHealth.RamLowFreeFraction;
    /// <summary>
    /// Not again sooner than this: moving apps too often makes them page in and out all the time. While RAM stays
    /// low it runs again after every such wait, because idle apps take their memory back within minutes.
    /// </summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
    /// <summary>An app that was in front this recently is left alone.</summary>
    public static readonly TimeSpan RecentUse = TimeSpan.FromMinutes(15);
    /// <summary>
    /// After the app starts it first watches this long, to learn which app is in use. Kept short: with a longer
    /// wait a game started soon after the app got no room at all until the wait was over.
    /// </summary>
    public static readonly TimeSpan WarmUp = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, DateTime> _lastInFront = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly DateTime _started = DateTime.UtcNow;
    private DateTime _lastRun = DateTime.MinValue;
    private int _running;

    public ReliefResult? Last { get; private set; }

    /// <summary>Time until auto mode may act (again); zero when it may act now.</summary>
    public TimeSpan Wait
    {
        get
        {
            var now = DateTime.UtcNow;
            var ready = _lastRun == DateTime.MinValue ? _started + WarmUp : _lastRun + Cooldown;
            return ready > now ? ready - now : TimeSpan.Zero;
        }
    }

    public static bool IsLow(long totalBytes, long availableBytes) => totalBytes > 0 && (double)availableBytes / totalBytes < FreeFraction;

    /// <summary>
    /// Due when RAM is low, the last run is long enough ago, and the app has watched long enough since
    /// <paramref name="started"/> to know which apps are really idle.
    /// </summary>
    public static bool IsDue(long totalBytes, long availableBytes, DateTime now, DateTime started, DateTime lastRun, TimeSpan cooldown) =>
        IsLow(totalBytes, availableBytes) && now - started >= WarmUp && now - lastRun >= cooldown;

    /// <summary>
    /// Call about once a second: remembers which app is in front and says whether auto mode should act now.
    /// <paramref name="urgent"/> (a game has just started) skips the waiting, because that is when room matters most.
    /// </summary>
    public bool Tick(long totalBytes, long availableBytes, bool urgent = false)
    {
        if (MemoryHealth.ForegroundApp() is { } front)
            lock (_gate) _lastInFront[front] = DateTime.UtcNow;
        if (Volatile.Read(ref _running) != 0) return false;
        return urgent ? IsLow(totalBytes, availableBytes) : IsDue(totalBytes, availableBytes, DateTime.UtcNow, _started, _lastRun, Cooldown);
    }

    /// <summary>
    /// Moves idle background apps out of RAM now. Apps in <paramref name="keep"/> (the running game) and apps used
    /// in the last few minutes are left alone. Takes about ten seconds; null when a run is already under way.
    /// With <paramref name="alsoRecent"/> (a game has started) recently used apps are moved too: the user has
    /// just left them for the game, and the game needs the room now.
    /// </summary>
    public ReliefResult? Run(IEnumerable<string>? keep = null, bool alsoRecent = false)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return null;
        try
        {
            _lastRun = DateTime.UtcNow;
            var skip = new HashSet<string>(keep ?? [], StringComparer.OrdinalIgnoreCase);
            if (!alsoRecent)
                lock (_gate)
                    foreach (var (name, at) in _lastInFront)
                        if (DateTime.UtcNow - at < RecentUse) skip.Add(name);
            var before = MemoryHealth.ReadRam().AvailableBytes;
            var apps = MemoryHealth.TrimBackgroundApps(skip);
            Thread.Sleep(10000); // Windows takes about this long to hand the moved pages back as free RAM
            var (total, after) = MemoryHealth.ReadRam();
            return Last = new ReliefResult(DateTime.Now, apps, before, after, total);
        }
        finally { Volatile.Write(ref _running, 0); }
    }
}
