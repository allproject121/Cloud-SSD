using System.Globalization;
using CloudPlayX.Core;

namespace CloudPlayX.Mount;

public static class DriveLetterAllocator
{
    private static readonly char[] Preference = "GXYZWVUTSRQPONMLKJIH".ToCharArray();

    public static IReadOnlySet<char> UsedLetters()
    {
        var used = new HashSet<char>();
        foreach (var d in DriveInfo.GetDrives()) used.Add(char.ToUpperInvariant(d.Name[0]));
        return used;
    }

    public static IReadOnlyList<char> FreeLetters(IReadOnlySet<char>? used = null)
    {
        used ??= UsedLetters();
        return "DEFGHIJKLMNOPQRSTUVWXYZ".Where(c => !used.Contains(c)).ToList();
    }

    /// <summary>Returns the requested letter if free, otherwise the first free letter by preference (G first, then X..H).</summary>
    public static char? Choose(string? requested, IReadOnlySet<char>? used = null)
    {
        used ??= UsedLetters();
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var c = char.ToUpperInvariant(requested.Trim()[0]);
            if (c is >= 'D' and <= 'Z' && !used.Contains(c)) return c;
        }
        foreach (var c in Preference) if (!used.Contains(c)) return c;
        return null;
    }
}

public sealed record MountPlan(IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string?> Environment, string CacheDir, string LogFile,
    long EffectiveCacheMaxBytes, IReadOnlyList<string> Warnings);

public sealed record MountPlanInput(
    MountDefinition Mount,
    char DriveLetter,
    CacheSettings Cache,
    NetworkSettings Network,
    string CacheDir,
    string LogFile,
    long EffectiveCacheMaxBytes,
    bool Telemetry,
    int RcPort,
    string? RemoteType);

/// <summary>
/// Translates Cloud-SSD settings into rclone mount arguments. Every optional flag is checked against the flags the
/// installed rclone reports, so an older rclone gets a warning instead of a failed mount.
/// </summary>
public static class MountFlagBuilder
{
    public static MountPlan Build(MountPlanInput i, RcloneInfo rclone)
    {
        var warnings = new List<string>();
        var a = new List<string> { "mount", i.Mount.RemoteSpec, $"{i.DriveLetter}:" };

        void Flag(string name, string? value = null)
        {
            if (!rclone.Supports(name))
            {
                warnings.Add($"rclone {rclone.Version} does not support --{name}; skipped.");
                return;
            }
            a.Add("--" + name);
            if (value != null) a.Add(value);
        }

        Flag("vfs-cache-mode", "full");
        Flag("cache-dir", i.CacheDir);
        Flag("vfs-cache-max-size", ByteSize.ToRclone(Math.Max(16 * ByteSize.MiB, i.EffectiveCacheMaxBytes)));
        if (i.Cache.MinFreeSpaceBytes > 0) Flag("vfs-cache-min-free-space", ByteSize.ToRclone(i.Cache.MinFreeSpaceBytes));
        Flag("vfs-cache-max-age", Dur(i.Cache.MaxAge));
        Flag("vfs-cache-poll-interval", Dur(i.Cache.PollInterval));
        // 0 is a deliberate default, not "unset": in full cache mode rclone downloads buffer-size + read-ahead past
        // every read position, so 32Mi + 16Mi turned each small random game read into up to 48 MiB of speculative
        // download (3 x 64 KiB reads fetched a whole 24 MiB file; BENCHMARKS.md: ~3x slower cold launches). The
        // prediction engine owns read-ahead; users on very fast links can still raise both in Settings.
        Flag("buffer-size", ByteSize.ToRclone(i.Cache.BufferSizeBytes));
        Flag("vfs-read-ahead", ByteSize.ToRclone(i.Cache.ReadAheadBytes));
        Flag("vfs-read-chunk-size", ByteSize.ToRclone(i.Cache.ReadChunkSizeBytes > 0 ? i.Cache.ReadChunkSizeBytes : 8 * ByteSize.MiB));
        var chunkStreams = i.Cache.ReadChunkStreams > 0 ? i.Cache.ReadChunkStreams : 1;
        if (chunkStreams > 1) Flag("vfs-read-chunk-streams", chunkStreams.ToString(CultureInfo.InvariantCulture));
        Flag("vfs-write-back", Dur(i.Cache.WriteBack));
        Flag("dir-cache-time", "72h");
        Flag("attr-timeout", "24h");
        Flag("poll-interval", "1m");
        Flag("vfs-fast-fingerprint");
        // Must match RcloneMountService.HandleCachingWindow (6 s), which graceful unmount and Prepare's readiness check
        // wait on. rclone writes a file's range index only when the cached handle really closes, and a cached handle
        // pins the item against eviction ("Grace period item skipped"). With 30s, quitting within 30 s of a read lost
        // the index (re-download on next start), and under cache/disk pressure rclone reset the file being read
        // instead of the older, closed ones.
        Flag("vfs-handle-caching", "5s");
        // No --vfs-disk-space-total-size: a fixed "15T" made every drive claim 15 TB in File Explorer. Without it rclone
        // reports the account's real quota (for a combined drive, the sum of its accounts).
        Flag("vfs-refresh");
        Flag("vfs-read-chunk-size-limit", "256Mi");
        Flag("no-checksum");
        Flag("filter", "- desktop.ini");
        Flag("filter", "- folder.jpg");
        Flag("filter", "- folder.gif");
        Flag("filter", "- Thumbs.db");
        Flag("filter", "- AlbumArt*");
        // Executables need the execute bit in the WinFsp ACL mapping; default 0666 makes CreateProcess fail with
        // "Access is denied" (verified on rclone 1.75.2 / WinFsp 2025).
        Flag("file-perms", "0777");
        Flag("dir-perms", "0777");
        Flag("volname", Sanitize($"Cloud Drive {i.Mount.DisplayName}"));
        if (i.Mount.ReadOnly) Flag("read-only");

        // Google Drive pacer safety and Windows encoding compatibility
        Flag("drive-pacer-min-sleep", "100ms");
        Flag("drive-pacer-burst", "10");
        Flag("drive-acknowledge-abuse");
        Flag("drive-encoding", "Slash,LtGt,DoubleQuote,Colon,Question,Asterisk,Pipe,BackSlash,Del,Ctl,RightSpace,RightPeriod,InvalidUtf8,Dot");

        if (i.Network.HardLimitMbps > 0)
            Flag("bwlimit", ((long)Bandwidth.MbpsToBytesPerSecond(i.Network.HardLimitMbps)).ToString(CultureInfo.InvariantCulture) + "B");
        // --tpslimit is one global limiter shared by every HTTP transaction (listings, per-file HEADs, chunk GETs). It
        // protects Google Drive API quotas, so it applies to Drive only (NetworkSettings default 10) and 0 keeps its
        // documented meaning "unlimited". Forcing 10 on every remote let the startup directory walks (--vfs-refresh and
        // the rc vfs/refresh after mount) starve data reads: cold throughput fell to ~20 % of the link, a walk took 25 s
        // instead of 60 ms, and an in-flight walk delayed rclone's shutdown by ~11 s.
        // Every cloud API remote keeps the limit, including unions/crypts built on Drive accounts (the real Z: drive is a
        // union of three); only quota-free HTTP/local sources (the simulated cloud in tests) skip it.
        if (i.Network.TpsLimit > 0 && i.RemoteType is not ("http" or "local"))
            Flag("tpslimit", i.Network.TpsLimit.ToString(CultureInfo.InvariantCulture));

        Flag("rc");
        Flag("rc-addr", $"127.0.0.1:{i.RcPort}");
        Flag("log-file", i.LogFile);
        Flag("log-format", "date,time,microseconds");
        if (rclone.Supports("log-file-max-size")) { Flag("log-file-max-size", "64Mi"); Flag("log-file-max-backups", "2"); }
        a.Add(i.Telemetry ? "-vv" : "-v");

        foreach (var (k, v) in i.Mount.ExtraFlags)
        {
            var name = k.TrimStart('-');
            if (IsForbiddenExtraFlag(name))
            {
                warnings.Add($"Extra flag --{name} is not allowed and was ignored.");
                continue;
            }
            Flag(name, string.IsNullOrEmpty(v) ? null : v);
        }

        return new MountPlan(a, new Dictionary<string, string?>(), i.CacheDir, i.LogFile, i.EffectiveCacheMaxBytes, warnings);
    }

    /// <summary>Flags that could execute commands, expose the rc server or redirect credentials.</summary>
    public static bool IsForbiddenExtraFlag(string name) =>
        name.Contains("password-command", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("rc", StringComparison.OrdinalIgnoreCase)
        || name.Equals("config", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("cache-dir", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("log-file", StringComparison.OrdinalIgnoreCase)
        || name.Contains("command", StringComparison.OrdinalIgnoreCase);

    public static string Dur(TimeSpan t) => t.TotalHours >= 1 && t.TotalHours == Math.Floor(t.TotalHours)
        ? $"{(long)t.TotalHours}h"
        : t.TotalSeconds >= 1 ? $"{(long)t.TotalSeconds}s" : $"{(long)t.TotalMilliseconds}ms";

    private static string Sanitize(string s) => new(s.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').Take(32).ToArray());
}
