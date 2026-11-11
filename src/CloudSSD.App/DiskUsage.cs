using System.Runtime.InteropServices;
using CloudSSD.Core;

namespace CloudSSD.App;

/// <summary>One line of "what is using the space": a folder or a big file, its size on disk, and a hint if there is one.</summary>
public sealed record SpaceUse(string Path, long Bytes, string Note = "");

/// <summary>
/// Finds out what takes the space on a drive, so the user can see what to delete or move. Sizes are what the files
/// really occupy (a sparse or compressed file counts with its size on disk, not its nominal size). Folders the
/// user may not read are left out; links to other folders are not followed.
/// </summary>
public static class DiskUsage
{
    /// <summary>Folders this many levels below the drive are still listed on their own; deeper ones count into their parent.</summary>
    public const int Depth = 5;
    /// <summary>A file at least this big is listed by itself instead of inside "other files".</summary>
    public const long BigFile = 256 * ByteSize.MiB;

    /// <summary>A folder or big file with everything below it. Children exist only down to <see cref="Depth"/>.</summary>
    internal sealed class Node(string path, long bytes = 0)
    {
        public string Path { get; } = path;
        public long Bytes { get; set; } = bytes;
        public List<Node> Children { get; } = [];
    }

    /// <summary>Windows' own folders: what is inside is not for the user to tidy up, so they stay one line.</summary>
    internal static readonly HashSet<string> Closed = new(StringComparer.OrdinalIgnoreCase) { "Windows", "$Recycle.Bin", "System Volume Information", "Recovery", "$WinREAgent" };

    private static readonly Dictionary<string, string> Notes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Windows"] = "Windows itself. Leave it.",
        ["pagefile.sys"] = "Windows' virtual memory. Leave it.",
        ["hiberfil.sys"] = "Used for sleep / fast start-up.",
        ["swapfile.sys"] = "Windows' virtual memory. Leave it.",
        ["$Recycle.Bin"] = "Recycle Bin. Empty it to get this space back.",
        ["Program Files"] = "Installed programs. Uninstall what you do not use.",
        ["Program Files (x86)"] = "Installed programs. Uninstall what you do not use.",
        ["Temp"] = "Temporary files. Cloud-SSD removes the old ones by itself.",
        ["npm-cache"] = "Download cache of a developer tool. Safe to delete; it downloads again.",
        ["caches"] = "Download cache of a developer tool. Safe to delete; it downloads again.",
        ["cache"] = "A cache. Usually safe to delete; the app fills it again.",
        ["Downloads"] = "Your downloads.",
    };

    /// <summary>
    /// Reads the whole drive (a minute or two on a full disk) and returns its biggest folders and files, biggest
    /// first. <paramref name="usedBytes"/> is what the drive reports as used; what could not be read shows as one line.
    /// </summary>
    public static IReadOnlyList<SpaceUse> Scan(string driveRoot, long usedBytes, int rows = 16, CancellationToken ct = default)
    {
        var root = Measure(new DirectoryInfo(driveRoot), 0, ct);
        long minBytes = Math.Max(200 * ByteSize.MiB, root.Bytes / 200);
        var list = Pick(root, rows, minBytes).Select(n => new SpaceUse(n.Path, n.Bytes, NoteFor(n.Path))).ToList();
        if (usedBytes - root.Bytes >= minBytes)
            list.Add(new SpaceUse("(not readable without admin rights: system files, other users)", usedBytes - root.Bytes));
        return list.OrderByDescending(r => r.Bytes).ToList();
    }

    /// <summary>
    /// Chooses the lines to show: starting from the drive, a folder that holds more than a twentieth of everything
    /// is replaced by what is inside it, again and again, so the list names the folders that really matter instead
    /// of "Users". Then the biggest <paramref name="rows"/> of at least <paramref name="minBytes"/> are kept.
    /// </summary>
    internal static IReadOnlyList<Node> Pick(Node root, int rows, long minBytes)
    {
        long open = Math.Max(minBytes, root.Bytes / 20);
        var list = new List<Node> { root };
        while (list.Where(n => n.Children.Count > 0 && (n == root || n.Bytes >= open)).MaxBy(n => n.Bytes) is { } biggest)
        {
            list.Remove(biggest);
            list.AddRange(biggest.Children);
            long rest = biggest.Bytes - biggest.Children.Sum(c => c.Bytes);
            if (rest > 0) list.Add(new Node(System.IO.Path.Combine(biggest.Path, "(other files here)"), rest));
        }
        return list.Where(n => n.Bytes >= minBytes).OrderByDescending(n => n.Bytes).Take(rows).ToList();
    }

    internal static Node Measure(DirectoryInfo dir, int depth, CancellationToken ct)
    {
        var node = new Node(dir.FullName);
        bool listed = depth < Depth && !(depth == 1 && Closed.Contains(dir.Name));
        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                if (ct.IsCancellationRequested) break;
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (entry is DirectoryInfo sub)
                {
                    var child = Measure(sub, depth + 1, ct);
                    node.Bytes += child.Bytes;
                    if (listed) node.Children.Add(child);
                }
                else if (entry is FileInfo file)
                {
                    long size = OnDisk(file);
                    node.Bytes += size;
                    if (listed && size >= BigFile) node.Children.Add(new Node(file.FullName, size));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return node;
    }

    internal static string NoteFor(string path) => Notes.GetValueOrDefault(System.IO.Path.GetFileName(path.TrimEnd('\\')) ?? "", "");

    /// <summary>What the file occupies. Only sparse and compressed files can differ from their length, so only those are asked for.</summary>
    internal static long OnDisk(FileInfo file)
    {
        if ((file.Attributes & (FileAttributes.SparseFile | FileAttributes.Compressed)) == 0) return file.Length;
        uint low = GetCompressedFileSizeW(@"\\?\" + file.FullName, out uint high);
        return low == uint.MaxValue && Marshal.GetLastWin32Error() != 0 ? file.Length : ((long)high << 32) | low;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string fileName, out uint fileSizeHigh);
}
