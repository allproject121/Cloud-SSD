using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.SimCloud;
using Xunit.Abstractions;

namespace CloudSSD.IntegrationTests;

/// <summary>
/// The real AppHost keeps exactly two drives for whatever accounts are connected: "Each Account" (a folder per
/// account) and "All Accounts" (everything joined). Accounts here are rclone http remotes on a SimCloud server.
/// </summary>
public sealed class StandardDrivesTests(ITestOutputHelper output)
{
    private static readonly Dataset D = Dataset.Create(Path.Combine(TestEnv.WorkRoot, "dataset"));

    [Fact]
    public async Task Two_drives_work_with_one_account_then_two_then_one_again()
    {
        var home = Path.Combine(TestEnv.WorkRoot, "home-standard-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(home);
        await using var server = new SimCloudServer(D.Root);
        var paths = new AppPaths(home);
        var settings = new AppSettings
        {
            FirstRunCompleted = true,
            Cache = new CacheSettings { CacheRoot = Path.Combine(home, "cache"), BudgetBytes = 2 * ByteSize.GiB, MinFreeSpaceBytes = 2 * ByteSize.GiB },
            Network = new NetworkSettings { TpsLimit = 0 },
        };
        settings.MobileBridge.Enabled = false;
        new JsonSettingsStore(paths.SettingsFile).Save(settings);
        string Account(string name) => $"[{name}]\ntype = http\nurl = {server.Url}\n\n";
        await File.WriteAllTextAsync(paths.RcloneConfigFile, Account("acct1"));

        await using var host = AppHost.Create(paths);
        await host.InitializeAsync();
        try
        {
            // One account: rclone refuses a union of one, so "All Accounts" must still come up (as an alias).
            await host.SyncStandardDrivesAsync();
            await AssertDrivesAsync(host, ["acct1"]);
            Assert.Contains("type = alias", await File.ReadAllTextAsync(paths.RcloneConfigFile));

            // A second account joins: both drives pick it up.
            await File.AppendAllTextAsync(paths.RcloneConfigFile, Account("acct2"));
            await host.SyncStandardDrivesAsync();
            await AssertDrivesAsync(host, ["acct1", "acct2"]);
            Assert.Contains("type = union", await File.ReadAllTextAsync(paths.RcloneConfigFile));

            // It is disconnected again: back to one account, still two working drives.
            await host.RcloneConfig.DeleteRemoteAsync(host.Rclone!, "acct2");
            await host.SyncStandardDrivesAsync();
            await AssertDrivesAsync(host, ["acct1"]);

            // No account left: no drives and no generated remotes.
            await host.RcloneConfig.DeleteRemoteAsync(host.Rclone!, "acct1");
            await host.SyncStandardDrivesAsync();
            Assert.Empty(host.Settings.Mounts);
            Assert.Empty(host.Mounts.Sessions);
            Assert.Empty(host.Remotes);
        }
        finally
        {
            await host.ShutdownAsync(keepMounts: false);
        }
    }

    private async Task AssertDrivesAsync(AppHost host, string[] accounts)
    {
        Assert.Equal([AppHost.SingleMountId, AppHost.CombinedMountId], host.Settings.Mounts.Select(m => m.Id).Order().ToArray());
        var all = host.Mounts.Get(AppHost.CombinedMountId);
        var each = host.Mounts.Get(AppHost.SingleMountId);
        Assert.NotNull(all);
        Assert.NotNull(each);
        output.WriteLine($"{accounts.Length} account(s): All Accounts {all!.DriveLetter}: {all.State}, Each Account {each!.DriveLetter}: {each.State}");
        Assert.Equal(MountState.Mounted, all.State);
        Assert.Equal(MountState.Mounted, each.State);
        // Accounts are listed once each, and the generated remotes behind the drives are not accounts.
        Assert.Equal(accounts, host.Remotes.Where(r => !AppHost.IsGeneratedType(r.Type)).Select(r => r.Name).Order().ToArray());
        Assert.Equal(accounts, host.AccountsBehind(AppHost.CombinedRemote).Order().ToArray());
        Assert.Equal(accounts, Directory.GetDirectories(each.Root).Select(d => Path.GetFileName(d)!).Order().ToArray());
        var expected = Bytes.ReadRange(D.Source("big.bin"), 5 * ByteSize.MiB, 4096);
        Assert.Equal(expected, Bytes.ReadRange(Path.Combine(all.Root, "big.bin"), 5 * ByteSize.MiB, 4096));
        foreach (var a in accounts)
            Assert.Equal(expected, Bytes.ReadRange(Path.Combine(each.Root, a, "big.bin"), 5 * ByteSize.MiB, 4096));
        await Task.CompletedTask;
    }
}
