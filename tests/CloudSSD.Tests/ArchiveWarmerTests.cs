using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudSSD.Tests;

public class ArchiveWarmerTests
{
    [Fact]
    public void Small_archives_are_fetched_whole_big_ones_end_first_then_start()
    {
        Assert.Empty(ArchiveWarmer.Ranges(0));
        Assert.Equal([(0L, 5000)], ArchiveWarmer.Ranges(5000));
        long big = 440 * ByteSize.MiB;
        // The end holds a zip's file list, which Windows reads first; the start is read to identify the file.
        Assert.Equal([(big - ArchiveWarmer.TailBytes, ArchiveWarmer.TailBytes), (0L, ArchiveWarmer.HeadBytes)], ArchiveWarmer.Ranges(big));
    }

    [Fact]
    public void Parser_reports_listed_folders_relative_to_the_drive_root()
    {
        var parser = new RcloneLogParser("m");
        var listed = new List<string>();
        parser.DirectoryListed += listed.Add;
        parser.ParseLine("2026/11/10 09:54:32.565110 DEBUG : /: Opendir: ");
        parser.ParseLine("2026/11/10 09:54:32.565110 DEBUG : /: >Opendir: errc=0, fh=0x0");
        parser.ParseLine("2026/11/10 09:54:40.000000 DEBUG : /ZIP File's: Opendir: ");
        parser.ParseLine("2026/11/10 09:54:41.000000 DEBUG : /File/Part 7.zip: Getattr: fh=0xFFFFFFFFFFFFFFFF");
        Assert.Equal(["", "ZIP File's"], listed);
    }

    [Fact]
    public void Quota_exceeded_is_classified_as_rate_limiting()
    {
        const string msg = "pacer: low level retry 1/10 (error googleapi: Error 403: Quota exceeded for quota metric 'Queries' and limit 'Previous quota: Requests per minute' of service 'drive.googleapis.com')";
        Assert.Equal(ProviderEventKind.RateLimited, RcloneLogParser.Classify(msg, ProviderEventKind.Retry));
    }

    [Fact]
    public async Task Listing_a_folder_fetches_each_archive_once_and_ignores_other_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "cpx_warm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Zips"));
        try
        {
            long bigSize = ArchiveWarmer.WholeFileLimit + 3 * ByteSize.MiB;
            await File.WriteAllBytesAsync(Path.Combine(root, "Zips", "small.zip"), new byte[70_000]);
            await using (var fs = File.Create(Path.Combine(root, "Zips", "big.7z"))) fs.SetLength(bigSize);
            await File.WriteAllBytesAsync(Path.Combine(root, "Zips", "video.mp4"), new byte[50_000]);

            var done = new TaskCompletionSource();
            var warmed = new Dictionary<string, long>();
            using var warmer = new ArchiveWarmer(_ => root, _ => null, () => false, NullLogger.Instance);
            warmer.Warmed += (path, bytes) =>
            {
                lock (warmed) { warmed[path] = warmed.GetValueOrDefault(path) + bytes; if (warmed.Count == 2) done.TrySetResult(); }
            };
            warmer.OnDirectoryListed("m", "Zips");
            warmer.OnDirectoryListed("m", "Zips"); // File Explorer lists a folder many times; only the first counts
            await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Task.Delay(300);

            lock (warmed)
            {
                Assert.Equal(70_000, warmed["Zips/small.zip"]);
                Assert.Equal(ArchiveWarmer.TailBytes + ArchiveWarmer.HeadBytes, warmed["Zips/big.7z"]);
                Assert.Equal(2, warmed.Count);
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
