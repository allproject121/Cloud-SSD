using System.Collections.Concurrent;
using System.Threading.Channels;
using CloudPlayX.Core;
using Microsoft.Extensions.Logging;

namespace CloudPlayX.App;

/// <summary>
/// Makes archives on a cloud drive open at once. Windows opens a .zip by reading its very end (the file list) in
/// small steps going backwards, plus the first bytes; on a cloud drive each step is a separate request to the
/// provider, and each request can take seconds (more when the provider rate-limits). So when a folder is listed,
/// this fetches the start and the end of every archive in it ahead of time, one request each, into rclone's cache;
/// a later double-click then needs no request at all. Small archives are fetched whole. Only the content of files
/// inside a big archive is still downloaded when it is actually opened or extracted.
/// </summary>
public sealed class ArchiveWarmer : IDisposable
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".rar" };

    /// <summary>Archives up to this size are fetched completely (then extracting is instant too).</summary>
    public const long WholeFileLimit = 8 * ByteSize.MiB;
    /// <summary>End of the archive: a zip's file list ("central directory") lives here — about 1 MiB covers ~10,000 files.</summary>
    public const int TailBytes = (int)ByteSize.MiB;
    /// <summary>Start of the archive: read by Windows when it identifies the file.</summary>
    public const int HeadBytes = 128 * 1024;
    private const int MaxPerDirectory = 40;
    private static readonly TimeSpan Rescan = TimeSpan.FromMinutes(5);

    private readonly Func<string, string?> _mountRoot;
    private readonly Func<string, ReadAttributionRegistry?> _attribution;
    private readonly Func<bool> _paused;
    private readonly ILogger _log;
    private readonly Channel<(string MountId, string Dir)> _queue =
        Channel.CreateBounded<(string, string)>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly ConcurrentDictionary<string, DateTime> _listed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _warmed = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();

    public ArchiveWarmer(Func<string, string?> mountRoot, Func<string, ReadAttributionRegistry?> attribution, Func<bool> paused, ILogger log)
    {
        _mountRoot = mountRoot;
        _attribution = attribution;
        _paused = paused;
        _log = log;
        _ = Task.Run(WorkAsync);
    }

    /// <summary>(path relative to the drive root, bytes fetched) after an archive was made ready.</summary>
    public event Action<string, long>? Warmed;

    /// <summary>A folder of a drive was listed. Cheap: the same folder is looked at again at most every few minutes.</summary>
    public void OnDirectoryListed(string mountId, string dir)
    {
        if (_paused()) return;
        var key = mountId + "|" + dir;
        var now = DateTime.UtcNow;
        if (_listed.TryGetValue(key, out var last) && now - last < Rescan) return;
        _listed[key] = now;
        _queue.Writer.TryWrite((mountId, dir));
    }

    /// <summary>What to fetch of an archive of this size: everything when small, otherwise its end and its start.</summary>
    public static IReadOnlyList<(long Offset, int Length)> Ranges(long size) =>
        size <= 0 ? []
        : size <= WholeFileLimit ? [(0, (int)size)]
        : [(size - TailBytes, TailBytes), (0, HeadBytes)];

    // One folder and one archive at a time, so this never competes with what the user is doing for provider requests.
    private async Task WorkAsync()
    {
        try
        {
            await foreach (var (mountId, dir) in _queue.Reader.ReadAllAsync(_cts.Token))
            {
                try { await WarmDirectoryAsync(mountId, dir, _cts.Token); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    _log.LogDebug("Archive warm-up of {Dir} skipped: {Error}", dir, e.Message);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task WarmDirectoryAsync(string mountId, string dir, CancellationToken ct)
    {
        if (_mountRoot(mountId) is not { } root) return;
        var full = Path.Combine(root, dir.Replace('/', '\\'));
        if (!Directory.Exists(full)) return;
        // Listing the folder here shows up as one more "folder listed" event; the rescan interval absorbs it.
        var archives = new DirectoryInfo(full).EnumerateFiles().Where(f => Extensions.Contains(f.Extension)).Take(MaxPerDirectory).ToList();
        foreach (var f in archives)
        {
            ct.ThrowIfCancellationRequested();
            if (_paused()) return;
            var key = $"{mountId}|{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}";
            if (!_warmed.TryAdd(key, 0)) continue;
            var rel = Path.GetRelativePath(root, f.FullName).Replace('\\', '/');
            try
            {
                var bytes = await WarmFileAsync(mountId, f, rel, ct);
                _log.LogInformation("Archive ready to open: {Path} ({Bytes} fetched ahead)", rel, ByteSize.Format(bytes));
                Warmed?.Invoke(rel, bytes);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _warmed.TryRemove(key, out _); // try again the next time the folder is opened
                _log.LogDebug("Archive warm-up of {Path} failed: {Error}", rel, e.Message);
            }
        }
    }

    private async Task<long> WarmFileAsync(string mountId, FileInfo file, string rel, CancellationToken ct)
    {
        var buffer = new byte[256 * 1024];
        long total = 0;
        await using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, useAsync: true);
        foreach (var (offset, length) in Ranges(fs.Length))
        {
            // Marked as read-ahead, so it is not counted as a program's own read (cache hit rate, game learning).
            using var reg = _attribution(mountId)?.Register(rel, offset, length);
            fs.Seek(offset, SeekOrigin.Begin);
            for (long left = length; left > 0;)
            {
                var n = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), ct);
                if (n == 0) break;
                left -= n;
                total += n;
            }
        }
        return total;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _queue.Writer.TryComplete();
    }
}
