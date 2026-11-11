using CloudSSD.App;
using CloudSSD.Core;
using Xunit;
namespace CloudSSD.Tests;
public class MemoryHealthTests
{
    private static MemorySnapshot Snapshot(double ramFreeGb = 6, double commitGb = 8, double commitLimitGb = 24, bool pageFile = true,
        bool? systemManaged = true, double? volumeFreeGb = 100, bool cacheShares = false) =>
        new((long)(12 * ByteSize.GiB), (long)(ramFreeGb * ByteSize.GiB), (long)(commitGb * ByteSize.GiB), (long)(commitLimitGb * ByteSize.GiB),
            pageFile ? [new PageFileInfo(@"C:\pagefile.sys", 12 * ByteSize.GiB, ByteSize.GiB, 2 * ByteSize.GiB)] : [],
            systemManaged, pageFile ? @"C:\" : null, volumeFreeGb is { } f ? (long)(f * ByteSize.GiB) : null, cacheShares, []);

    [Fact]
    public void Reads_real_memory_figures_on_any_windows_pc()
    {
        var s = MemoryHealth.Read(Path.GetTempPath());
        Assert.True(s.PhysicalTotalBytes > 0);
        Assert.InRange(s.PhysicalAvailableBytes, 1, s.PhysicalTotalBytes);
        Assert.InRange(s.CommitUsedBytes, 1, s.CommitLimitBytes);
        Assert.True(s.CommitLimitBytes >= s.PhysicalTotalBytes, "commit limit is RAM + page files");
        Assert.InRange(s.TopApps.Count, 1, 8);
        Assert.All(s.TopApps, a => Assert.True(a.WorkingSetBytes > 0 && a.Processes > 0));
        Assert.All(s.TopApps, a => Assert.False(MemoryHealth.IsProtected(a.Name), $"{a.Name} belongs to Windows or Cloud-SSD and must not be listed"));
        Assert.True(s.SystemBytes > 0 && s.SystemProcesses > 0, "Windows' own processes are added up separately");
        Assert.Equal(s.TopApps.OrderByDescending(a => a.WorkingSetBytes), s.TopApps);
        foreach (var f in s.PageFiles)
        {
            Assert.True(Path.IsPathFullyQualified(f.Path), f.Path);
            Assert.True(f.AllocatedBytes > 0 && f.UsedBytes <= f.AllocatedBytes);
        }
        Assert.NotEmpty(MemoryHealth.Advise(s));
    }

    [Fact]
    public void Quick_ram_reading_agrees_with_the_full_one()
    {
        var (total, available) = MemoryHealth.ReadRam();
        Assert.Equal(MemoryHealth.Read(topApps: 0).PhysicalTotalBytes, total);
        Assert.InRange(available, 1, total);
    }

    [Theory]
    [InlineData("svchost")]
    [InlineData("explorer")]
    [InlineData("rclone")]
    [InlineData("Cloud-SSD")]
    [InlineData("Memory Compression")]
    public void Windows_and_cloud_ssd_processes_are_never_closed(string name)
    {
        Assert.True(MemoryHealth.IsProtected(name));
        Assert.Equal(0, MemoryHealth.RequestClose(name));
    }

    [Fact]
    public void The_running_app_itself_is_protected_and_ordinary_apps_are_not()
    {
        Assert.True(MemoryHealth.IsProtected(System.Diagnostics.Process.GetCurrentProcess().ProcessName));
        Assert.False(MemoryHealth.IsProtected("notepad"));
        Assert.Equal(0, MemoryHealth.RequestClose("no-such-app-cloudssd-test"));
    }

    [Fact]
    public void Auto_mode_acts_only_when_ram_is_low_and_it_has_watched_and_rested_long_enough()
    {
        long total = 10 * ByteSize.GiB, low = 2 * ByteSize.GiB, fine = 4 * ByteSize.GiB;
        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var longAgo = now - TimeSpan.FromHours(1);

        var rest = MemoryRelief.Cooldown;

        Assert.True(MemoryRelief.IsDue(total, low, now, longAgo, DateTime.MinValue, rest));
        Assert.False(MemoryRelief.IsDue(total, fine, now, longAgo, DateTime.MinValue, rest));                      // 40% free
        Assert.False(MemoryRelief.IsDue(total, 3 * ByteSize.GiB, now, longAgo, DateTime.MinValue, rest));         // exactly 30% free
        Assert.False(MemoryRelief.IsDue(total, low, now, now - TimeSpan.FromSeconds(30), DateTime.MinValue, rest)); // just started
        Assert.True(MemoryRelief.IsDue(total, low, now, now - TimeSpan.FromMinutes(1), DateTime.MinValue, rest));   // watched a minute
        Assert.False(MemoryRelief.IsDue(total, low, now, longAgo, now - TimeSpan.FromMinutes(2), rest));           // ran 2 min ago
        Assert.True(MemoryRelief.IsDue(total, low, now, longAgo, now - rest, rest));
        Assert.False(MemoryRelief.IsDue(total, low, now, longAgo, now - rest + TimeSpan.FromSeconds(1), rest));    // one second early
    }

    [Fact]
    public void Auto_mode_runs_again_every_ten_minutes_while_ram_stays_low()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), MemoryRelief.Cooldown);
        Assert.Equal(TimeSpan.FromMinutes(1), MemoryRelief.WarmUp);

        long total = 10 * ByteSize.GiB;
        Assert.False(new ReliefResult(DateTime.Now, 5, 1 * ByteSize.GiB, 2 * ByteSize.GiB, total).ReachedTarget); // 20% free: still low
        Assert.True(new ReliefResult(DateTime.Now, 5, 2 * ByteSize.GiB, 3 * ByteSize.GiB, total).ReachedTarget);  // 30% free
    }

    [Fact]
    public void Moving_a_process_out_of_ram_works_on_our_own_process_and_fails_quietly_on_a_missing_one()
    {
        Assert.True(MemoryHealth.Trim(Environment.ProcessId));
        Assert.False(MemoryHealth.Trim(-1));
    }

    [Fact]
    public void Background_trim_leaves_skipped_and_protected_apps_alone()
    {
        // Everything is either skipped by name or too small to matter, so nothing may be touched.
        var everything = System.Diagnostics.Process.GetProcesses().Select(p => p.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, MemoryHealth.TrimBackgroundApps(everything));
        Assert.Equal(0, MemoryHealth.TrimBackgroundApps(new HashSet<string>(), minWorkingSetBytes: long.MaxValue));
    }

    [Fact]
    public void Healthy_pc_gets_one_ok_line()
    {
        var advice = MemoryHealth.Advise(Snapshot());
        Assert.Equal("ok", Assert.Single(advice).Tone);
    }

    [Fact]
    public void Ram_between_low_and_almost_full_is_called_low_never_healthy()
    {
        // 3 of 12 GiB free = 25%: under the 30% where auto mode acts, so the advice must not say "healthy".
        var advice = Assert.Single(MemoryHealth.Advise(Snapshot(ramFreeGb: 3)));
        Assert.Equal("accent", advice.Tone);
        Assert.Contains("getting low", advice.Text);
        Assert.Contains("25", advice.Text);
        Assert.Equal("ok", Assert.Single(MemoryHealth.Advise(Snapshot(ramFreeGb: 3.7))).Tone); // 31% free
    }

    [Fact]
    public void Advice_keeps_its_sentence_and_numbers_apart_so_the_sentence_can_be_translated()
    {
        var advice = Assert.Single(MemoryHealth.Advise(Snapshot()));
        Assert.Equal("Memory looks healthy: {0} of RAM is free.", advice.Format);
        Assert.Equal("Memory looks healthy: 6.00 GB of RAM is free.", advice.Text);
    }

    [Theory]
    [InlineData(865L * 1024 * 1024, "865 MB")]
    [InlineData(7L * 1024 * 1024 * 1024 + 322122547, "7.30 GB")]
    [InlineData(21L * 1024 * 1024 * 1024 + 1009317315, "21.9 GB")]
    public void Sizes_read_like_task_manager(long bytes, string expected) =>
        Assert.Equal(expected, MemoryHealth.Size(bytes).Replace(',', '.'));

    [Fact]
    public void Almost_full_ram_warns_and_says_to_close_apps()
    {
        var advice = Assert.Single(MemoryHealth.Advise(Snapshot(ramFreeGb: 0.7)));
        Assert.Equal("warn", advice.Tone);
        Assert.Contains("Close apps", advice.Text);
    }

    [Fact]
    public void Commit_near_limit_is_an_error_and_blames_a_fixed_page_file_only_when_fixed()
    {
        var managed = Assert.Single(MemoryHealth.Advise(Snapshot(commitGb: 23)));
        Assert.Equal("error", managed.Tone);
        Assert.DoesNotContain("fixed size", managed.Text);

        var custom = Assert.Single(MemoryHealth.Advise(Snapshot(commitGb: 23, systemManaged: false)));
        Assert.Contains("fixed size", custom.Text);
    }

    [Fact]
    public void Missing_page_file_is_reported_first()
    {
        var advice = MemoryHealth.Advise(Snapshot(ramFreeGb: 0.5, pageFile: false, volumeFreeGb: null));
        Assert.Equal(["error", "warn"], advice.Select(a => a.Tone));
        Assert.Contains("no page file", advice[0].Text);
    }

    [Fact]
    public void Low_space_on_the_page_file_drive_warns_and_mentions_the_cache_only_when_it_shares_the_drive()
    {
        var alone = Assert.Single(MemoryHealth.Advise(Snapshot(volumeFreeGb: 8)));
        Assert.Equal("warn", alone.Tone);
        Assert.DoesNotContain("cache", alone.Text);

        var shared = Assert.Single(MemoryHealth.Advise(Snapshot(volumeFreeGb: 8, cacheShares: true)));
        Assert.Contains("cache", shared.Text);
    }

    [Theory]
    [InlineData(@"?:\pagefile.sys", true)]
    [InlineData(@"C:\pagefile.sys 0 0", true)]
    [InlineData(@"C:\pagefile.sys 8192 16384", false)]
    public void Page_file_setting_tells_windows_sized_from_fixed(string entry, bool expected) =>
        Assert.Equal(expected, MemoryHealth.IsSystemManaged([entry]));

    [Fact]
    public void Page_file_setting_is_unknown_when_missing_or_empty()
    {
        Assert.Null(MemoryHealth.IsSystemManaged(null));
        Assert.Null(MemoryHealth.IsSystemManaged([]));
        Assert.Null(MemoryHealth.IsSystemManaged([""]));
    }

    [Fact]
    public void Nt_page_file_paths_become_normal_paths() =>
        Assert.Equal(@"C:\pagefile.sys", MemoryHealth.NormalizePath(@"\??\C:\pagefile.sys"));
}
