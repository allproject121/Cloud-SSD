namespace CloudPlayX.Core;

/// <summary>
/// File-system locations used by Cloud-SSD. The data root defaults to %LOCALAPPDATA%\Cloud-SSD (or %LOCALAPPDATA%\CloudPlayX if it exists)
/// and can be redirected with the CLOUD_SSD_HOME or CLOUDPLAYX_HOME environment variable (portable mode, tests).
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? dataRoot = null)
    {
        DataRoot = dataRoot
                   ?? Environment.GetEnvironmentVariable("CLOUD_SSD_HOME")
                   ?? Environment.GetEnvironmentVariable("CLOUDPLAYX_HOME")
                   ?? ResolveDefaultDataRoot();
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogDirectory);
    }

    private static string ResolveDefaultDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ssdPath = Path.Combine(localAppData, "Cloud-SSD");
        var legacyPath = Path.Combine(localAppData, "CloudPlayX");
        if (Directory.Exists(legacyPath) && !Directory.Exists(ssdPath))
        {
            return legacyPath;
        }
        return ssdPath;
    }

    public string DataRoot { get; }
    public string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public string DatabaseFile => File.Exists(Path.Combine(DataRoot, "cloudssd.db"))
        ? Path.Combine(DataRoot, "cloudssd.db")
        : (File.Exists(Path.Combine(DataRoot, "cloudplayx.db")) ? Path.Combine(DataRoot, "cloudplayx.db") : Path.Combine(DataRoot, "cloudssd.db"));
    public string LogDirectory => Path.Combine(DataRoot, "logs");
    public string RcloneConfigFile => Path.Combine(DataRoot, "rclone.conf");
    public string ConfigPasswordFile => Path.Combine(DataRoot, "rclone.conf.key");
    public string MountStateFile => Path.Combine(DataRoot, "mounts.state.json");
    public string DefaultCacheRoot => Path.Combine(DataRoot, "cache");
    public string DefaultLocalGamesRoot => Path.Combine(DataRoot, "local-games");
    public string DiagnosticsDirectory => Path.Combine(DataRoot, "diagnostics");

    public string CacheRootFor(AppSettings s) => string.IsNullOrWhiteSpace(s.Cache.CacheRoot) ? DefaultCacheRoot : s.Cache.CacheRoot!;

    public string MountCacheDir(AppSettings s, string mountId) => Path.Combine(CacheRootFor(s), "m-" + mountId);

    public string LocalGamesRootFor(AppSettings s) => string.IsNullOrWhiteSpace(s.LocalGamesRoot) ? DefaultLocalGamesRoot : s.LocalGamesRoot!;

    /// <summary>Directory containing the running executables (bundled rclone lives under tools\rclone).</summary>
    public static string AppDirectory => AppContext.BaseDirectory;
}
