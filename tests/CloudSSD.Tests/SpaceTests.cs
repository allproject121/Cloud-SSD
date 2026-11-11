using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CloudSSD.App;
using CloudSSD.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>Space &amp; RAM: the live map of the disks, programs, startup apps, clean-ups, protected places and the phone's side.</summary>
public class SpaceTests
{
    private static string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "cssd_space_" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Write(string path, long bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var f = File.Create(path);
        f.SetLength(bytes);
    }

    private const long MiB = 1024 * 1024;

    [Fact]
    public void Index_measures_folders_and_follows_changes()
    {
        var dir = NewDir();
        try
        {
            Write(Path.Combine(dir, "a", "big.bin"), 2 * MiB);
            Write(Path.Combine(dir, "a", "b", "small.bin"), 1000);
            Write(Path.Combine(dir, "c", "x.bin"), 3 * MiB / 2);
            using var index = new SpaceIndex(Path.Combine(dir, "..", Path.GetFileName(dir) + ".json"), () => [dir]);
            index.FullScan();

            Assert.True(index.Ready);
            Assert.Equal(2 * MiB + 1000 + 3 * MiB / 2, index.Total(dir));
            Assert.Equal(2 * MiB + 1000, index.SizeOf(Path.Combine(dir, "a"))!.Value.Bytes);
            Assert.Null(index.SizeOf(Path.Combine(dir, "a", "b")));            // too small to be remembered on its own
            var list = index.List(dir);
            Assert.Equal(["a", "c"], list.Select(i => i.Name));                 // biggest first
            Assert.Equal(1000, index.List(Path.Combine(dir, "a")).Single(i => i.Name == "b").Bytes);   // small folders measured when opened

            // A new file: only that folder is measured again, and the totals above it follow.
            Write(Path.Combine(dir, "a", "more.bin"), MiB);
            index.Remeasure(Path.Combine(dir, "a"));
            Assert.Equal(3 * MiB + 1000, index.SizeOf(Path.Combine(dir, "a"))!.Value.Bytes);
            Assert.Equal(3 * MiB + 1000 + 3 * MiB / 2, index.Total(dir));

            // A deleted folder disappears from the map and from the total.
            Directory.Delete(Path.Combine(dir, "c"), true);
            index.Remeasure(Path.Combine(dir, "c"));
            Assert.Null(index.SizeOf(Path.Combine(dir, "c")));
            Assert.Equal(3 * MiB + 1000, index.Total(dir));
        }
        finally { Directory.Delete(dir, true); File.Delete(dir + ".json"); }
    }

    [Fact]
    public void Index_keeps_big_files_and_its_last_scan()
    {
        var dir = NewDir();
        var snap = dir + ".json";
        try
        {
            Write(Path.Combine(dir, "movies", "film.mkv"), SpaceIndex.KeepFileBytes + 10);
            using (var index = new SpaceIndex(snap, () => [dir]))
            {
                index.FullScan();
                Assert.Equal(Path.Combine(dir, "movies", "film.mkv"), index.BigFiles().Single().Path);
            }
            // After a restart the saved map is there at once, before a new scan.
            using var again = new SpaceIndex(snap, () => [dir]);
            again.Start(TimeSpan.FromHours(1));
            Assert.False(again.Ready);
            Assert.NotNull(again.ScannedAt);
            Assert.Equal(SpaceIndex.KeepFileBytes + 10, again.SizeOf(Path.Combine(dir, "movies"))!.Value.Bytes);
        }
        finally { Directory.Delete(dir, true); File.Delete(snap); }
    }

    [Fact]
    public void Installed_programs_are_read_like_Windows_lists_them()
    {
        object? Get(Dictionary<string, object> d, string n) => d.TryGetValue(n, out var v) ? v : null;
        var app = new Dictionary<string, object> { ["DisplayName"] = "Game X", ["Publisher"] = "Studio", ["EstimatedSize"] = 2048, ["InstallDate"] = "20260115", ["UninstallString"] = "\"C:\\Games\\X\\unins000.exe\"" };
        var p = InstalledPrograms.Parse("lm64|X", n => Get(app, n))!;
        Assert.Equal(2048 * 1024, p.Bytes);
        Assert.Equal(new DateTime(2026, 1, 15), p.Installed!.Value.ToLocalTime().Date);
        Assert.Null(InstalledPrograms.Parse("u", n => Get(new() { ["DisplayName"] = "KB123", ["ReleaseType"] = "Security Update" }, n)));
        Assert.Null(InstalledPrograms.Parse("s", n => Get(new() { ["DisplayName"] = "Part", ["SystemComponent"] = 1 }, n)));
        Assert.Null(InstalledPrograms.Parse("e", n => Get(new() { ["Publisher"] = "No name" }, n)));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\X\\unins000.exe\" /S", "C:\\Program Files\\X\\unins000.exe", "/S")]
    [InlineData("MsiExec.exe /X{1234}", "MsiExec.exe", "/X{1234}")]
    [InlineData("C:\\Tools\\un install.exe --remove", "C:\\Tools\\un install.exe", "--remove")]
    public void Uninstall_commands_are_split_into_program_and_arguments(string command, string exe, string args) =>
        Assert.Equal((exe, args), InstalledPrograms.SplitCommand(command));

    [Fact]
    public void Startup_apps_switch_like_Task_Manager()
    {
        var path = @"Software\CloudSSD-Tests\" + Guid.NewGuid().ToString("N");
        using var cu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        try
        {
            using (var run = cu.CreateSubKey(path + @"\Run")) run.SetValue("Helper", "C:\\helper.exe /min");
            var entry = StartupApps.ReadRun(cu, "cu-run", "This user", true, path + @"\Run", path + @"\Approved").Single();
            Assert.True(entry.Enabled);                                         // no flag = on
            StartupApps.SetFlag(cu, path + @"\Approved", "Helper", enabled: false);
            Assert.False(StartupApps.ReadRun(cu, "cu-run", "This user", true, path + @"\Run", path + @"\Approved").Single().Enabled);
            StartupApps.SetFlag(cu, path + @"\Approved", "Helper", enabled: true);
            Assert.True(StartupApps.ReadRun(cu, "cu-run", "This user", true, path + @"\Run", path + @"\Approved").Single().Enabled);
            using var run2 = cu.OpenSubKey(path + @"\Run");
            Assert.Equal("C:\\helper.exe /min", run2!.GetValue("Helper"));    // the entry itself is never touched
        }
        finally { cu.DeleteSubKeyTree(@"Software\CloudSSD-Tests", throwOnMissingSubKey: false); }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[] { 2, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 6, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 3, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 7, 0, 0, 0 }, false)]
    public void Startup_flags_are_read(byte[]? flag, bool enabled) => Assert.Equal(enabled, StartupApps.IsEnabled(flag));

    [Fact]
    public async Task Windows_programs_and_Cloud_SSD_data_are_protected()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cssd_space_host_" + Guid.NewGuid().ToString("N")[..8]);
        await using var host = AppHost.Create(new AppPaths(temp));
        try
        {
            var s = host.Space;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Assert.True(s.IsProtected(@"C:\"));
            Assert.True(s.IsProtected(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
            Assert.True(s.IsProtected(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "x.dll")));
            Assert.True(s.IsProtected(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Some App")));
            Assert.True(s.IsProtected(@"C:\pagefile.sys"));
            Assert.True(s.IsProtected(profile));
            Assert.True(s.IsProtected(Path.Combine(profile, "AppData")));
            Assert.True(s.IsProtected(Path.Combine(profile, "Downloads")));
            Assert.True(s.IsProtected(Path.Combine(temp, "cache", "x")));          // Cloud-SSD's own data
            Assert.False(s.IsProtected(Path.Combine(profile, "Downloads", "setup.exe")));
            Assert.False(s.IsProtected(Path.Combine(profile, "Videos", "old")));
            Assert.True(SpaceService.IsUserCode(Path.Combine(profile, "Downloads", "Project's", "app", "a.cs")));
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    [Fact]
    public async Task Deleting_and_cleaning_free_space_but_refuse_protected_places()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cssd_space_host_" + Guid.NewGuid().ToString("N")[..8]);
        await using var host = AppHost.Create(new AppPaths(temp));
        var work = NewDir();
        try
        {
            var f = Path.Combine(work, "old.iso");
            Write(f, 4 * MiB);
            var r = await host.Space.ActionAsync(SpaceActionKinds.Delete, f);
            Assert.True(r.Ok, r.Message);
            Assert.False(File.Exists(f));
            var folder = Path.Combine(work, "folder");
            Write(Path.Combine(folder, "a", "b.txt"), 10);
            File.SetAttributes(Path.Combine(folder, "a", "b.txt"), FileAttributes.ReadOnly);
            Assert.True((await host.Space.ActionAsync(SpaceActionKinds.Delete, folder)).Ok);
            Assert.False(Directory.Exists(folder));

            var refused = await host.Space.ActionAsync(SpaceActionKinds.Delete, Path.Combine(temp, "settings.json"));
            Assert.False(refused.Ok);
            Assert.False((await host.Space.ActionAsync(SpaceActionKinds.Delete, Path.Combine(work, "gone.txt"))).Ok);
            Assert.False((await host.Space.ActionAsync("format-c", "C:\\")).Ok);
        }
        finally { Directory.Delete(work, true); try { Directory.Delete(temp, true); } catch { } }
    }

    [Fact]
    public async Task Duplicates_are_found_by_content()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cssd_space_host_" + Guid.NewGuid().ToString("N")[..8]);
        await using var host = AppHost.Create(new AppPaths(temp));
        var work = NewDir();
        try
        {
            var data = new byte[2 * MiB];
            new Random(1).NextBytes(data);
            File.WriteAllBytes(Path.Combine(work, "photo.jpg"), data);
            Directory.CreateDirectory(Path.Combine(work, "copy"));
            File.WriteAllBytes(Path.Combine(work, "copy", "photo (1).jpg"), data);
            var other = (byte[])data.Clone();
            other[^1] ^= 1;
            File.WriteAllBytes(Path.Combine(work, "same size.jpg"), other);       // same size, different content

            var groups = host.Space.FindDuplicates(TimeSpan.FromSeconds(30), [work]);
            var g = Assert.Single(groups);
            Assert.Equal(2, g.Count);
            Assert.DoesNotContain(g, i => i.Name == "same size.jpg");
        }
        finally { Directory.Delete(work, true); try { Directory.Delete(temp, true); } catch { } }
    }

    // ---- the phone's side ----------------------------------------------------------------------------------------------

    [Fact]
    public void Phone_reports_are_kept_and_commands_wait_for_the_phone()
    {
        var dir = NewDir();
        try
        {
            var store = new PhoneSpaceStore(dir);
            Assert.Null(store.Enqueue("p1", SpaceActionKinds.Delete, "/sdcard/a.mp4"));     // nothing known about the phone yet
            store.SaveReport("p1", "Test phone", allowControl: false, new JsonObject { ["summary"] = new JsonObject { ["device"] = "Test phone" } });
            Assert.Null(store.Enqueue("p1", SpaceActionKinds.Delete, "/sdcard/a.mp4"));     // it does not allow control

            store.SaveReport("p1", "Test phone", allowControl: true, new JsonObject { ["summary"] = new JsonObject { ["device"] = "Test phone" } });
            var c = store.Enqueue("p1", SpaceActionKinds.Delete, "/sdcard/a.mp4")!;
            Assert.Empty(store.Take("p2"));
            Assert.Equal(c.Id, Assert.Single(store.Take("p1")).Id);
            Assert.Empty(store.Take("p1"));                                                  // handed out once
            store.Complete("p2", c.Id, true, "not yours", 5);                                // another phone cannot answer for it
            Assert.Equal("sent", store.Status(c.Id)!.State);
            store.Complete("p1", c.Id, true, "Deleted", 1234);
            Assert.Equal(("done", 1234L), (store.Status(c.Id)!.State, store.Status(c.Id)!.FreedBytes));

            // The last report survives a restart (shown as "last seen").
            var again = new PhoneSpaceStore(dir);
            var r = again.Get("p1")!;
            Assert.Equal("Test phone", (string?)r.Data["summary"]!["device"]);
            Assert.True(r.AllowControl);
            Assert.Single(again.All());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Bridge_serves_the_PC_and_takes_the_phone_report()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        var temp = Path.Combine(Path.GetTempPath(), "cssd_space_bridge_" + Guid.NewGuid().ToString("N")[..8]);
        var host = AppHost.Create(new AppPaths(temp));
        var b = host.Settings.MobileBridge;
        b.Port = port;
        b.PairingToken = "0123456789abcdef0123456789abcdef";
        b.LegacyTokenAccepted = false;
        var bridge = new LocalMobileBridgeService(host, NullLogger<LocalMobileBridgeService>.Instance);
        var work = NewDir();
        try
        {
            bridge.Start();
            using var http = new HttpClient();
            string baseUrl = $"http://127.0.0.1:{port}";
            var pair = await http.PostAsync($"{baseUrl}/api/pair?code={bridge.PairingCode}&name=Pixel&installId=abc", null);
            var token = JsonDocument.Parse(await pair.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString();

            var summary = JsonDocument.Parse(await http.GetStringAsync($"{baseUrl}/api/space/summary?token={token}")).RootElement;
            Assert.Equal("pc", summary.GetProperty("kind").GetString());
            Assert.True(summary.GetProperty("volumes").GetArrayLength() > 0);
            Assert.True(summary.GetProperty("ram").GetProperty("total").GetInt64() > 0);

            var liveNow = JsonDocument.Parse(await http.GetStringAsync($"{baseUrl}/api/space/live?token={token}")).RootElement;
            Assert.True(liveNow.GetProperty("ram").GetProperty("total").GetInt64() > 0);
            Assert.True(liveNow.GetProperty("disk").GetProperty("total").GetInt64() > 0);

            Write(Path.Combine(work, "x.bin"), 2 * MiB);
            var folder = JsonDocument.Parse(await http.GetStringAsync($"{baseUrl}/api/space/folder?token={token}&path={Uri.EscapeDataString(work)}")).RootElement;
            Assert.Equal("x.bin", folder.GetProperty("items")[0].GetProperty("name").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"{baseUrl}/api/space/folder?token={token}&path={Uri.EscapeDataString(Path.Combine(work, "nope"))}")).StatusCode);

            // An action needs POST.
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await http.GetAsync($"{baseUrl}/api/space/action?token={token}&kind=rescan")).StatusCode);

            // The phone's own report, then a command from the PC that the phone fetches and answers.
            var report = new StringContent("{\"allowControl\":true,\"summary\":{\"device\":\"Pixel\",\"kind\":\"phone\"}}", Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.OK, (await http.PostAsync($"{baseUrl}/api/space/phone-report?token={token}", report)).StatusCode);
            var deviceId = host.Settings.MobileBridge.PairedDevices.Single().Id;
            Assert.Equal("Pixel", (string?)host.Space.Phones.Get(deviceId)!.Data["summary"]!["device"]);
            var cmd = host.Space.Phones.Enqueue(deviceId, SpaceActionKinds.Recycle, "/storage/emulated/0/a.mp4")!;
            var pending = JsonDocument.Parse(await http.GetStringAsync($"{baseUrl}/api/space/phone-commands?token={token}")).RootElement;
            Assert.Equal(cmd.Id, pending[0].GetProperty("id").GetString());
            await http.PostAsync($"{baseUrl}/api/space/phone-result?token={token}&id={cmd.Id}&ok=1&freed=777&message=Done", null);
            Assert.Equal(777, host.Space.Phones.Status(cmd.Id)!.FreedBytes);

            // A report that is too big is refused (the PC may close the connection before all of it was sent).
            var big = new StringContent(new string('x', LocalMobileBridgeService.MaxPhoneReportBytes + 10));
            try { Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await http.PostAsync($"{baseUrl}/api/space/phone-report?token={token}", big)).StatusCode); }
            catch (HttpRequestException) { }
            Assert.Equal("Pixel", (string?)host.Space.Phones.Get(deviceId)!.Data["summary"]!["device"]);
        }
        finally
        {
            await bridge.DisposeAsync();
            await host.DisposeAsync();
            Directory.Delete(work, true);
            try { Directory.Delete(temp, true); } catch { }
        }
    }
}
