namespace CloudSSD.App;

/// <summary>What one clean-up of the temporary folder removed.</summary>
public sealed record TempCleanupResult(int Files, long Bytes);

/// <summary>
/// Removes old files from the user's temporary folder, the way Windows' own Disk Cleanup does: only files nobody
/// has written for a week, never a file a program still has open, and never through a link into another folder.
/// </summary>
public static class TempCleanup
{
    /// <summary>A temporary file written more recently than this is left alone: a running program may still need it.</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromDays(7);

    /// <summary>
    /// The user's own temporary folder. Taken from the profile, not from the TMP variable: a wrong TMP (say "C:\")
    /// would otherwise point the clean-up at folders that are not temporary at all.
    /// </summary>
    public static string UserTempDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");

    /// <summary>Where Windows keeps the memory dumps of crashed programs; old ones are only useful to a developer.</summary>
    public static string CrashDumpDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");

    /// <summary>
    /// Deletes every file under <paramref name="directory"/> that is older than <see cref="MinAge"/>, then the
    /// folders this left empty. A file that cannot be deleted (in use, no access) is skipped.
    /// </summary>
    public static TempCleanupResult Clean(string directory, DateTime nowUtc, CancellationToken ct = default)
    {
        var root = new DirectoryInfo(directory);
        if (!root.Exists) return new TempCleanupResult(0, 0);

        // Links to other folders are neither followed nor removed.
        var all = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        int files = 0;
        long bytes = 0;
        foreach (var f in root.EnumerateFiles("*", all))
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var changed = f.CreationTimeUtc > f.LastWriteTimeUtc ? f.CreationTimeUtc : f.LastWriteTimeUtc;
                if (nowUtc - changed < MinAge) continue;
                long length = f.Length;
                if (f.IsReadOnly) f.IsReadOnly = false;
                f.Delete();
                files++;
                bytes += length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        // Deepest first, so a folder whose subfolders were just removed can go too.
        foreach (var d in root.EnumerateDirectories("*", all).OrderByDescending(d => d.FullName.Length).ToList())
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (nowUtc - d.CreationTimeUtc >= MinAge && !d.EnumerateFileSystemInfos().Any()) d.Delete();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return new TempCleanupResult(files, bytes);
    }
}

/// <summary>
/// Removes what has been lying in the Recycle Bin for a long time, like Windows' Storage Sense does. Windows keeps
/// each deleted thing as a pair in the bin's folder: "$I…" (when and from where it was deleted) and "$R…" (the
/// file or folder itself).
/// </summary>
public static class RecycleBinCleanup
{
    /// <summary>Only things deleted longer ago than this are removed for good.</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromDays(30);

    /// <summary>This user's Recycle Bin folder on the Windows drive, or null when it cannot be worked out.</summary>
    public static string? UserBinDirectory
    {
        get
        {
            var drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
            return drive is null || sid is null ? null : Path.Combine(drive, "$Recycle.Bin", sid);
        }
    }

    /// <summary>Removes the pairs whose "$I" record is older than <see cref="MinAge"/>. Files counts the things deleted, Bytes their size.</summary>
    public static TempCleanupResult Clean(string binDirectory, DateTime nowUtc, CancellationToken ct = default)
    {
        var bin = new DirectoryInfo(binDirectory);
        if (!bin.Exists) return new TempCleanupResult(0, 0);
        int items = 0;
        long bytes = 0;
        foreach (var record in bin.EnumerateFiles("$I*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (nowUtc - record.LastWriteTimeUtc < MinAge) continue;
                var content = Path.Combine(bin.FullName, "$R" + record.Name[2..]);
                long size = 0;
                if (Directory.Exists(content))
                {
                    var inside = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                    size = new DirectoryInfo(content).EnumerateFiles("*", inside).Sum(f => f.Length);
                    Directory.Delete(content, true);
                }
                else if (File.Exists(content))
                {
                    size = new FileInfo(content).Length;
                    File.SetAttributes(content, FileAttributes.Normal);
                    File.Delete(content);
                }
                record.Delete();
                items++;
                bytes += size;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return new TempCleanupResult(items, bytes);
    }
}
