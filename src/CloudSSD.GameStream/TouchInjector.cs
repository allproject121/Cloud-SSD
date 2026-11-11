using System.Runtime.InteropServices;

namespace CloudSSD.GameStream;

/// <summary>
/// Plays the phone's fingers on the PC screen as real Windows touches (up to 10 at once), which BlueStacks passes into
/// Android as its own multi-touch. Windows needs every finger that is down in every report and drops a finger that
/// is not reported for a while, so held fingers are reported again by <see cref="KeepAlive"/>.
/// </summary>
public sealed class TouchInjector
{
    private const int MaxFingers = 10;
    private const int FlagInRange = 0x2, FlagInContact = 0x4, FlagDown = 0x10000, FlagUpdate = 0x20000, FlagUp = 0x40000;

    private readonly Func<(int Left, int Top, int Right, int Bottom)> _area;
    private readonly Dictionary<byte, (uint WinId, int X, int Y)> _down = new();
    private readonly object _lock = new();
    private bool _ready;
    private long _lastReport;

    /// <param name="area">The desktop area the phone sees (physical pixels); a finger at 0..1 lands inside it.</param>
    public TouchInjector(Func<(int Left, int Top, int Right, int Bottom)> area) => _area = area;

    /// <summary>Where a phone finger at (<paramref name="x"/>, <paramref name="y"/>), 0..1 across the picture, lands on the screen.</summary>
    internal static (int X, int Y) Map((int Left, int Top, int Right, int Bottom) screen, double x, double y)
    {
        int w = screen.Right - screen.Left, h = screen.Bottom - screen.Top;
        return (screen.Left + (int)Math.Round(Math.Clamp(x, 0, 1) * (w - 1)), screen.Top + (int)Math.Round(Math.Clamp(y, 0, 1) * (h - 1)));
    }

    public void Apply(IReadOnlyList<StreamProtocol.Finger> fingers)
    {
        lock (_lock)
        {
            if (!Ready()) return;
            var area = _area();
            var report = new List<PointerTouchInfo>();
            var reported = new HashSet<byte>();
            foreach (var f in fingers)
            {
                if (!reported.Add(f.Id)) continue;
                var (x, y) = Map(area, f.X, f.Y);
                if (_down.TryGetValue(f.Id, out var held))
                {
                    if (f.State == StreamProtocol.FingerUp)
                    {
                        // Windows refuses a lift at a new place: lift where the finger last was.
                        report.Add(Contact(held.WinId, held.X, held.Y, FlagUp));
                        _down.Remove(f.Id);
                    }
                    else
                    {
                        report.Add(Contact(held.WinId, x, y, FlagUpdate | FlagInRange | FlagInContact));
                        _down[f.Id] = (held.WinId, x, y);
                    }
                }
                else if (f.State != StreamProtocol.FingerUp && _down.Count < MaxFingers)
                {
                    uint winId = FreeId();
                    report.Add(Contact(winId, x, y, FlagDown | FlagInRange | FlagInContact));
                    _down[f.Id] = (winId, x, y);
                }
            }
            // Fingers still down that this message did not mention.
            foreach (var (id, held) in _down)
                if (!reported.Contains(id)) report.Add(Contact(held.WinId, held.X, held.Y, FlagUpdate | FlagInRange | FlagInContact));
            Send(report);
        }
    }

    /// <summary>Reports held fingers again when nothing moved for a moment; call it every few tens of milliseconds.</summary>
    public void KeepAlive()
    {
        lock (_lock)
        {
            if (_down.Count == 0 || Environment.TickCount64 - _lastReport < 50) return;
            Send(_down.Values.Select(h => Contact(h.WinId, h.X, h.Y, FlagUpdate | FlagInRange | FlagInContact)).ToList());
        }
    }

    /// <summary>Lifts every finger (the game left the front, or the phone went away).</summary>
    public void LiftAll()
    {
        lock (_lock)
        {
            if (_down.Count == 0) return;
            Send(_down.Values.Select(h => Contact(h.WinId, h.X, h.Y, FlagUp)).ToList());
            _down.Clear();
        }
    }

    public int FingersDown { get { lock (_lock) return _down.Count; } }

    private uint FreeId()
    {
        for (uint i = 0; ; i++)
            if (!_down.Values.Any(h => h.WinId == i)) return i;
    }

    private bool Ready()
    {
        if (!_ready) _ready = InitializeTouchInjection(MaxFingers, 3 /* no touch circles drawn on the PC */);
        return _ready;
    }

    private void Send(List<PointerTouchInfo> report)
    {
        if (report.Count == 0) return;
        // The screen bounds are physical pixels; make sure Windows reads them that way on this thread.
        var old = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        try { InjectTouchInput((uint)report.Count, report.ToArray()); }
        finally { if (old != IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
        _lastReport = Environment.TickCount64;
    }

    private static PointerTouchInfo Contact(uint id, int x, int y, int flags) => new()
    {
        PointerInfo = new PointerInfo { PointerType = 2 /* touch */, PointerId = id, PointerFlags = flags, PixelLocation = new Point { X = x, Y = y } },
        TouchMask = 1 | 2 | 4,
        Contact = new Rect { Left = x - 2, Top = y - 2, Right = x + 2, Bottom = y + 2 },
        Orientation = 90,
        Pressure = 32000,
    };

    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerInfo
    {
        public int PointerType;
        public uint PointerId, FrameId;
        public int PointerFlags;
        public IntPtr SourceDevice, TargetWindow;
        public Point PixelLocation, HimetricLocation, PixelLocationRaw, HimetricLocationRaw;
        public uint Time, HistoryCount;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerTouchInfo
    {
        public PointerInfo PointerInfo;
        public int TouchFlags, TouchMask;
        public Rect Contact, ContactRaw;
        public uint Orientation, Pressure;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool InitializeTouchInjection(uint maxCount, uint feedback);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool InjectTouchInput(uint count, [In] PointerTouchInfo[] contacts);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
