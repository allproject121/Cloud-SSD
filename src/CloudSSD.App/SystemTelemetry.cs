using System.Runtime.InteropServices;

namespace CloudSSD.App;

/// <summary>A HUD reading. Null means this PC cannot measure it without admin rights or a vendor tool.</summary>
public sealed record SystemSample(double? CpuLoadPercent, double? RamUsedPercent, double? GpuLoadPercent, double? CpuTempC);

/// <summary>
/// Live PC load for the phone's gaming HUD, from APIs that need no admin rights and no extra software:
/// CPU from GetSystemTimes, RAM from GlobalMemoryStatusEx, GPU 3D-engine load and ACPI thermal zones from Windows
/// performance counters (PDH). Rates are deltas between calls; calls closer than 500 ms reuse the last sample.
/// </summary>
public sealed class SystemTelemetry : IDisposable
{
    private readonly object _lock = new();
    private long _idle, _kernel, _user;
    private DateTime _lastAt;
    private SystemSample _last = new(null, null, null, null);
    private IntPtr _query, _gpu, _thermal;

    public SystemTelemetry()
    {
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; return; }
            if (PdhAddEnglishCounterW(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpu) != 0) _gpu = IntPtr.Zero;
            if (PdhAddEnglishCounterW(_query, @"\Thermal Zone Information(*)\Temperature", IntPtr.Zero, out _thermal) != 0) _thermal = IntPtr.Zero;
            PdhCollectQueryData(_query); // first collection primes the rate counters
        }
        catch (DllNotFoundException) { _query = IntPtr.Zero; }
        GetSystemTimes(out _idle, out _kernel, out _user);
    }

    public SystemSample Sample()
    {
        lock (_lock)
        {
            if (DateTime.UtcNow - _lastAt < TimeSpan.FromMilliseconds(500)) return _last;
            _lastAt = DateTime.UtcNow;

            double? cpu = null;
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                long total = (kernel - _kernel) + (user - _user); // kernel time includes idle time
                if (total > 0) cpu = Math.Round(Math.Clamp(100.0 * (total - (idle - _idle)) / total, 0, 100), 1);
                (_idle, _kernel, _user) = (idle, kernel, user);
            }

            double? ram = null;
            var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref mem)) ram = mem.MemoryLoad;

            double? gpu = null, temp = null;
            if (_query != IntPtr.Zero && PdhCollectQueryData(_query) == 0)
            {
                // Sum of every process's 3D-engine share; several GPUs could exceed 100, so clamp.
                if (ReadSum(_gpu, out var g, out _)) gpu = Math.Round(Math.Clamp(g, 0, 100), 1);
                // Thermal zones report Kelvin; use the hottest zone, which is normally the CPU package.
                if (ReadSum(_thermal, out _, out var maxK) && maxK > 273.15) temp = Math.Round(maxK - 273.15, 1);
            }

            return _last = new SystemSample(cpu, ram, gpu, temp);
        }
    }

    /// <summary>Sum and maximum over all instances of a wildcard counter; false when it has no valid instance.</summary>
    private static bool ReadSum(IntPtr counter, out double sum, out double max)
    {
        sum = 0;
        max = double.MinValue;
        if (counter == IntPtr.Zero) return false;
        uint size = 0;
        if (PdhGetFormattedCounterArrayW(counter, PdhFmtDouble | PdhFmtNoCap100, ref size, out _, IntPtr.Zero) != PdhMoreData || size == 0) return false;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, PdhFmtDouble | PdhFmtNoCap100, ref size, out var count, buf) != 0) return false;
            // PDH_FMT_COUNTERVALUE_ITEM_W = { wchar_t* name; { DWORD status; double value } } with 8-byte alignment.
            int itemSize = IntPtr.Size == 8 ? 24 : 16, statusAt = IntPtr.Size, valueAt = IntPtr.Size == 8 ? 16 : 8;
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                if (Marshal.ReadInt32(buf, i * itemSize + statusAt) is not (0 or 1)) continue; // VALID_DATA / NEW_DATA
                var v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(buf, i * itemSize + valueAt));
                sum += v;
                max = Math.Max(max, v);
                any = true;
            }
            return any;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_query != IntPtr.Zero) PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    private const uint PdhFmtDouble = 0x00000200, PdhFmtNoCap100 = 0x00008000, PdhMoreData = 0x800007D2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
