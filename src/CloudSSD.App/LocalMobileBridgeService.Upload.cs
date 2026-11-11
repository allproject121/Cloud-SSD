using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using CloudSSD.Core;
using Microsoft.Extensions.Logging;

namespace CloudSSD.App;

/// <summary>
/// Uploads that continue where they stopped. The phone picks an upload id and sends the file with "offset" and
/// "total"; what arrived is kept in a staging folder on the PC (never inside a cloud drive, where rclone would upload
/// a half file). When the last byte is in, the file is put in place under a temporary name and renamed, so the cloud
/// only ever sees the finished file. /api/upload/status tells the phone how much the PC already has.
/// </summary>
public sealed partial class LocalMobileBridgeService
{
    private string StagingDir => Path.Combine(_host.Paths.DataRoot, "incoming");
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _uploadLocks = new();
    private DateTime _lastStagingClean = DateTime.MinValue;

    /// <summary>32 lowercase hex characters: chosen by the phone, the same for every retry of one file.</summary>
    internal static bool IsUploadId(string? s) => s is { Length: 32 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed class StagingRecord
    {
        public string Route { get; set; } = "";
        public string Folder { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Overwrite { get; set; }
        public long Total { get; set; }
        public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>The result of one request of a resumable upload: either more is needed (<see cref="Offset"/>) or the file is in place.</summary>
    private sealed record ResumeResult(bool Complete, long Offset, string? FinalPath, long Size, double SpeedMBps);

    private static long? QueryLong(Dictionary<string, string> q, string key) =>
        long.TryParse(q.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>How many bytes of an upload the PC already has (0 for an id it does not know).</summary>
    private object UploadStatus(Dictionary<string, string> q)
    {
        var id = q.GetValueOrDefault("uploadId");
        if (!IsUploadId(id)) throw new FsException(400, "uploadId must be 32 lowercase hex characters.");
        if (DateTime.UtcNow - _lastStagingClean > TimeSpan.FromHours(1)) CleanStaging(TimeSpan.FromHours(72));
        var part = new FileInfo(Path.Combine(StagingDir, id + ".part"));
        var rec = ReadRecord(id!);
        return rec != null && part.Exists
            ? new { uploadId = id, offset = part.Length, total = rec.Total }
            : (object)new { uploadId = id, offset = 0L };
    }

    private StagingRecord? ReadRecord(string id)
    {
        try
        {
            var path = Path.Combine(StagingDir, id + ".json");
            return File.Exists(path) ? JsonSerializer.Deserialize<StagingRecord>(File.ReadAllText(path), JsonOpts) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private void DropStaging(string id)
    {
        foreach (var ext in new[] { ".part", ".json" })
            try { File.Delete(Path.Combine(StagingDir, id + ext)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Removes uploads that were never finished (older than <paramref name="maxAge"/>).</summary>
    internal void CleanStaging(TimeSpan maxAge)
    {
        _lastStagingClean = DateTime.UtcNow;
        try
        {
            if (!Directory.Exists(StagingDir)) return;
            foreach (var f in new DirectoryInfo(StagingDir).EnumerateFiles())
                if (DateTime.UtcNow - f.LastWriteTimeUtc > maxAge)
                    try { f.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogDebug(e, "Cleaning upload staging failed"); }
    }

    /// <summary>
    /// Receives one request of a resumable upload into <paramref name="folder"/>/<paramref name="name"/>.
    /// A different file under the same id starts over; an offset the PC does not have yet gets 409 with the real one.
    /// </summary>
    private async Task<ResumeResult> ReceiveResumableAsync(string id, string route, string folder, string name, bool overwrite,
        DateTime? modifiedUtc, long offset, long total, string spaceCheckedOn, Dictionary<string, string> headers, NetworkStream stream,
        string clientIp, CancellationToken ct)
    {
        if (total < 0 || offset < 0 || offset > total) throw new FsException(400, "offset and total do not fit together.");
        long contentLength = -1;
        if (headers.TryGetValue("Content-Length", out var clStr) && !long.TryParse(clStr, out contentLength)) contentLength = -1;
        bool chunked = headers.TryGetValue("Transfer-Encoding", out var te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase);
        if (contentLength < 0 && !chunked) throw new FsException(411, "Upload needs a Content-Length or chunked transfer encoding.");
        if (contentLength > total - offset) throw new FsException(400, "More data than the file's size.");

        var gate = _uploadLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(30), ct)) throw new FsException(409, "This upload is already being received.");
        try
        {
            Directory.CreateDirectory(StagingDir);
            var partPath = Path.Combine(StagingDir, id + ".part");
            var rec = ReadRecord(id);
            bool same = rec != null && rec.Route == route && string.Equals(rec.Folder, folder, StringComparison.OrdinalIgnoreCase)
                        && rec.Name == name && rec.Total == total && rec.Overwrite == overwrite;
            if (!same)
            {
                DropStaging(id);
                if (offset > 0) throw new FsOffsetException(0, "The PC has nothing of this upload yet. Start again from the beginning.");
                rec = new StagingRecord { Route = route, Folder = folder, Name = name, Overwrite = overwrite, Total = total };
                File.WriteAllText(Path.Combine(StagingDir, id + ".json"), JsonSerializer.Serialize(rec, JsonOpts));
            }

            long have = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            if (offset > have) throw new FsOffsetException(have, $"The PC has {have} bytes of this upload; continue from there.");

            // The rest must fit both where it waits (the staging folder) and where it ends up, minus the usual reserve.
            long need = total - offset;
            foreach (var place in new[] { StagingDir, spaceCheckedOn })
            {
                long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(place))!).AvailableFreeSpace;
                if (need > free - CacheReserveBytes)
                    throw new FsException(507, $"Not enough free space on the PC ({ByteSize.Format(Math.Max(0, free))} free, 10 GB is kept in reserve).");
            }

            long saved = 0;
            bool bodyComplete;
            var sw = Stopwatch.StartNew();
            await using (var file = new FileStream(partPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, 64 * 1024, useAsync: true))
            {
                file.SetLength(offset); // a retry from an earlier point drops what came after it
                file.Seek(offset, SeekOrigin.Begin);
                var body = chunked ? (Stream)new ChunkedBodyStream(stream) : stream;
                long limit = chunked ? need : contentLength;
                var buffer = new byte[64 * 1024];
                while (saved < limit)
                {
                    int read;
                    try { read = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - saved)), ct); }
                    catch (Exception e) when (e is IOException or InvalidDataException) { break; }
                    if (read == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    saved += read;
                    TrackBytes(read);
                }
                bodyComplete = chunked ? ((ChunkedBodyStream)body).Finished : saved == contentLength;
            }
            File.SetLastWriteTimeUtc(partPath, DateTime.UtcNow); // keeps it away from the staging clean-up while it is in use
            long nowHave = offset + saved;
            double speed = saved / (1024.0 * 1024.0) / Math.Max(0.01, sw.Elapsed.TotalSeconds);

            if (nowHave < total)
            {
                if (!bodyComplete)
                {
                    RecordTransfer(name, Path.Combine(folder, name), nowHave, "upload", ResolveDeviceName(clientIp), clientIp, speed, "Interrupted");
                    throw new FsOffsetException(nowHave, "The upload was cut off; it continues from where it stopped.", 400);
                }
                return new ResumeResult(false, nowHave, null, nowHave, speed);
            }

            // Complete: into the destination folder under a hidden name first, then renamed in one step.
            Directory.CreateDirectory(folder);
            var finalName = overwrite ? name : FreeName(folder, name);
            var dest = Path.Combine(folder, finalName);
            var temp = Path.Combine(folder, $"{finalName}.{Guid.NewGuid():N}"[..Math.Min(finalName.Length + 9, 200)] + PartSuffix);
            try
            {
                File.Move(partPath, temp);
                if (modifiedUtc is { } m) File.SetLastWriteTimeUtc(temp, m);
                File.Move(temp, dest, overwrite);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Move(temp, partPath, true); } catch (IOException) { }
                throw;
            }
            DropStaging(id);
            _uploadLocks.TryRemove(id, out _);
            FileTransferred?.Invoke(finalName, total);
            RecordTransfer(finalName, dest, total, "upload", ResolveDeviceName(clientIp), clientIp, speed, "Completed");
            return new ResumeResult(true, total, dest, total, speed);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>/api/upload with an upload id: the phone's "Send to PC", continuing after a broken connection.</summary>
    private async Task HandleResumableUploadAsync(Dictionary<string, string> q, Dictionary<string, string> headers, NetworkStream stream, string clientIp, CancellationToken ct)
    {
        try
        {
            var id = q.GetValueOrDefault("uploadId");
            if (!IsUploadId(id)) throw new FsException(400, "uploadId must be 32 lowercase hex characters.");
            if (QueryLong(q, "total") is not { } total) throw new FsException(400, "A resumable upload needs total.");
            var folder = UploadDirectory;
            Directory.CreateDirectory(folder);
            var r = await ReceiveResumableAsync(id!, "upload", folder, SafeUploadName(q.GetValueOrDefault("filename")), false, ModifiedTime(q),
                QueryLong(q, "offset") ?? 0, total, folder, headers, stream, clientIp, ct);
            if (!r.Complete)
                await SendJsonAsync(stream, 200, "OK", new { success = true, complete = false, offset = r.Offset }, ct);
            else
                await SendJsonAsync(stream, 200, "OK", new
                {
                    success = true, complete = true, filename = Path.GetFileName(r.FinalPath), path = r.FinalPath, size = r.Size,
                    speedMBps = Math.Round(r.SpeedMBps, 1)
                }, ct);
        }
        catch (Exception e) when (e is FsException or IOException or UnauthorizedAccessException)
        {
            await DrainBrieflyAsync(stream, ct);
            await SendFsErrorAsync(stream, e, ct);
        }
    }
}
