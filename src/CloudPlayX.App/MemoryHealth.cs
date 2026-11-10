using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CloudPlayX.Core;
using Microsoft.Win32;

namespace CloudPlayX.App;

/// <summary>One Windows page file. Sizes are bytes.</summary>
public sealed record PageFileInfo(string Path, long AllocatedBytes, long UsedBytes, long PeakBytes);

/// <summary>
/// RAM held by all processes of one app (their working sets added up). CanClose: it has a window of its own in
/// this user's session and is not a Windows or Cloud-SSD process, so asking it to close is safe to offer.
/// Title is the app's own description ("Microsoft Edge WebView2") when Windows lets us read it.
/// </summary>
public sealed record AppMemory(string Name, int Processes, long WorkingSetBytes, bool CanClose = false, string? Title = null);

/// <summary>
/// What Windows reports about memory right now. Commit is what apps have reserved; its limit is RAM + page files.
/// PageFileSystemManaged is null when the setting could not be read. TopApps lists only apps the user could
/// close; Windows' and Cloud-SSD's own processes are added up in SystemBytes.
/// </summary>
public sealed record MemorySnapshot(
    long PhysicalTotalBytes, long PhysicalAvailableBytes, long CommitUsedBytes, long CommitLimitBytes,
    IReadOnlyList<PageFileInfo> PageFiles, bool? PageFileSystemManaged,
    string? PageFileVolume, long? PageFileVolumeFreeBytes, bool CacheSharesPageFileVolume,
    IReadOnlyList<AppMemory> TopApps, long SystemBytes = 0, int SystemProcesses = 0)
{
    public double RamUsedFraction => PhysicalTotalBytes <= 0 ? 0 : 1 - (double)PhysicalAvailableBytes / PhysicalTotalBytes;
    public double CommitUsedFraction => CommitLimitBytes <= 0 ? 0 : (double)CommitUsedBytes / CommitLimitBytes;
}

/// <summary>One finding. Format is an English sentence with {0}-style places for Args, so the UI can translate it.</summary>
public sealed record MemoryAdvice(string Format, object[] Args, string Tone)
{
    public string Text => string.Format(Format, Args);
}

/// <summary>
/// Memory check for the PC Memory page: RAM, commit, page file and the apps holding the most RAM. Needs no admin
/// rights and never changes a Windows setting, because a page file adds no speed and "RAM cleaner" tricks give
/// nothing Windows does not already do. Closing apps is what actually frees RAM, so that is the one action offered.
/// </summary>
public static class MemoryHealth
{
    /// <summary>With this share of RAM in use Windows starts leaning on the page file.</summary>
    public const double RamAlmostFullFraction = 0.85;
    /// <summary>With less than this share of RAM free the PC is "low on RAM": advice says so and auto mode acts.</summary>
    public const double RamLowFreeFraction = 0.30;
    /// <summary>At the commit limit allocations fail and apps close with "out of memory".</summary>
    public const double CommitNearLimitFraction = 0.90;
    /// <summary>Room the page file's drive should keep so Windows can grow it.</summary>
    public const long PageFileRoomBytes = 20 * ByteSize.GiB;

    /// <summary>Windows' own compression store; it is listed as a process but is not an app the user can close.</summary>
    private const string CompressionProcess = "Memory Compression";

    /// <summary>Windows itself and Cloud-SSD's own processes: closing these breaks the desktop or the cloud drives.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Secure System", CompressionProcess, "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "svchost", "dwm", "fontdrvhost", "sihost", "ctfmon", "taskhostw", "conhost", "RuntimeBroker", "explorer",
        "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "TextInputHost", "ApplicationFrameHost",
        "MsMpEng", "SecurityHealthService", "rclone", "cpx", "Cloud-SSD",
    };

    public static MemorySnapshot Read(string? cacheRoot = null, int topApps = 8)
    {
        var perf = ReadPerformanceInfo();
        long page = (long)perf.PageSize;

        var files = new List<PageFileInfo>();
        EnumPageFilesCallback callback = (IntPtr _, ref EnumPageFileInformation info, string name) =>
        {
            files.Add(new PageFileInfo(NormalizePath(name), (long)info.TotalSize * page, (long)info.TotalInUse * page, (long)info.PeakUsage * page));
            return true;
        };
        K32EnumPageFilesW(callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        string? volume = null;
        long? volumeFree = null;
        bool shared = false;
        if (files.Count > 0)
        {
            try
            {
                volume = DiskInfo.VolumeRoot(files[0].Path);
                volumeFree = DiskInfo.FreeSpace(volume).FreeForUser;
                shared = cacheRoot != null && string.Equals(DiskInfo.VolumeRoot(cacheRoot), volume, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is Win32Exception or IOException or ArgumentException) { }
        }

        var (apps, systemBytes, systemProcesses) = ReadTopApps(topApps);
        return new MemorySnapshot((long)perf.PhysicalTotal * page, (long)perf.PhysicalAvailable * page,
            (long)perf.CommitTotal * page, (long)perf.CommitLimit * page,
            files, IsSystemManaged(ReadPagingFilesSetting()), volume, volumeFree, shared, apps, systemBytes, systemProcesses);
    }

    /// <summary>Sizes the way Task Manager shows them: "7.3 GB", "865 MB".</summary>
    public static string Size(long bytes) =>
        bytes >= 10 * ByteSize.GiB ? $"{bytes / (double)ByteSize.GiB:0.0} GB"
        : bytes >= ByteSize.GiB ? $"{bytes / (double)ByteSize.GiB:0.00} GB"
        : $"{bytes / (double)ByteSize.MiB:0} MB";

    /// <summary>Only RAM total and free: cheap enough to sample every second for a chart.</summary>
    public static (long TotalBytes, long AvailableBytes) ReadRam()
    {
        var perf = ReadPerformanceInfo();
        return ((long)perf.PhysicalTotal * (long)perf.PageSize, (long)perf.PhysicalAvailable * (long)perf.PageSize);
    }

    /// <summary>Plain-language findings, most serious first. Always returns at least one line.</summary>
    public static IReadOnlyList<MemoryAdvice> Advise(MemorySnapshot s)
    {
        var list = new List<MemoryAdvice>();
        void Add(string tone, string format, params object[] args) => list.Add(new(format, args, tone));

        if (s.PageFiles.Count == 0)
            Add("error", "Windows has no page file. When RAM is full, games and apps will close with an 'out of memory' error. " +
                         "In Windows, turn on 'Automatically manage paging file size for all drives'.");
        if (s.CommitUsedFraction >= CommitNearLimitFraction)
            Add("error", "Apps have reserved {0} of the {1} Windows can give (RAM + page file). " +
                         "At the limit, games close with an 'out of memory' error. Close apps you do not need." +
                         (s.PageFileSystemManaged == false ? " The page file has a fixed size; set it back to 'Automatically manage' so Windows can grow it." : ""),
                Size(s.CommitUsedBytes), Size(s.CommitLimitBytes));
        if (s.RamUsedFraction >= RamAlmostFullFraction)
            Add("warn", "RAM is almost full: only {0} of {1} is free. " +
                        "When RAM runs out, Windows uses the page file on the SSD, which is far slower and makes games stutter. " +
                        "Close apps from the list before you play.", Size(s.PhysicalAvailableBytes), Size(s.PhysicalTotalBytes));
        else if (1 - s.RamUsedFraction < RamLowFreeFraction)
            Add("accent", "RAM is getting low: {0} of {1} is free ({2}). Close apps you do not need before you play.",
                Size(s.PhysicalAvailableBytes), Size(s.PhysicalTotalBytes), $"{1 - s.RamUsedFraction:P0}");
        if (s.PageFileVolumeFreeBytes is { } free && free < PageFileRoomBytes)
            Add("warn", s.CacheSharesPageFileVolume
                    ? "Only {0} free on {1}. Windows needs space there to grow the page file, and the Cloud-SSD cache uses the same drive. Keep at least {2} free."
                    : "Only {0} free on {1}. Windows needs space there to grow the page file. Keep at least {2} free.",
                Size(free), s.PageFileVolume ?? "", Size(PageFileRoomBytes));
        if (list.Count == 0)
            Add("ok", "Memory looks healthy: {0} of RAM is free.", Size(s.PhysicalAvailableBytes));
        return list;
    }

    /// <summary>
    /// Asks an app to close the normal way: WM_CLOSE to each of its windows, the same as pressing X, so it can
    /// still ask to save. Never kills. Returns how many windows were asked; 0 for protected or windowless apps.
    /// </summary>
    public static int RequestClose(string appName)
    {
        if (IsProtected(appName)) return 0;
        int session = Process.GetCurrentProcess().SessionId;
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName(appName))
        {
            try { if (p.SessionId == session) pids.Add((uint)p.Id); }
            catch (InvalidOperationException) { } // the process exited while being read
            finally { p.Dispose(); }
        }
        int asked = 0;
        foreach (var (window, pid) in AppWindows())
            if (pids.Contains(pid) && PostMessageW(window, WmClose, IntPtr.Zero, IntPtr.Zero)) asked++;
        return asked;
    }

    /// <summary>Name of the app whose window is in front, or null when there is none or it cannot be read.</summary>
    public static string? ForegroundApp()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out var pid) == 0) return null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return null; }
    }

    /// <summary>
    /// Moves the memory of this user's background apps out of RAM (Windows keeps it compressed or in the page
    /// file and reads it back when the app is used again). Nothing is closed. Skips the named apps, Windows and
    /// Cloud-SSD processes, and apps too small to matter. Returns how many apps were moved.
    /// </summary>
    public static int TrimBackgroundApps(ISet<string> skip, long minWorkingSetBytes = 32 * ByteSize.MiB)
    {
        int session = Process.GetCurrentProcess().SessionId;
        var trimmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.SessionId != session || p.WorkingSet64 < minWorkingSetBytes || IsProtected(p.ProcessName) || skip.Contains(p.ProcessName)) continue;
                if (Trim(p.Id)) trimmed.Add(p.ProcessName);
            }
            catch (InvalidOperationException) { } // the process exited while being read
            finally { p.Dispose(); }
        }
        return trimmed.Count;
    }

    /// <summary>False when Windows refuses, for example for a process that runs as administrator.</summary>
    internal static bool Trim(int processId)
    {
        var handle = OpenProcess(ProcessSetQuota | ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == IntPtr.Zero) return false;
        try { return K32EmptyWorkingSet(handle); }
        finally { CloseHandle(handle); }
    }

    public static bool IsRunning(string appName)
    {
        var found = Process.GetProcessesByName(appName);
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    internal static bool IsProtected(string appName) =>
        Protected.Contains(appName) || string.Equals(appName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the PagingFiles setting: "?:\pagefile.sys" or sizes "0 0" mean Windows sizes the file itself,
    /// any other sizes are a fixed custom size. Null when the setting is missing or empty.
    /// </summary>
    internal static bool? IsSystemManaged(string[]? pagingFiles)
    {
        var entries = pagingFiles?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        if (entries is null || entries.Count == 0) return null;
        return entries.All(e =>
        {
            var parts = e.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length < 3 || (parts[^2] == "0" && parts[^1] == "0");
        });
    }

    /// <summary>EnumPageFiles reports NT paths such as "\??\C:\pagefile.sys".</summary>
    internal static string NormalizePath(string ntPath) => ntPath.StartsWith(@"\??\", StringComparison.Ordinal) ? ntPath[4..] : ntPath;

    private static PerformanceInformation ReadPerformanceInfo()
    {
        var perf = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (!K32GetPerformanceInfo(ref perf, perf.Size)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return perf;
    }

    private static string[]? ReadPagingFilesSetting()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
            return key?.GetValue("PagingFiles") as string[];
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    /// <summary>
    /// The apps holding the most RAM, biggest first. Windows' and Cloud-SSD's own processes are not listed (the
    /// user cannot do anything about them); their RAM is returned as one total instead.
    /// </summary>
    private static (IReadOnlyList<AppMemory> Apps, long SystemBytes, int SystemProcesses) ReadTopApps(int count)
    {
        int session = Process.GetCurrentProcess().SessionId;
        var windowed = AppWindows().Select(w => w.ProcessId).ToHashSet();
        var byName = new Dictionary<string, (int Processes, long Bytes, bool HasWindow, int BiggestId, long BiggestBytes)>(StringComparer.OrdinalIgnoreCase);
        long systemBytes = 0;
        int systemProcesses = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                long bytes = p.WorkingSet64;
                if (IsProtected(p.ProcessName))
                {
                    systemBytes += bytes;
                    systemProcesses++;
                    continue;
                }
                byName.TryGetValue(p.ProcessName, out var cur);
                bool biggest = bytes > cur.BiggestBytes;
                byName[p.ProcessName] = (cur.Processes + 1, cur.Bytes + bytes,
                    cur.HasWindow || (p.SessionId == session && windowed.Contains((uint)p.Id)),
                    biggest ? p.Id : cur.BiggestId, biggest ? bytes : cur.BiggestBytes);
            }
            catch (InvalidOperationException) { } // the process exited while being read
            finally { p.Dispose(); }
        }
        var apps = byName.OrderByDescending(kv => kv.Value.Bytes).Take(count)
            .Select(kv => new AppMemory(kv.Key, kv.Value.Processes, kv.Value.Bytes, kv.Value.HasWindow, Describe(kv.Value.BiggestId))).ToList();
        return (apps, systemBytes, systemProcesses);
    }

    /// <summary>Frameworks many unrelated apps are built on; their description says nothing about which app it is.</summary>
    private static readonly HashSet<string> GenericDescriptions = new(StringComparer.OrdinalIgnoreCase) { "Electron", "Node.js JavaScript Runtime", ".NET Host" };

    /// <summary>The description the app's maker gave its program file, or null when it is missing, generic or unreadable.</summary>
    private static string? Describe(int processId)
    {
        try
        {
            using var p = Process.GetProcessById(processId);
            var text = p.MainModule?.FileVersionInfo.FileDescription?.Trim();
            return string.IsNullOrEmpty(text) || GenericDescriptions.Contains(text) ? null : text;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>Visible top-level windows without an owner (what the taskbar shows), with their process id.</summary>
    private static List<(IntPtr Window, uint ProcessId)> AppWindows()
    {
        var list = new List<(IntPtr, uint)>();
        EnumWindowsCallback callback = (window, _) =>
        {
            if (IsWindowVisible(window) && GetWindow(window, GwOwner) == IntPtr.Zero && GetWindowThreadProcessId(window, out var pid) != 0)
                list.Add((window, pid));
            return true;
        };
        EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return list;
    }

    private const uint WmClose = 0x0010, GwOwner = 4, ProcessSetQuota = 0x0100, ProcessQueryLimitedInformation = 0x1000;

    // All sizes are in pages.
    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumPageFileInformation
    {
        public uint Size, Reserved;
        public nuint TotalSize, TotalInUse, PeakUsage;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate bool EnumPageFilesCallback(IntPtr context, ref EnumPageFileInformation info, string fileName);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr context);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool K32GetPerformanceInfo(ref PerformanceInformation info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool K32EnumPageFilesW(EnumPageFilesCallback callback, IntPtr context);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool K32EmptyWorkingSet(IntPtr process);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr context);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
