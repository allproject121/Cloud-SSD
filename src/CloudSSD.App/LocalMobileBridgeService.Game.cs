using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Text;
using CloudSSD.GameStream;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>
/// "Play on phone" (/api/game/*, switch "Play on phone"): the phone starts the PC's game and then plays it, picture and
/// sound coming to the phone and its touches going to the game. <c>/api/game/stream</c> switches the connection to
/// <see cref="StreamProtocol"/> for as long as the phone plays. Only on the home network and only with the phone's
/// own key, because the phone's touches act on this PC.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    /// <summary>One phone plays at a time.</summary>
    private readonly SemaphoreSlim _gameStreamSlot = new(1, 1);

    private async Task<bool> TryHandleGameRouteAsync(string method, string path, NetworkStream stream, CancellationToken ct)
    {
        var p = path.ToLowerInvariant();
        if (!p.StartsWith("/api/game/")) return false;
        var caller = CurrentCaller.Value!;
        var settings = _host.Settings.MobileBridge.GameStream;
        if (caller.Device is null)
        {
            await SendJsonAsync(stream, 403, "Forbidden", new { error = "Pair this phone again to play: the old shared key cannot control games.", code = "device-key-needed" }, ct);
            return true;
        }
        if (caller.Route != NetRoute.Lan)
        {
            await SendJsonAsync(stream, 403, "Forbidden", new { error = "Play on phone works on the home Wi-Fi for now.", code = "home-only" }, ct);
            return true;
        }
        switch (p)
        {
            case "/api/game/status":
                await SendJsonAsync(stream, 200, "OK", new
                {
                    game = settings.GameName,
                    emulatorRunning = GameWindow.Find() is not null,
                    inFront = GameWindow.InFront(),
                    playing = _gameStreamSlot.CurrentCount == 0,
                }, ct);
                return true;
            case "/api/game/start" when method == "POST":
                var started = await Task.Run(() => GameWindow.Start(settings.LaunchPath, settings.LaunchArguments, TimeSpan.FromSeconds(60)), ct);
                await SendJsonAsync(stream, started ? 200 : 500, started ? "OK" : "Error",
                    started ? new { ok = true } : new { ok = false, error = $"{settings.GameName} did not open on the PC." }, ct);
                return true;
            case "/api/game/stream":
                await StreamGameAsync(stream, settings, ct);
                return true;
        }
        await SendJsonAsync(stream, 404, "Not Found", new { error = "Unknown game route" }, ct);
        return true;
    }

    private async Task StreamGameAsync(NetworkStream stream, Core.GameStreamSettings settings, CancellationToken ct)
    {
        if (!await _gameStreamSlot.WaitAsync(0, ct))
        {
            await SendJsonAsync(stream, 409, "Conflict", new { error = "Another phone is already playing.", code = "busy" }, ct);
            return;
        }
        try
        {
            var hello = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: cloudssd-game\r\nConnection: Upgrade\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(hello), ct);
            var device = CurrentCaller.Value?.Device?.Name ?? "phone";
            _log.LogInformation("Game stream started for {Device}", device);
            var session = new GameStreamSession(stream, settings, GameWindow.InFront, () => GameWindow.Find()?.ProcessId, GameWindow.Area, _log);
            // While the phone plays, the emulator goes before background apps on this busy PC.
            using (GameWindow.Boost())
                await session.RunAsync(ct);
            _log.LogInformation("Game stream ended for {Device}", device);
        }
        finally { _gameStreamSlot.Release(); }
    }
}

/// <summary>The emulator on the PC: whether it runs, whether it is in front, and starting the game in it.</summary>
internal static class GameWindow
{
    internal sealed record Info(int ProcessId, IntPtr Window);

    public static Info? Find()
    {
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (GameActivity.IsEmulator(p.ProcessName) && p.MainWindowHandle != IntPtr.Zero) return new Info(p.Id, p.MainWindowHandle);
                }
                catch (InvalidOperationException) { }
            }
        }
        return null;
    }

    public static bool InFront() => GameActivity.IsEmulator(MemoryHealth.ForegroundApp());

    /// <summary>
    /// For as long as the phone plays: the emulator gets "above normal" CPU priority and Windows' power mode is set to
    /// "Best performance" (the user asked for both). Disposing puts both back as they were.
    /// </summary>
    public static IDisposable Boost()
    {
        var undo = new List<Action>();
        if (PowerGetUserConfiguredACPowerMode(out var ac) == 0 && PowerGetUserConfiguredDCPowerMode(out var dc) == 0 && (ac != BestPerformance || dc != BestPerformance))
        {
            var best = BestPerformance;
            PowerSetUserConfiguredACPowerMode(ref best);
            PowerSetUserConfiguredDCPowerMode(ref best);
            undo.Add(() => { PowerSetUserConfiguredACPowerMode(ref ac); PowerSetUserConfiguredDCPowerMode(ref dc); });
        }
        if (Find() is { } info)
        {
            try
            {
                var p = Process.GetProcessById(info.ProcessId);
                var before = p.PriorityClass;
                if (before == ProcessPriorityClass.Normal) p.PriorityClass = ProcessPriorityClass.AboveNormal;
                undo.Add(() => { try { if (!p.HasExited) p.PriorityClass = before; } catch (InvalidOperationException) { } finally { p.Dispose(); } });
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        return new Restore(() => undo.ForEach(a => a()));
    }

    /// <summary>Windows 11's "Best performance" power mode.</summary>
    private static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    [DllImport("powrprof.dll")] private static extern uint PowerGetUserConfiguredACPowerMode(out Guid mode);
    [DllImport("powrprof.dll")] private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid mode);
    [DllImport("powrprof.dll")] private static extern uint PowerSetUserConfiguredACPowerMode(ref Guid mode);
    [DllImport("powrprof.dll")] private static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid mode);

    private sealed class Restore(Action? undo) : IDisposable
    {
        public void Dispose() => undo?.Invoke();
    }

    private static (long At, (int, int, int, int)? Area) _area;

    /// <summary>
    /// Where the game itself is drawn on the desktop (physical pixels): BlueStacks draws Android into a child window of
    /// its own class, so title bar and side toolbar stay off the phone. Other emulators: their biggest child window,
    /// else the whole window. Looked up at most twice a second.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom)? Area()
    {
        var cached = _area;
        if (Environment.TickCount64 - cached.At < 500) return cached.Area;
        (int, int, int, int)? area = null;
        var old = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            if (Find() is { } game)
            {
                IntPtr best = IntPtr.Zero;
                long bestSize = 0;
                bool named = false;
                EnumChildWindows(game.Window, (child, _) =>
                {
                    var cls = new System.Text.StringBuilder(64);
                    GetClassName(child, cls, cls.Capacity);
                    if (!GetWindowRect(child, out var r)) return true;
                    long size = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                    bool isRender = cls.ToString() == "BlueStacksApp";
                    if ((isRender && !named) || (isRender == named && size > bestSize)) { best = child; bestSize = size; named |= isRender; }
                    return true;
                }, IntPtr.Zero);
                if (GetWindowRect(best != IntPtr.Zero ? best : game.Window, out var w)) area = (w.Left, w.Top, w.Right, w.Bottom);
            }
        }
        finally { if (old != IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
        _area = (Environment.TickCount64, area);
        return area;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    /// <summary>Runs the game command (it also brings an emulator that is already open to the game) and waits for its window.</summary>
    public static bool Start(string path, string arguments, TimeSpan wait)
    {
        if (!File.Exists(path)) return false;
        using (Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! })) { }
        var until = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < until)
        {
            if (Find() is not null) return true;
            Thread.Sleep(500);
        }
        return false;
    }
}
