using System.Diagnostics;
using Microsoft.Win32;

namespace CloudSSD.App;

/// <summary>
/// The programs Windows lists under "Installed apps" (the Uninstall keys of the registry), with the size the
/// installer reported. Uninstalling starts the program's own uninstaller on the PC screen; the user finishes it there,
/// exactly as from Windows Settings. Store apps are not in these keys; Windows Settings handles those.
/// </summary>
public static class InstalledPrograms
{
    public sealed record Program(string Id, string Name, string Publisher, string Version, long Bytes, DateTime? Installed,
        string? UninstallCommand, string? Location);

    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<Program> Read()
    {
        var list = new List<Program>();
        foreach (var (hive, view, tag) in new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, "lm64"), (RegistryHive.LocalMachine, RegistryView.Registry32, "lm32"),
            (RegistryHive.CurrentUser, RegistryView.Registry64, "cu"),
        })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(UninstallKey);
                if (key == null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(name);
                    if (sub == null) continue;
                    if (Parse(tag + "|" + name, v => sub.GetValue(v)) is { } p) list.Add(p);
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        // The same program can be listed in two views; keep one of each name.
        return list.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(p => p.Bytes).First())
            .OrderByDescending(p => p.Bytes).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>One Uninstall entry, or null for updates, parts of other programs and entries without a name.</summary>
    internal static Program? Parse(string id, Func<string, object?> get)
    {
        string? Text(string n) => get(n) is string s && s.Trim().Length > 0 ? s.Trim() : null;
        var name = Text("DisplayName");
        if (name == null) return null;
        if (get("SystemComponent") is int sc && sc == 1) return null;
        if (Text("ParentKeyName") != null) return null;
        var release = Text("ReleaseType") ?? "";
        if (release.Contains("Update", StringComparison.OrdinalIgnoreCase) || release.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)) return null;
        long kb = get("EstimatedSize") switch { int i => (uint)i, long l => l, _ => 0 };
        DateTime? installed = null;
        if (Text("InstallDate") is { Length: 8 } d && DateTime.TryParseExact(d, "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeLocal, out var when))
            installed = when.ToUniversalTime();
        // The normal uninstaller, not the quiet one: the user sees what it removes and can still say no.
        return new Program(id, name, Text("Publisher") ?? "", Text("DisplayVersion") ?? "", kb * 1024, installed, Text("UninstallString"), Text("InstallLocation")?.Trim('"'));
    }

    /// <summary>Starts the program's uninstaller (it shows its own window on the PC). False when there is none.</summary>
    public static bool StartUninstall(Program p)
    {
        if (string.IsNullOrWhiteSpace(p.UninstallCommand)) return false;
        var (exe, args) = SplitCommand(p.UninstallCommand);
        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    /// <summary>"\"C:\x\un.exe\" /S" or "MsiExec.exe /X{…}" → program and arguments.</summary>
    internal static (string Exe, string Args) SplitCommand(string command)
    {
        var c = command.Trim();
        if (c.StartsWith('"'))
        {
            int end = c.IndexOf('"', 1);
            if (end > 0) return (c[1..end], c[(end + 1)..].Trim());
        }
        int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return (c[..(exe + 4)], c[(exe + 4)..].Trim());
        int space = c.IndexOf(' ');
        return space > 0 ? (c[..space], c[(space + 1)..]) : (c, "");
    }
}
