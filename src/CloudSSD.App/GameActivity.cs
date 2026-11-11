using System.Runtime.InteropServices;
using CloudSSD.Games;

namespace CloudSSD.App;

/// <summary>
/// Tells whether the user is playing right now, so background work that takes internet or disk (moving files to
/// the cloud, cleaning up) can wait until the game is over.
/// </summary>
public static class GameActivity
{
    /// <summary>Android emulators: a game inside one is not in the Game Library and need not be full screen.</summary>
    private static readonly HashSet<string> Emulators = new(StringComparer.OrdinalIgnoreCase)
    {
        "HD-Player", "dnplayer", "MuMuPlayer", "MuMuNxDevice", "Nox", "MEmu", "AndroidEmulatorEn", "AndroidEmulatorEx",
    };

    /// <summary>Windows' own full-screen surfaces (desktop, lock screen, Start); these are not games.</summary>
    private static readonly HashSet<string> Shell = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "LockApp", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "LogonUI",
    };

    /// <summary>A Game Library game is running, an emulator is in front, or the app in front fills the whole screen.</summary>
    public static bool IsPlaying(AppHost host) =>
        host.CurrentLaunch is { State: LaunchState.Running } || IsGameInFront(MemoryHealth.ForegroundApp(), ForegroundFillsScreen());

    /// <summary>
    /// The strict answer, for steps the user would mind if they were wrong (closing apps): a Game Library game is
    /// running or an emulator is in front. A full-screen window alone is not enough here: it may be a video, a
    /// presentation or a terminal.
    /// </summary>
    public static bool IsGaming(AppHost host) =>
        host.CurrentLaunch is { State: LaunchState.Running } || IsEmulator(MemoryHealth.ForegroundApp());

    internal static bool IsEmulator(string? app) => app is not null && Emulators.Contains(app);

    internal static bool IsGameInFront(string? foregroundApp, bool fillsScreen) =>
        foregroundApp is not null && (Emulators.Contains(foregroundApp) || (fillsScreen && !Shell.Contains(foregroundApp)));

    /// <summary>True for a full-screen window. A maximized window does not count: it leaves the taskbar visible.</summary>
    private static bool ForegroundFillsScreen()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !GetWindowRect(window, out var w)) return false;
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info)) return false;
        var m = info.Monitor;
        return w.Left <= m.Left && w.Top <= m.Top && w.Right >= m.Right && w.Bottom >= m.Bottom;
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
}
