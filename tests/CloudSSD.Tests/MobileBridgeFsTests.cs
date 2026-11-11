using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CloudSSD.App;
using CloudSSD.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>The phone's file management routes (/api/fs/*, /api/backup/*), with plain folders standing in for cloud drives.</summary>
public class MobileBridgeFsTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    /// <summary>A running bridge with three drives: "cloudssd" (all accounts), "accounts" (one folder per account), "ro" (read-only).</summary>
    private sealed record Fs(HttpClient Http, string Base, AppHost Host, string All, string Each, string Ro)
    {
        public async Task<(HttpStatusCode Status, JsonElement Json)> Send(HttpMethod method, string url, HttpContent? body = null)
        {
            using var resp = await Http.SendAsync(new HttpRequestMessage(method, Base + url) { Content = body });
            var text = await resp.Content.ReadAsStringAsync();
            return (resp.StatusCode, text.StartsWith('{') || text.StartsWith('[') ? JsonDocument.Parse(text).RootElement : default);
        }

        public Task<(HttpStatusCode Status, JsonElement Json)> Get(string url) => Send(HttpMethod.Get, url);
        public Task<(HttpStatusCode Status, JsonElement Json)> Post(string url, HttpContent? body = null) => Send(HttpMethod.Post, url, body);

        public Task<(HttpStatusCode Status, JsonElement Json)> Write(string path, string text, bool overwrite = false, long? modified = null) =>
            Post($"/api/fs/write?path={E(path)}&overwrite={(overwrite ? 1 : 0)}" + (modified is { } m ? $"&modified={m}" : ""), new StringContent(text));
    }

    private static string E(string s) => Uri.EscapeDataString(s);

    private static async Task WithFs(Func<Fs, Task> body)
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        var tempDir = Path.Combine(Path.GetTempPath(), "cpx_fs_test_" + Guid.NewGuid().ToString("N"));
        var host = AppHost.Create(new AppPaths(tempDir));
        host.Settings.MobileBridge.Port = port;
        host.Settings.MobileBridge.PairingToken = Token;
        host.Settings.MobileBridge.LegacyTokenAccepted = true;
        string all = Path.Combine(tempDir, "all"), each = Path.Combine(tempDir, "each"), ro = Path.Combine(tempDir, "ro");
        foreach (var d in new[] { all, Path.Combine(each, "a@gmail.com"), Path.Combine(each, "b@gmail.com"), ro }) Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(ro, "old.txt"), "old");
        var service = new LocalMobileBridgeService(host, NullLogger<LocalMobileBridgeService>.Instance)
        {
            CloudRootsOverride = () =>
            [
                new CloudRoot(AppHost.CombinedMountId, all, false, false, tempDir, "All Accounts"),
                new CloudRoot(AppHost.SingleMountId, each, false, true, tempDir, "Each Account"),
                new CloudRoot("ro", ro, true, false, tempDir, "Read only")
            ]
        };
        try
        {
            service.Start();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("X-Pairing-Token", Token);
            await body(new Fs(http, $"http://127.0.0.1:{port}", host, all, each, ro));
        }
        finally
        {
            await service.DisposeAsync();
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static string[] Names(JsonElement list) => list.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public Task Needs_the_token_and_the_switch() => WithFs(async fs =>
    {
        using var anonymous = new HttpClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(fs.Base + "/api/fs/roots")).StatusCode);

        fs.Host.Settings.MobileBridge.AllowFileManagement = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Get("/api/fs/roots")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Post($"/api/fs/create?parent={E("cloudssd:")}&name=x&dir=1")).Status);
        Assert.Empty(Directory.GetFileSystemEntries(fs.All));
    });

    [Fact]
    public Task Lists_the_drives_and_what_may_be_changed() => WithFs(async fs =>
    {
        var (status, roots) = await fs.Get("/api/fs/roots");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["cloudssd", "accounts", "ro"], roots.EnumerateArray().Select(r => r.GetProperty("id").GetString()));
        Assert.Equal([true, false, false], roots.EnumerateArray().Select(r => r.GetProperty("canCreate").GetBoolean()));

        // "Each Account": the account folders are fixed, their contents are not.
        var (_, top) = await fs.Get($"/api/fs/list?path={E("accounts:")}");
        Assert.Equal(["a@gmail.com", "b@gmail.com"], Names(top));
        Assert.All(top.GetProperty("entries").EnumerateArray(), e =>
        {
            Assert.False(e.GetProperty("canEdit").GetBoolean());
            Assert.True(e.GetProperty("canCreate").GetBoolean());
        });
        Assert.False(top.GetProperty("folder").GetProperty("canCreate").GetBoolean());
    });

    [Theory]
    [InlineData("cloudssd:../outside.txt", HttpStatusCode.BadRequest)]
    [InlineData("cloudssd:a/../../outside.txt", HttpStatusCode.BadRequest)]
    [InlineData("cloudssd:a.txt:stream", HttpStatusCode.BadRequest)]
    [InlineData("cloudssd:CON", HttpStatusCode.BadRequest)]
    [InlineData("cloudssd:name. ", HttpStatusCode.BadRequest)]
    [InlineData(@"\\server\share\x.txt", HttpStatusCode.BadRequest)]
    [InlineData(@"C:\Windows\x.txt", HttpStatusCode.ServiceUnavailable)]
    [InlineData("nosuchdrive:x.txt", HttpStatusCode.ServiceUnavailable)]
    [InlineData("", HttpStatusCode.BadRequest)]
    public Task A_path_outside_a_cloud_drive_is_refused(string path, HttpStatusCode expected) => WithFs(async fs =>
    {
        Assert.Equal(expected, (await fs.Write(path, "x")).Status);
        Assert.Equal(expected, (await fs.Get($"/api/fs/stat?path={E(path)}")).Status);
        Assert.Equal(expected, (await fs.Post($"/api/fs/delete?path={E(path)}")).Status);
        Assert.Empty(Directory.GetFileSystemEntries(fs.All));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(fs.All)!, "outside.txt")));
    });

    [Fact]
    public Task Create_makes_folders_and_files_and_never_replaces() => WithFs(async fs =>
    {
        var (s1, d1) = await fs.Post($"/api/fs/create?parent={E("cloudssd:")}&name=Docs&dir=1");
        var (_, d2) = await fs.Post($"/api/fs/create?parent={E("cloudssd:")}&name=Docs&dir=1");
        Assert.Equal(HttpStatusCode.OK, s1);
        Assert.Equal("cloudssd:Docs", d1.GetProperty("id").GetString());
        Assert.Equal("cloudssd:Docs (1)", d2.GetProperty("id").GetString());
        Assert.True(d1.GetProperty("isDirectory").GetBoolean());

        var (_, f1) = await fs.Post($"/api/fs/create?parent={E("cloudssd:Docs")}&name={E("note.txt")}");
        var (_, f2) = await fs.Post($"/api/fs/create?parent={E("cloudssd:Docs")}&name={E("note.txt")}");
        Assert.Equal("cloudssd:Docs/note.txt", f1.GetProperty("id").GetString());
        Assert.Equal("cloudssd:Docs/note (1).txt", f2.GetProperty("id").GetString());
        Assert.Equal(0, f1.GetProperty("size").GetInt64());
        // A name with characters Windows cannot store is cleaned, not refused.
        var (_, f3) = await fs.Post($"/api/fs/create?parent={E("cloudssd:Docs")}&name={E("what?.txt")}");
        Assert.Equal("what_.txt", f3.GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await fs.Post($"/api/fs/create?parent={E("cloudssd:Nope")}&name=x")).Status);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await fs.Get($"/api/fs/create?parent={E("cloudssd:")}&name=x")).Status);
    });

    [Fact]
    public Task Write_saves_the_whole_file_with_its_time_and_leaves_no_part_file() => WithFs(async fs =>
    {
        await fs.Post($"/api/fs/create?parent={E("cloudssd:")}&name=Photos&dir=1");
        long when = new DateTimeOffset(2024, 3, 15, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var (status, saved) = await fs.Write("cloudssd:Photos/a.txt", "hello", modified: when);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("cloudssd:Photos/a.txt", saved.GetProperty("id").GetString());
        Assert.Equal(5, saved.GetProperty("size").GetInt64());
        Assert.Equal(when, saved.GetProperty("modified").GetInt64());
        var file = Path.Combine(fs.All, "Photos", "a.txt");
        Assert.Equal("hello", File.ReadAllText(file));

        // Without overwrite a second file is kept beside the first; with it the content is replaced.
        var (_, second) = await fs.Write("cloudssd:Photos/a.txt", "other");
        Assert.Equal("a (1).txt", second.GetProperty("name").GetString());
        Assert.Equal("hello", File.ReadAllText(file));
        await fs.Write("cloudssd:Photos/a.txt", "replaced", overwrite: true);
        Assert.Equal("replaced", File.ReadAllText(file));

        // Chunked body (what Android sends when the size is not known in advance).
        var big = new byte[300_000];
        Random.Shared.NextBytes(big);
        using var req = new HttpRequestMessage(HttpMethod.Post, fs.Base + $"/api/fs/write?path={E("cloudssd:Photos/big.bin")}&overwrite=1") { Content = new StreamContent(new MemoryStream(big)) };
        req.Headers.TransferEncodingChunked = true;
        using (var resp = await fs.Http.SendAsync(req)) Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(big, File.ReadAllBytes(Path.Combine(fs.All, "Photos", "big.bin")));

        Assert.Empty(Directory.GetFiles(fs.All, "*" + LocalMobileBridgeService.PartSuffix, SearchOption.AllDirectories));
        Assert.Equal(HttpStatusCode.NotFound, (await fs.Write("cloudssd:Missing/a.txt", "x")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await fs.Write("cloudssd:Photos", "x", overwrite: true)).Status);
    });

    [Fact]
    public Task An_upload_that_is_cut_off_changes_nothing() => WithFs(async fs =>
    {
        var file = Path.Combine(fs.All, "keep.txt");
        File.WriteAllText(file, "original");
        File.WriteAllText(Path.Combine(fs.All, "x" + LocalMobileBridgeService.PartSuffix), "left over from a crash");

        var port = new Uri(fs.Base).Port;
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, port);
            var head = $"POST /api/fs/write?path={E("cloudssd:keep.txt")}&overwrite=1 HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nX-Pairing-Token: {Token}\r\nContent-Length: 1000\r\n\r\nonly ten b";
            await tcp.GetStream().WriteAsync(Encoding.ASCII.GetBytes(head));
            await Task.Delay(200);
        }
        // The server notices the closed connection, drops the part file and keeps the old content.
        string[] Parts() => Directory.GetFiles(fs.All).Where(f => Path.GetFileName(f).StartsWith("keep.txt.") && f.EndsWith(LocalMobileBridgeService.PartSuffix)).ToArray();
        for (int i = 0; i < 50 && Parts().Length > 0; i++) await Task.Delay(100);
        Assert.Empty(Parts());
        Assert.Equal("original", File.ReadAllText(file));

        // Part files never show up in a listing.
        var (_, list) = await fs.Get($"/api/fs/list?path={E("cloudssd:")}");
        Assert.Equal(["keep.txt"], Names(list));
    });

    [Fact]
    public Task Rename_and_move_refuse_to_replace_or_cross_drives() => WithFs(async fs =>
    {
        Directory.CreateDirectory(Path.Combine(fs.All, "A", "Inner"));
        Directory.CreateDirectory(Path.Combine(fs.All, "B"));
        File.WriteAllText(Path.Combine(fs.All, "A", "one.txt"), "1");
        File.WriteAllText(Path.Combine(fs.All, "A", "two.txt"), "2");

        Assert.Equal(HttpStatusCode.Conflict, (await fs.Post($"/api/fs/rename?path={E("cloudssd:A/one.txt")}&name=two.txt")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await fs.Post($"/api/fs/rename?path={E("cloudssd:A/one.txt")}&name={E("../x.txt")}")).Status);
        var (ok, renamed) = await fs.Post($"/api/fs/rename?path={E("cloudssd:A/one.txt")}&name=uno.txt");
        Assert.Equal(HttpStatusCode.OK, ok);
        Assert.Equal("cloudssd:A/uno.txt", renamed.GetProperty("id").GetString());
        Assert.Equal("1", File.ReadAllText(Path.Combine(fs.All, "A", "uno.txt")));

        Assert.Equal(HttpStatusCode.BadRequest, (await fs.Post($"/api/fs/move?path={E("cloudssd:A")}&to={E("cloudssd:A/Inner")}")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await fs.Post($"/api/fs/move?path={E("cloudssd:A")}&to={E("cloudssd:A")}")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await fs.Post($"/api/fs/move?path={E("cloudssd:A/uno.txt")}&to={E("accounts:a@gmail.com")}")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await fs.Post($"/api/fs/move?path={E("cloudssd:A/uno.txt")}&to={E("cloudssd:A")}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Post($"/api/fs/move?path={E("cloudssd:")}&to={E("cloudssd:B")}")).Status);

        var (moved, where) = await fs.Post($"/api/fs/move?path={E("cloudssd:A")}&to={E("cloudssd:B")}");
        Assert.Equal(HttpStatusCode.OK, moved);
        Assert.Equal("cloudssd:B/A", where.GetProperty("id").GetString());
        Assert.Equal("2", File.ReadAllText(Path.Combine(fs.All, "B", "A", "two.txt")));
        Assert.False(Directory.Exists(Path.Combine(fs.All, "A")));
    });

    [Fact]
    public Task Delete_removes_a_folder_with_its_contents_but_never_a_drive() => WithFs(async fs =>
    {
        Directory.CreateDirectory(Path.Combine(fs.All, "Old", "Deep"));
        File.WriteAllText(Path.Combine(fs.All, "Old", "Deep", "f.txt"), "x");
        File.WriteAllText(Path.Combine(fs.All, "stay.txt"), "x");

        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Post($"/api/fs/delete?path={E("cloudssd:")}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await fs.Post($"/api/fs/delete?path={E("cloudssd:Nope")}")).Status);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await fs.Get($"/api/fs/delete?path={E("cloudssd:Old")}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await fs.Post($"/api/fs/delete?path={E("cloudssd:Old")}")).Status);
        Assert.False(Directory.Exists(Path.Combine(fs.All, "Old")));
        Assert.True(File.Exists(Path.Combine(fs.All, "stay.txt")));
    });

    [Fact]
    public Task Read_only_drive_and_account_folders_cannot_be_changed() => WithFs(async fs =>
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Write("ro:new.txt", "x")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Write("ro:old.txt", "x", overwrite: true)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Post($"/api/fs/delete?path={E("ro:old.txt")}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Post($"/api/fs/rename?path={E("ro:old.txt")}&name=n.txt")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Post($"/api/fs/create?parent={E("ro:")}&name=x&dir=1")).Status);
        Assert.Equal("old", File.ReadAllText(Path.Combine(fs.Ro, "old.txt")));

        // "Each Account": nothing at the top, no change to an account folder, everything inside one.
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Write("accounts:loose.txt", "x")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Post($"/api/fs/create?parent={E("accounts:")}&name=x&dir=1")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Post($"/api/fs/delete?path={E("accounts:a@gmail.com")}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Post($"/api/fs/rename?path={E("accounts:a@gmail.com")}&name=z")).Status);
        Assert.Equal(HttpStatusCode.OK, (await fs.Write("accounts:a@gmail.com/mine.txt", "x")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await fs.Post($"/api/fs/move?path={E("accounts:a@gmail.com/mine.txt")}&to={E("accounts:b@gmail.com")}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await fs.Post($"/api/fs/delete?path={E("accounts:a@gmail.com/mine.txt")}")).Status);
        Assert.Equal(2, Directory.GetDirectories(fs.Each).Length);
    });

    [Fact]
    public Task Read_returns_the_bytes_and_honours_ranges() => WithFs(async fs =>
    {
        File.WriteAllText(Path.Combine(fs.All, "r.txt"), "0123456789");
        Assert.Equal("0123456789", await fs.Http.GetStringAsync(fs.Base + $"/api/fs/read?path={E("cloudssd:r.txt")}"));
        using var req = new HttpRequestMessage(HttpMethod.Get, fs.Base + $"/api/fs/read?path={E("cloudssd:r.txt")}");
        req.Headers.Range = new RangeHeaderValue(4, null);
        using var resp = await fs.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.PartialContent, resp.StatusCode);
        Assert.Equal("456789", await resp.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await fs.Get($"/api/fs/read?path={E("cloudssd:none.txt")}")).Status);

        fs.Host.Settings.MobileBridge.AllowStreaming = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await fs.Get($"/api/fs/read?path={E("cloudssd:r.txt")}")).Status);
    });

    [Fact]
    public Task Camera_backup_files_go_into_month_folders_and_are_not_sent_twice() => WithFs(async fs =>
    {
        long march = new DateTimeOffset(2024, 3, 15, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        string url = $"/api/backup/upload?device={E("Pixel 8")}&folder=Camera&name=IMG_1.jpg&modified={march}";

        var (status, first) = await fs.Post(url, new StringContent("photo-bytes"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(first.GetProperty("skipped").GetBoolean());
        var file = Path.Combine(fs.All, "Phone Backup", "Pixel 8", "Camera", "2024-03", "IMG_1.jpg");
        Assert.Equal("photo-bytes", File.ReadAllText(file));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(march).UtcDateTime, File.GetLastWriteTimeUtc(file));

        // The same photo again is recognised; a different one with the same name is kept beside it.
        var (_, again) = await fs.Post(url, new StringContent("photo-bytes"));
        Assert.True(again.GetProperty("skipped").GetBoolean());
        var (_, other) = await fs.Post(url, new StringContent("a different photo"));
        Assert.Equal("IMG_1 (1).jpg", other.GetProperty("name").GetString());

        var (_, index) = await fs.Get($"/api/backup/index?device={E("Pixel 8")}&folder=Camera");
        var files = index.GetProperty("files").EnumerateArray()
            .Select(f => $"{f.GetProperty("month").GetString()}/{f.GetProperty("name").GetString()}/{f.GetProperty("size").GetInt64()}").Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["2024-03/IMG_1 (1).jpg/17", "2024-03/IMG_1.jpg/11"], files);
        var (_, empty) = await fs.Get($"/api/backup/index?device={E("Pixel 8")}&folder=Camera&month=2024-04");
        Assert.Empty(empty.GetProperty("files").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await fs.Get($"/api/backup/index?device=x&month={E("..")}")).Status);

        // A folder chosen in settings is used instead of the cloud drive.
        var chosen = Path.Combine(Path.GetDirectoryName(fs.All)!, "chosen");
        fs.Host.Settings.MobileBridge.PhotoBackupDirectory = chosen;
        await fs.Post($"/api/backup/upload?device={E("../evil")}&name=v.mp4&modified={march}", new StringContent("video"));
        Assert.Equal("video", File.ReadAllText(Path.Combine(chosen, "evil", "Camera", "2024-03", "v.mp4")));
    });
}
