using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CloudSSD.App;

/// <summary>One app game mode asked to close, and how to open it again: a Store app by its app id, any other by its program file.</summary>
public sealed record ClosedApp(string Name, string? Path, string? AppId);

/// <summary>What game mode did when the last game started: the apps it asked to close and free RAM before and after.</summary>
public sealed record GameModeResult(DateTime At, IReadOnlyList<string> Closed, long AvailableBefore, long AvailableAfter)
{
    public long FreedBytes => Math.Max(0, AvailableAfter - AvailableBefore);
}

/// <summary>
/// Game mode. When a game starts it makes room at once: the apps the user chose are asked to close and everything
/// else idle is moved out of RAM. When the game is over the closed apps can be opened again. Nothing is ever
/// killed: an app is closed the normal way, so one with unsaved work stays open and asks the user.
/// </summary>
public sealed class GameMode
{
    /// <summary>
    /// The game counts as over only after this long without it in front. Switching to another window for a moment
    /// must not open every closed app again.
    /// </summary>
    public static readonly TimeSpan EndDelay = TimeSpan.FromMinutes(2);

    public enum Step { None, Started, Ended }

    private DateTime _lastPlaying;

    /// <summary>True from the moment a game starts until it has been over for <see cref="EndDelay"/>.</summary>
    public bool Active { get; private set; }

    /// <summary>What was done for the last game; null until the first one.</summary>
    public GameModeResult? Last { get; set; }

    /// <summary>The apps closed for the running game, kept so they can be opened again.</summary>
    public IReadOnlyList<ClosedApp> Closed { get; private set; } = [];

    /// <summary>Call about once a second with whether a game is being played. Says when a game has just started or is over.</summary>
    public Step Tick(bool playing, DateTime nowUtc)
    {
        if (playing)
        {
            _lastPlaying = nowUtc;
            if (Active) return Step.None;
            Active = true;
            return Step.Started;
        }
        if (!Active || nowUtc - _lastPlaying < EndDelay) return Step.None;
        Active = false;
        return Step.Ended;
    }

    /// <summary>
    /// Asks each of <paramref name="apps"/> that has a window to close, except those in <paramref name="keep"/>
    /// (the game and the app in front). Returns the ones that were asked.
    /// </summary>
    public IReadOnlyList<ClosedApp> CloseApps(IEnumerable<string> apps, IEnumerable<string> keep)
    {
        var skip = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
        var closed = new List<ClosedApp>();
        foreach (var name in apps.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (skip.Contains(name) || MemoryHealth.WindowProcesses(name) is not [var owner, ..]) continue;
            var (path, appId) = LaunchInfo(owner);
            if (MemoryHealth.RequestClose(name) > 0) closed.Add(new ClosedApp(name, path, appId));
        }
        return Closed = closed;
    }

    /// <summary>
    /// Opens the apps closed for the game again. One that has a window (it never closed, or the user opened it
    /// already) is left as it is. Returns the names of the ones started.
    /// </summary>
    public IReadOnlyList<string> Reopen()
    {
        var opened = new List<string>();
        foreach (var app in Closed)
        {
            if (MemoryHealth.WindowProcesses(app.Name).Count > 0) continue;
            try
            {
                if (app.AppId is { } id) Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{id}") { UseShellExecute = true })?.Dispose();
                else if (app.Path is { } path && File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                else continue;
                opened.Add(app.Name);
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException) { }
        }
        Closed = [];
        return opened;
    }

    /// <summary>Program file and, for a Store app, the app id of a running process; nulls when Windows does not tell.</summary>
    internal static (string? Path, string? AppId) LaunchInfo(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == IntPtr.Zero) return (null, null);
        try
        {
            var text = new StringBuilder(1024);
            uint size = (uint)text.Capacity;
            string? path = QueryFullProcessImageNameW(handle, 0, text, ref size) ? text.ToString(0, (int)size) : null;
            text.Clear();
            size = (uint)text.Capacity;
            string? appId = GetApplicationUserModelId(handle, ref size, text) == 0 && text.Length > 0 ? text.ToString() : null;
            return (path, appId);
        }
        finally { CloseHandle(handle); }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref uint size);

    /// <summary>0 for a packaged (Store) app; an error code for an ordinary program, which has no app id.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder appId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
