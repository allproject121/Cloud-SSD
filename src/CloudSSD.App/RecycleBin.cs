using System.Runtime.InteropServices;

namespace CloudSSD.App;

/// <summary>
/// The Windows Recycle Bin: put a file or folder in it (it can be restored from there), see how much it holds, and
/// empty it. Uses the same shell calls as Explorer's Delete key, without its dialogs.
/// </summary>
public static class RecycleBin
{
    /// <summary>Moves a file or folder to the Recycle Bin. False when Windows could not (in use, no bin on that disk…).</summary>
    public static bool Send(string path)
    {
        bool ok = false;
        // The shell wants a single-threaded apartment; a short thread of its own keeps callers free of that.
        var t = new Thread(() =>
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = Path.GetFullPath(path).TrimEnd('\\') + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
            };
            ok = SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        return ok && !File.Exists(path) && !Directory.Exists(path);
    }

    /// <summary>What the Recycle Bins of all disks hold.</summary>
    public static (long Bytes, long Items) Size()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    /// <summary>Empties the Recycle Bins of all disks (no dialog, no sound).</summary>
    public static bool Empty() => SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND) is 0 or unchecked((int)0x8000FFFF);

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_NOERRORUI = 0x0400;
    private const uint SHERB_NOCONFIRMATION = 1, SHERB_NOPROGRESSUI = 2, SHERB_NOSOUND = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    // 64-bit layout (natural alignment); the app is built for x64 only.
    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}
