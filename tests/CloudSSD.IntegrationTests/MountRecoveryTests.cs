using System.Collections.Concurrent;
using CloudSSD.Core;
using CloudSSD.Mount;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace CloudSSD.IntegrationTests;

public sealed class MountRecoveryTests(ITestOutputHelper output)
{
    private static readonly Dataset D = Dataset.Create(Path.Combine(TestEnv.WorkRoot, "dataset"));

    /// <summary>
    /// Regression: a start that failed left its automatic retry running; connecting the drive again then created a
    /// second session, and the two rclone processes fought over the drive (the old one kept reporting "Failed").
    /// </summary>
    [Fact]
    public async Task Connecting_again_after_a_failed_start_leaves_one_healthy_mount()
    {
        var rclone = await TestEnv.RcloneAsync();
        var home = Path.Combine(TestEnv.WorkRoot, "home-recover");
        Directory.CreateDirectory(home);
        var paths = new AppPaths(home);
        using var lf = TestEnv.Logging(paths.LogDirectory);
        await using var server = new CloudSSD.SimCloud.SimCloudServer(D.Root);
        var svc = new RcloneMountService(paths, new RcloneConfigService(paths, lf.CreateLogger<RcloneConfigService>()), lf.CreateLogger<RcloneMountService>());
        svc.SetRclone(rclone);
        var statuses = new ConcurrentQueue<MountStatus>();
        svc.StatusChanged += statuses.Enqueue;
        var cs = new CacheSettings { PollInterval = TimeSpan.FromSeconds(60), MinFreeSpaceBytes = 2 * ByteSize.GiB, BudgetBytes = 4 * ByteSize.GiB };
        var cacheDir = Path.Combine(TestEnv.WorkRoot, "cache-recover-" + Guid.NewGuid().ToString("N")[..10]);
        MountRequest Request(MountDefinition d) => new(d, cs, new NetworkSettings { TpsLimit = 0 }, cacheDir, cs.BudgetBytes, true, "http");
        try
        {
            // 1. A start that fails at once (unknown remote). The service keeps retrying that session in the background.
            var bad = new MountDefinition { Id = "recover", DisplayName = "recover", RemoteName = "no-such-remote" };
            await Assert.ThrowsAnyAsync<Exception>(() => svc.MountAsync(Request(bad)));

            // 2. The user (or the phone) presses Connect again, this time with a working drive.
            var good = new MountDefinition
            {
                Id = "recover", DisplayName = "recover", RemoteName = ":http",
                ExtraFlags = new() { ["http-url"] = server.Url, ["low-level-retries"] = "3", ["contimeout"] = "5s" }
            };
            var session = await svc.MountAsync(Request(good));
            Assert.Equal(MountState.Mounted, session.State);
            var pid = session.ProcessId;
            while (statuses.TryDequeue(out _)) { }

            // 3. Well past the failed session's retry delays (2 s, then 4 s): it must have given up.
            await Task.Delay(TimeSpan.FromSeconds(9));
            foreach (var st in statuses) output.WriteLine($"{st.State}: {st.Message}");
            Assert.Empty(statuses);
            Assert.Same(session, svc.Get("recover"));
            Assert.Equal(MountState.Mounted, session.State);
            Assert.Equal(pid, session.ProcessId);
            Assert.Equal(0, session.RestartCount);
            Assert.Equal(Bytes.ReadRange(D.Source("big.bin"), 0, 4096), Bytes.ReadRange(Path.Combine(session.Root, "big.bin"), 0, 4096));
        }
        finally
        {
            try { await svc.UnmountAllAsync(TimeSpan.FromSeconds(10)); } catch (Exception) { }
            await svc.DisposeAsync();
        }
    }
}
