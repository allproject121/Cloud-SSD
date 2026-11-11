using CloudSSD.App;
using CloudSSD.Core;
using Xunit;
namespace CloudSSD.Tests;
public class SmartOffloadTests
{
    [Fact]
    public void Auto_switch_starts_below_50_gb_free_unless_the_user_sets_another_limit()
    {
        Assert.Equal(50.0, new SmartOffloadSettings().MinFreeSpaceThresholdGB);
        Assert.True(new SmartOffloadSettings().Enabled);
        Assert.False(new SmartOffloadSettings().OffloadDownloadsFolder); // the user's own files move only when asked for
    }

    [Fact]
    public void From_downloads_only_big_old_finished_files_are_picked_biggest_first_and_no_more_than_needed()
    {
        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var old = now - TimeSpan.FromDays(30);
        long gb = ByteSize.GiB;
        var files = new[]
        {
            new SmartStorageOffloaderService.LocalFile(@"C:\D\movie.mkv", 4 * gb, old),
            new SmartStorageOffloaderService.LocalFile(@"C:\D\setup.exe", 2 * gb, old),
            new SmartStorageOffloaderService.LocalFile(@"C:\D\game.zip", 6 * gb, old),
            new SmartStorageOffloaderService.LocalFile(@"C:\D\new.iso", 9 * gb, now - TimeSpan.FromDays(2)),       // downloaded this week
            new SmartStorageOffloaderService.LocalFile(@"C:\D\notes.pdf", 5 * ByteSize.MiB, old),                  // too small to matter
            new SmartStorageOffloaderService.LocalFile(@"C:\D\big.zip.crdownload", 8 * gb, old),                   // still downloading
        };

        Assert.Empty(SmartStorageOffloaderService.PickDownloads(files, now, 0));
        Assert.Equal([@"C:\D\game.zip"], SmartStorageOffloaderService.PickDownloads(files, now, 5 * gb).Select(f => f.Path));
        Assert.Equal([@"C:\D\game.zip", @"C:\D\movie.mkv"], SmartStorageOffloaderService.PickDownloads(files, now, 7 * gb).Select(f => f.Path));
        Assert.Equal([@"C:\D\game.zip", @"C:\D\movie.mkv", @"C:\D\setup.exe"], SmartStorageOffloaderService.PickDownloads(files, now, 500 * gb).Select(f => f.Path));
    }

    [Fact]
    public void Temp_clean_up_removes_week_old_files_and_keeps_new_ones_open_ones_and_linked_folders()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cloudssd-tempclean-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "cloudssd-tempclean-outside-" + Guid.NewGuid().ToString("N"));
        var now = DateTime.UtcNow;
        var old = now - TimeSpan.FromDays(10);
        string Make(string root, string relative, DateTime time)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "12345");
            File.SetCreationTimeUtc(path, time);
            File.SetLastWriteTimeUtc(path, time);
            return path;
        }
        try
        {
            var oldFile = Make(dir, "old.tmp", old);
            var oldDeep = Make(dir, @"gone\deeper\old.bin", old);
            var fresh = Make(dir, @"kept\fresh.tmp", now - TimeSpan.FromDays(2));
            var inUse = Make(dir, "open.tmp", old);
            var linked = Make(outside, "precious.txt", old);
            Directory.SetCreationTimeUtc(Path.Combine(dir, "gone"), old);
            Directory.SetCreationTimeUtc(Path.Combine(dir, "gone", "deeper"), old);
            // A junction, because unlike a symbolic link it needs neither admin rights nor Developer Mode.
            using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(dir, "link")}\" \"{outside}\"")
                       { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
                mklink.WaitForExit();
            Assert.True(File.Exists(Path.Combine(dir, "link", "precious.txt")));

            TempCleanupResult result;
            using (new FileStream(inUse, FileMode.Open, FileAccess.Read, FileShare.None))
                result = TempCleanup.Clean(dir, now);

            Assert.Equal(2, result.Files);
            Assert.Equal(10, result.Bytes);
            Assert.False(File.Exists(oldFile));
            Assert.False(File.Exists(oldDeep));
            Assert.False(Directory.Exists(Path.Combine(dir, "gone"))); // emptied folders go too
            Assert.True(File.Exists(fresh));
            Assert.True(File.Exists(inUse));
            Assert.True(File.Exists(linked)); // never through a link into another folder
        }
        finally
        {
            // The junction first and on its own, so removing the test folder cannot reach through it.
            if (Directory.Exists(Path.Combine(dir, "link"))) Directory.Delete(Path.Combine(dir, "link"));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
        Assert.Equal(new TempCleanupResult(0, 0), TempCleanup.Clean(Path.Combine(dir, "missing"), now));
        Assert.EndsWith(@"\AppData\Local\Temp", TempCleanup.UserTempDirectory);
    }

    [Fact]
    public void A_game_is_being_played_when_an_emulator_is_in_front_or_an_app_fills_the_screen()
    {
        Assert.True(GameActivity.IsGameInFront("HD-Player", fillsScreen: false));  // BlueStacks in a window
        Assert.True(GameActivity.IsGameInFront("hd-player", fillsScreen: false));
        Assert.True(GameActivity.IsGameInFront("FreeFire", fillsScreen: true));
        Assert.False(GameActivity.IsGameInFront("chrome", fillsScreen: false));
        Assert.False(GameActivity.IsGameInFront("explorer", fillsScreen: true));   // the desktop itself
        Assert.False(GameActivity.IsGameInFront("LockApp", fillsScreen: true));    // lock screen
        Assert.False(GameActivity.IsGameInFront(null, fillsScreen: true));
    }

    [Fact]
    public void Game_mode_starts_once_per_game_and_ends_only_after_two_minutes_without_it()
    {
        var mode = new GameMode();
        var t = new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);

        Assert.Equal(GameMode.Step.None, mode.Tick(false, t));
        Assert.Equal(GameMode.Step.Started, mode.Tick(true, t));
        Assert.Equal(GameMode.Step.None, mode.Tick(true, t.AddSeconds(1)));          // still the same game
        Assert.Equal(GameMode.Step.None, mode.Tick(false, t.AddSeconds(60)));        // switched to another window for a moment
        Assert.True(mode.Active);
        Assert.Equal(GameMode.Step.None, mode.Tick(true, t.AddSeconds(90)));         // back in the game: no second start
        Assert.Equal(GameMode.Step.None, mode.Tick(false, t.AddSeconds(90 + 119)));
        Assert.Equal(GameMode.Step.Ended, mode.Tick(false, t.AddSeconds(90 + 120)));
        Assert.False(mode.Active);
        Assert.Equal(GameMode.Step.None, mode.Tick(false, t.AddSeconds(400)));
        Assert.Equal(GameMode.Step.Started, mode.Tick(true, t.AddSeconds(500)));     // the next game

        Assert.True(new MemorySettings().GameMode);
        Assert.Empty(new MemorySettings().CloseForGame);                              // apps are closed only when the user chose them
        Assert.True(new MemorySettings().ReopenAfterGame);
    }

    [Fact]
    public void Apps_are_closed_for_a_game_only_when_an_emulator_or_a_library_game_says_so()
    {
        Assert.True(GameActivity.IsEmulator("HD-Player"));
        Assert.True(GameActivity.IsEmulator("dnplayer"));
        Assert.False(GameActivity.IsEmulator("msedge"));          // a full-screen video is no reason to close apps
        Assert.False(GameActivity.IsEmulator("WindowsTerminal"));
        Assert.False(GameActivity.IsEmulator(null));

        var mode = new GameMode();
        Assert.Empty(mode.CloseApps(["no-such-app-cloudssd-test"], []));   // not running: nothing asked
        Assert.Empty(mode.Reopen());
        var (path, appId) = GameMode.LaunchInfo(Environment.ProcessId);
        Assert.Equal(Environment.ProcessPath, path, ignoreCase: true);
        Assert.Null(appId);                                                // the test host is no Store app
        Assert.Equal(1000, new GameModeResult(DateTime.Now, ["a"], 2000, 3000).FreedBytes);
        Assert.Equal(0, new GameModeResult(DateTime.Now, [], 3000, 2000).FreedBytes);
    }

    [Fact]
    public void During_a_game_only_uploads_are_slowed_and_downloads_keep_their_speed()
    {
        Assert.Equal("off", CloudSSD.Mount.RcloneMountService.BandwidthRate(0));
        Assert.Equal("1250000B", CloudSSD.Mount.RcloneMountService.BandwidthRate(10));
        Assert.Equal("125000B:off", CloudSSD.Mount.RcloneMountService.BandwidthRate(0, uploadMbps: 1));       // no normal limit
        Assert.Equal("125000B:1250000B", CloudSSD.Mount.RcloneMountService.BandwidthRate(10, uploadMbps: 1));
        Assert.Equal("62500B:62500B", CloudSSD.Mount.RcloneMountService.BandwidthRate(0.5, uploadMbps: 1));   // never faster than the normal limit
        Assert.Equal("1250000B", CloudSSD.Mount.RcloneMountService.BandwidthRate(10, uploadMbps: 0));         // 0 = do not slow
        Assert.Equal(1, new NetworkSettings().GameUploadLimitMbps);
    }

    [Fact]
    public void History_names_the_file_that_was_moved()
    {
        Assert.Equal("movie.mkv", SmartStorageOffloaderService.HistoryTitle(["movie.mkv"]));
        Assert.Equal("movie.mkv + 2 more", SmartStorageOffloaderService.HistoryTitle(["movie.mkv", "a.zip", "b.iso"]));
        Assert.Equal("Storage Saver Optimization", SmartStorageOffloaderService.HistoryTitle([]));
    }

    [Fact]
    public void Recycle_bin_clean_up_removes_only_what_was_deleted_over_a_month_ago()
    {
        var bin = Path.Combine(Path.GetTempPath(), "cloudssd-bin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bin);
        var now = DateTime.UtcNow;
        void Deleted(string id, DateTime when, bool folder)
        {
            File.WriteAllText(Path.Combine(bin, "$I" + id), "record");
            File.SetLastWriteTimeUtc(Path.Combine(bin, "$I" + id), when);
            if (folder)
            {
                Directory.CreateDirectory(Path.Combine(bin, "$R" + id, "sub"));
                File.WriteAllText(Path.Combine(bin, "$R" + id, "sub", "a.txt"), "1234567");
            }
            else File.WriteAllText(Path.Combine(bin, "$R" + id), "123");
        }
        try
        {
            Deleted("OLDFIL.txt", now - TimeSpan.FromDays(40), folder: false);
            Deleted("OLDDIR", now - TimeSpan.FromDays(31), folder: true);
            Deleted("NEWFIL.txt", now - TimeSpan.FromDays(29), folder: false);
            Assert.False(new SmartOffloadSettings().CleanRecycleBin); // the user's own deleted files go only when asked for

            var result = RecycleBinCleanup.Clean(bin, now);

            Assert.Equal(new TempCleanupResult(2, 10), result);
            Assert.False(File.Exists(Path.Combine(bin, "$ROLDFIL.txt")));
            Assert.False(File.Exists(Path.Combine(bin, "$IOLDFIL.txt")));
            Assert.False(Directory.Exists(Path.Combine(bin, "$ROLDDIR")));
            Assert.True(File.Exists(Path.Combine(bin, "$RNEWFIL.txt")));
            Assert.True(File.Exists(Path.Combine(bin, "$INEWFIL.txt")));
            Assert.Equal(new TempCleanupResult(0, 0), RecycleBinCleanup.Clean(Path.Combine(bin, "missing"), now));
        }
        finally
        {
            Directory.Delete(bin, true);
        }
    }

    [Fact]
    public void Space_list_opens_big_folders_until_it_names_the_ones_that_matter()
    {
        long gb = ByteSize.GiB;
        DiskUsage.Node Folder(string path, long bytes, params DiskUsage.Node[] children)
        {
            var n = new DiskUsage.Node(path, bytes);
            n.Children.AddRange(children);
            return n;
        }
        var root = Folder(@"C:\", 100 * gb,
            Folder(@"C:\Windows", 30 * gb),
            Folder(@"C:\Users", 60 * gb,
                Folder(@"C:\Users\me", 59 * gb,
                    Folder(@"C:\Users\me\Downloads", 40 * gb),
                    Folder(@"C:\Users\me\Videos", 15 * gb),
                    Folder(@"C:\Users\me\Documents", 3 * gb, Folder(@"C:\Users\me\Documents\Work", 2 * gb)))),
            new DiskUsage.Node(@"C:\pagefile.sys", 8 * gb));

        var rows = DiskUsage.Pick(root, rows: 10, minBytes: gb).Select(n => $"{n.Path}={n.Bytes / gb}").ToArray();

        // "Users" and "me" are opened (each over a twentieth of the drive); Documents (3 GB) is small enough to stay one line.
        Assert.Equal([@"C:\Users\me\Downloads=40", @"C:\Windows=30", @"C:\Users\me\Videos=15", @"C:\pagefile.sys=8", @"C:\Users\me\Documents=3",
            @"C:\(other files here)=2", @"C:\Users\(other files here)=1", @"C:\Users\me\(other files here)=1"], rows);
        Assert.Equal(2, DiskUsage.Pick(root, rows: 2, minBytes: gb).Count);
    }

    [Fact]
    public void Space_scan_adds_up_real_folders_and_does_not_follow_links()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cloudssd-scan-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "cloudssd-scan-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "a", "deep"));
            Directory.CreateDirectory(outside);
            File.WriteAllBytes(Path.Combine(dir, "a", "one.bin"), new byte[1000]);
            File.WriteAllBytes(Path.Combine(dir, "a", "deep", "two.bin"), new byte[2000]);
            File.WriteAllBytes(Path.Combine(dir, "top.bin"), new byte[300]);
            File.WriteAllBytes(Path.Combine(outside, "far.bin"), new byte[50000]);
            using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(dir, "link")}\" \"{outside}\"")
                       { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
                mklink.WaitForExit();

            var root = DiskUsage.Measure(new DirectoryInfo(dir), 0, CancellationToken.None);

            Assert.Equal(3300, root.Bytes);
            Assert.Equal(3000, root.Children.Single(c => c.Path.EndsWith(@"\a")).Bytes);
        }
        finally
        {
            if (Directory.Exists(Path.Combine(dir, "link"))) Directory.Delete(Path.Combine(dir, "link"));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [Fact]
    public void A_shortcut_is_left_where_a_moved_file_was()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cloudssd-lnk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var link = Path.Combine(dir, "movie.mkv.lnk");
            Assert.True(ShellShortcut.TryCreate(link, @"Z:\Offloaded_Files\Downloads\movie.mkv", "Moved to the cloud drive by Cloud-SSD"));
            Assert.True(new FileInfo(link).Length > 100);
            Assert.False(ShellShortcut.TryCreate(Path.Combine(dir, "no-such-folder", "x.lnk"), @"Z:\x", "x"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Only_a_folder_with_files_of_its_own_gives_the_auto_switch_something_to_move()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cloudssd-offload-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(SmartStorageOffloaderService.HasFiles(dir)); // missing folder
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, "sub", "inner.txt"), "x");
            Assert.False(SmartStorageOffloaderService.HasFiles(dir)); // files in subfolders are not moved, so they do not count
            File.WriteAllText(Path.Combine(dir, "photo.jpg"), "x");
            Assert.True(SmartStorageOffloaderService.HasFiles(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
