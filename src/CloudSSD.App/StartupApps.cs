using Microsoft.Win32;

namespace CloudSSD.App;

/// <summary>
/// Programs that start with Windows, read and switched the way Task Manager's "Startup apps" does it: the entry stays,
/// only its on/off flag under StartupApproved changes, so turning it back on restores it exactly. This user's entries
/// can be switched; the ones for all users need admin rights and are shown only.
/// </summary>
public static class StartupApps
{
    public sealed record Entry(string Id, string Name, string Command, bool Enabled, string Source, bool CanChange);

    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public static IReadOnlyList<Entry> Read()
    {
        var list = new List<Entry>();
        using (var cu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            list.AddRange(ReadRun(cu, "cu-run", "This user", canChange: true));
        try
        {
            using var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            list.AddRange(ReadRun(lm, "lm-run", "All users", canChange: false));
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException) { }
        list.AddRange(ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "cu-folder", "Startup folder", canChange: true));
        list.AddRange(ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "lm-folder", "Startup folder (all users)", canChange: false));
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static IEnumerable<Entry> ReadRun(RegistryKey root, string tag, string source, bool canChange, string runPath = Run, string approvedPath = Approved + @"\Run")
    {
        using var run = root.OpenSubKey(runPath);
        if (run == null) yield break;
        using var approved = root.OpenSubKey(approvedPath);
        foreach (var name in run.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            yield return new Entry(tag + "|" + name, name, run.GetValue(name)?.ToString() ?? "", IsEnabled(approved?.GetValue(name)), source, canChange);
        }
    }

    private static IEnumerable<Entry> ReadFolder(string folder, string tag, string source, bool canChange)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) yield break;
        using var cu = RegistryKey.OpenBaseKey(tag.StartsWith("cu") ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);
        using var approved = cu.OpenSubKey(Approved + @"\StartupFolder");
        foreach (var f in Directory.EnumerateFiles(folder).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
        {
            var file = Path.GetFileName(f);
            yield return new Entry(tag + "|" + file, Path.GetFileNameWithoutExtension(f), f, IsEnabled(approved?.GetValue(file)), source, canChange);
        }
    }

    /// <summary>No flag = on. Otherwise the first byte is even (2, 6) when on and odd (3, 7) when switched off.</summary>
    internal static bool IsEnabled(object? flag) => flag is not byte[] b || b.Length == 0 || (b[0] & 1) == 0;

    /// <summary>Switches one of this user's entries on or off. False for entries that cannot be changed here.</summary>
    public static bool SetEnabled(string id, bool enabled)
    {
        var parts = id.Split('|', 2);
        if (parts.Length != 2 || !parts[0].StartsWith("cu")) return false;
        using var cu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        return SetFlag(cu, Approved + (parts[0] == "cu-folder" ? @"\StartupFolder" : @"\Run"), parts[1], enabled);
    }

    internal static bool SetFlag(RegistryKey root, string approvedPath, string name, bool enabled)
    {
        using var key = root.CreateSubKey(approvedPath);
        if (key == null) return false;
        var flag = new byte[12];
        flag[0] = (byte)(enabled ? 2 : 3);
        // Task Manager stores when it was switched off.
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(flag, 4);
        key.SetValue(name, flag, RegistryValueKind.Binary);
        return true;
    }
}
