using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.Desktop;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudSSD.Tests;

public class MobileBridgeTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    private static (AppHost Host, string TempDir) CreateTestHost(int port)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cpx_bridge_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var paths = new AppPaths(tempDir);
        var host = AppHost.Create(paths);
        host.Settings.MobileBridge.Port = port;
        host.Settings.MobileBridge.PairingToken = Token;
        host.Settings.MobileBridge.LegacyTokenAccepted = true; // these tests use the shared token, like phones paired before device tokens
        host.Settings.MobileBridge.UploadDirectory = Path.Combine(tempDir, "uploads");
        return (host, tempDir);
    }

    /// <summary>Runs a started bridge on a free port for the test body, then stops it and deletes its folder.</summary>
    private static Task WithBridge(Func<HttpClient, string, AppHost, string, Task> body) =>
        WithBridge((http, baseUrl, host, tempDir, _) => body(http, baseUrl, host, tempDir));

    private static async Task WithBridge(Func<HttpClient, string, AppHost, string, LocalMobileBridgeService, Task> body)
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        var (host, tempDir) = CreateTestHost(port);
        var service = new LocalMobileBridgeService(host, NullLogger<LocalMobileBridgeService>.Instance);
        try
        {
            service.Start();
            Assert.True(service.IsRunning);
            using var http = new HttpClient();
            await body(http, $"http://127.0.0.1:{port}", host, tempDir, service);
        }
        finally
        {
            await service.DisposeAsync();
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void QrCodeGenerator_Generates_Valid_Bitmap_For_PairingUrl()
    {
        string url = $"http://192.168.1.15:8090/?token={Token}";
        var bmp = QrCodeGenerator.GenerateQrBitmap(url, 4);

        Assert.NotNull(bmp);
        Assert.True(bmp.PixelWidth > 0);
        Assert.True(bmp.PixelHeight > 0);
        Assert.True(bmp.IsFrozen);
    }

    [Fact]
    public void LocalMobileBridgeService_PreferredLocalIp_Returns_Valid_Address()
    {
        var ip = LocalMobileBridgeService.GetPreferredLocalIp();
        Assert.NotNull(ip);
        Assert.NotEmpty(ip);
        Assert.True(IPAddress.TryParse(ip, out _));
    }

    [Fact]
    public void New_pairing_tokens_have_128_bits()
    {
        var t = MobileBridgeSettings.NewPairingToken();
        Assert.Equal(32, t.Length);
        Assert.Matches("^[0-9a-f]{32}$", t);
        Assert.NotEqual(t, MobileBridgeSettings.NewPairingToken());
    }

    [Fact]
    public Task MobileBridge_Serves_Status_Hud_And_Pairing() => WithBridge(async (http, baseUrl, host, tempDir) =>
    {
        // Unpaired status: only what the phone needs to find the PC.
        using (var doc = JsonDocument.Parse(await http.GetStringAsync($"{baseUrl}/api/status")))
        {
            Assert.Equal(LocalMobileBridgeService.AppVersion, doc.RootElement.GetProperty("version").GetString());
            Assert.Contains("device-tokens", doc.RootElement.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
            Assert.False(doc.RootElement.GetProperty("isPaired").GetBoolean());
            Assert.False(doc.RootElement.TryGetProperty("storage", out _));
        }

        // HUD needs the token and reports real (or null) values.
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"{baseUrl}/api/hud")).StatusCode);
        var hud = await http.GetAsync($"{baseUrl}/api/hud?token={Token}");
        Assert.Equal(HttpStatusCode.OK, hud.StatusCode);
        var hudJson = await hud.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(hudJson))
        {
            Assert.True(doc.RootElement.TryGetProperty("ramUsedPercent", out var ram), hudJson);
            Assert.InRange(ram.GetDouble(), 1, 100);
            Assert.False(doc.RootElement.TryGetProperty("fps", out _), "no made-up FPS: " + hudJson); // null values are omitted
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync($"{baseUrl}/api/pair?token=wrong_token", null)).StatusCode);
        var okPair = await http.PostAsync($"{baseUrl}/api/pair?token={Token}&name=Pixel8", null);
        Assert.Equal(HttpStatusCode.OK, okPair.StatusCode);
        Assert.Contains(host.Settings.MobileBridge.PairedDevices, d => d.Name == "Pixel8");

        // Pairing an IP no longer lets that IP in without the token.
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"{baseUrl}/api/drives")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"{baseUrl}/")).StatusCode);
    });

    [Fact]
    public Task Phone_app_pairs_with_the_six_digit_code() => WithBridge(async (http, baseUrl, host, _, bridge) =>
    {
        var wrong = await http.PostAsync($"{baseUrl}/api/pair?code=abc&name=Phone", null);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.DoesNotContain(Token, await wrong.Content.ReadAsStringAsync());
        Assert.DoesNotContain(bridge.PairingCode, await http.GetStringAsync($"{baseUrl}/api/status")); // never served

        string code = bridge.PairingCode;
        var ok = await http.PostAsync($"{baseUrl}/api/pair?code={code}&name=vivo", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        string own;
        using (var doc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync()))
            own = doc.RootElement.GetProperty("token").GetString()!;
        // The phone gets a key of its own, never the shared one, and the PC keeps only its hash.
        Assert.Matches("^[0-9a-f]{32}$", own);
        Assert.NotEqual(Token, own);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"{baseUrl}/api/hud?token={own}")).StatusCode);
        var vivo = Assert.Single(host.Settings.MobileBridge.PairedDevices, d => d.Name == "vivo");
        Assert.Equal(LocalMobileBridgeService.HashToken(own), vivo.TokenHash);

        // Used up: the same six digits do not pair a second phone.
        Assert.NotEqual(code, bridge.PairingCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync($"{baseUrl}/api/pair?code={code}", null)).StatusCode);
    });

    [Fact]
    public void Pairing_code_is_used_once_and_wrong_guesses_lock_it()
    {
        var (host, tempDir) = CreateTestHost(0);
        try
        {
            var bridge = new LocalMobileBridgeService(host, NullLogger<LocalMobileBridgeService>.Instance);
            string code = bridge.PairingCode;
            Assert.Matches("^[0-9]{6}$", code);

            Assert.Equal((false, 0), bridge.TryPairingCode(code, "8.8.8.8"));           // not on the local network
            Assert.Equal(code, bridge.PairingCode);
            Assert.Equal((true, 0), bridge.TryPairingCode(code[..3] + " " + code[3..], "192.168.1.20")); // typed with the space
            Assert.NotEqual(code, bridge.PairingCode);                                   // used up
            Assert.Equal((false, 0), bridge.TryPairingCode(code, "192.168.1.20"));

            // The first wrong guess was the reuse above; four more reach the limit.
            for (int i = 1; i < LocalMobileBridgeService.MaxCodeFailures; i++)
                Assert.False(bridge.TryPairingCode("x", "192.168.1.20").Ok);
            var locked = bridge.TryPairingCode(bridge.PairingCode, "192.168.1.20");
            Assert.False(locked.Ok);
            Assert.InRange(locked.RetryAfterSeconds, 1, (int)LocalMobileBridgeService.CodeLockout.TotalSeconds);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    [Theory]
    [InlineData("192.168.31.4", true)]
    [InlineData("10.0.0.7", true)]
    [InlineData("172.20.1.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::ffff:192.168.1.9", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("unknown", false)]
    public void Code_pairing_is_for_the_local_network_only(string ip, bool local) =>
        Assert.Equal(local, LocalMobileBridgeService.IsLocalNetworkAddress(ip));

    [Fact]
    public Task Files_lists_a_folder_and_reports_a_missing_drive() => WithBridge(async (http, baseUrl, host, tempDir) =>
    {
        host.Settings.MobileBridge.AllowPcDisks = true; // the test folder is on a PC disk, not a cloud drive
        var dir = Directory.CreateDirectory(Path.Combine(tempDir, "browse")).FullName;
        Directory.CreateDirectory(Path.Combine(dir, "Zeta"));
        Directory.CreateDirectory(Path.Combine(dir, "alpha"));
        await File.WriteAllTextAsync(Path.Combine(dir, "b.txt"), "12345");
        await File.WriteAllTextAsync(Path.Combine(dir, "A.mp4"), "x");

        using var doc = JsonDocument.Parse(await http.GetStringAsync($"{baseUrl}/api/files?path={Uri.EscapeDataString(dir)}&token={Token}"));
        Assert.Equal(["alpha", "Zeta"], doc.RootElement.GetProperty("folders").EnumerateArray().Select(f => f.GetProperty("name").GetString()));
        var files = doc.RootElement.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(["A.mp4", "b.txt"], files.Select(f => f.GetProperty("name").GetString()));
        Assert.True(files[0].GetProperty("isVideo").GetBoolean());
        Assert.Equal(5, files[1].GetProperty("size").GetInt64());
        Assert.Equal(Path.Combine(dir, "b.txt"), files[1].GetProperty("path").GetString());

        // A folder name with spaces, written the way browsers and Java write a query ("+" for a space, %2B for a plus).
        var spaced = Directory.CreateDirectory(Path.Combine(dir, "My Folder + more")).FullName;
        await File.WriteAllTextAsync(Path.Combine(spaced, "in.txt"), "x");
        var formEncoded = Uri.EscapeDataString(spaced).Replace("%20", "+");
        using (var sub = JsonDocument.Parse(await http.GetStringAsync($"{baseUrl}/api/files?path={formEncoded}&token={Token}")))
            Assert.Equal("in.txt", sub.RootElement.GetProperty("files")[0].GetProperty("name").GetString());

        // A drive letter that does not exist is a clear "not connected", not an empty folder.
        char free = "QRSTUVW".First(c => !Directory.Exists(c + ":\\"));
        var missing = await http.GetAsync($"{baseUrl}/api/files?path={free}%3A%5C&token={Token}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var err = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.True(err.RootElement.GetProperty("driveMissing").GetBoolean());
    });

    [Fact]
    public Task Remote_only_knows_media_keys() => WithBridge(async (http, baseUrl, host, _) =>
    {
        // Known keys are not pressed here (that would change this PC's volume); only the refusals are exercised.
        Assert.True(MediaKeys.IsKnown("volup"));
        Assert.True(MediaKeys.IsKnown("PlayPause"));
        Assert.False(MediaKeys.IsKnown("shutdown"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync($"{baseUrl}/api/remote?action=mute", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync($"{baseUrl}/api/remote?action=shutdown&token={Token}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await http.GetAsync($"{baseUrl}/api/remote?action=mute&token={Token}")).StatusCode);

        host.Settings.MobileBridge.AllowGamingHud = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync($"{baseUrl}/api/remote?action=mute&token={Token}", null)).StatusCode);
    });

    [Fact]
    public Task Drives_and_status_report_real_storage_only() => WithBridge(async (http, baseUrl, host, tempDir) =>
    {
        // PC disks are listed only when they are shared.
        using (var hidden = JsonDocument.Parse(await http.GetStringAsync($"{baseUrl}/api/drives?token={Token}")))
            Assert.DoesNotContain(hidden.RootElement.EnumerateArray(), d => !d.GetProperty("isCloud").GetBoolean());
        host.Settings.MobileBridge.AllowPcDisks = true;
        using var drives = JsonDocument.Parse(await http.GetStringAsync($"{baseUrl}/api/drives?token={Token}"));
        var local = drives.RootElement.EnumerateArray().First(d => !d.GetProperty("isCloud").GetBoolean());
        Assert.False(local.GetProperty("canToggle").GetBoolean());
        Assert.True(local.GetProperty("totalBytes").GetInt64() > local.GetProperty("freeBytes").GetInt64());

        using var status = JsonDocument.Parse(await http.GetStringAsync($"{baseUrl}/api/status?token={Token}"));
        Assert.Equal(0, status.RootElement.GetProperty("drivesCount").GetInt32());
        var cloud = status.RootElement.GetProperty("cloud");
        Assert.Equal(0, cloud.GetProperty("accountsCount").GetInt32());
        Assert.False(cloud.TryGetProperty("totalBytes", out _), "no accounts: no invented cloud size");

        // Disconnecting a drive that is not connected succeeds quietly; connecting an unknown one is a clear 404.
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync($"{baseUrl}/api/drives/unmount?id=nope&token={Token}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync($"{baseUrl}/api/drives/mount?id=nope&token={Token}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync($"{baseUrl}/api/drives/mount?id=nope&token={Token}")).StatusCode);
    });

    [Fact]
    public Task Short_tokens_from_v070_are_replaced_on_start()
    {
        var (host, tempDir) = CreateTestHost(0);
        try
        {
            host.Settings.MobileBridge.PairingToken = "testtoken123";
            host.Settings.MobileBridge.Port = 9137;
            var service = new LocalMobileBridgeService(host, NullLogger<LocalMobileBridgeService>.Instance);
            service.Start();
            service.Stop();
            Assert.Equal(32, host.Settings.MobileBridge.PairingToken.Length);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
        return Task.CompletedTask;
    }

    [Fact]
    public Task Requests_from_this_PC_need_the_token_too() => WithBridge(async (http, baseUrl, _, tempDir) =>
    {
        var f = Path.Combine(tempDir, "secret.txt");
        await File.WriteAllTextAsync(f, "secret");
        foreach (var url in new[] { "/api/drives", $"/api/preview?path={Uri.EscapeDataString(f)}", $"/api/download?path={Uri.EscapeDataString(f)}" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(baseUrl + url)).StatusCode);
    });

    [Fact]
    public Task Static_route_cannot_leave_the_app_folder() => WithBridge(async (http, baseUrl, _, tempDir) =>
    {
        var f = Path.Combine(tempDir, "settings_copy.json");
        await File.WriteAllTextAsync(f, "{\"PairingToken\":\"x\"}");
        var abs = "/" + f.Replace('\\', '/');                       // GET /C:/Users/.../settings_copy.json
        var resp = await http.GetAsync(baseUrl + abs);
        Assert.NotEqual("{\"PairingToken\":\"x\"}", await resp.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(baseUrl + "/%2e%2e%2f%2e%2e%2fsettings.json")).StatusCode);
    });

    [Fact]
    public Task Foreign_host_names_are_refused() => WithBridge(async (http, baseUrl, _, _) =>
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/hud?token={Token}");
        req.Headers.Host = "attacker.example";
        Assert.Equal(HttpStatusCode.MisdirectedRequest, (await http.SendAsync(req)).StatusCode);
    });

    [Fact]
    public Task Open_on_pc_refuses_programs_and_scripts() => WithBridge(async (http, baseUrl, host, tempDir) =>
    {
        host.Settings.MobileBridge.AllowPcDisks = true; // so the file-type check is what refuses, not the shared-places check
        // Never executed: the check runs before anything is started. Contents are harmless anyway.
        foreach (var name in new[] { "x.bat", "x.exe", "x.ps1", "x.lnk", "x.url", "x.html" })
        {
            var f = Path.Combine(tempDir, name);
            await File.WriteAllTextAsync(f, "rem");
            var resp = await http.GetAsync($"{baseUrl}/api/open-pc?path={Uri.EscapeDataString(f)}&token={Token}");
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }
    });

    [Fact]
    public Task Stream_supports_ranges_including_suffix_and_416() => WithBridge(async (http, baseUrl, host, tempDir) =>
    {
        var uploads = Directory.CreateDirectory(host.Settings.MobileBridge.UploadDirectory!).FullName; // a shared place
        string testFile = Path.Combine(uploads, "sample_clip.mp4");
        byte[] data = Enumerable.Range(0, 1024).Select(i => (byte)(i % 256)).ToArray();
        await File.WriteAllBytesAsync(testFile, data);
        string url = $"{baseUrl}/api/stream?path={Uri.EscapeDataString(testFile)}&token={Token}";

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(0, 63);
        var resp = await http.SendAsync(req);
        Assert.Equal(HttpStatusCode.PartialContent, resp.StatusCode);
        var got = await resp.Content.ReadAsByteArrayAsync();
        Assert.Equal(data[..64], got);

        req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(null, 100); // last 100 bytes
        got = await (await http.SendAsync(req)).Content.ReadAsByteArrayAsync();
        Assert.Equal(data[^100..], got);

        req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(5000, null);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, (await http.SendAsync(req)).StatusCode);
    });

    [Fact]
    public Task Uploads_never_overwrite_and_accept_chunked_bodies() => WithBridge(async (http, baseUrl, host, tempDir) =>
    {
        string url = $"{baseUrl}/api/upload?filename={Uri.EscapeDataString("..\\photo.jpg:hidden")}&token={Token}";
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync(url, new ByteArrayContent(Encoding.UTF8.GetBytes("first")))).StatusCode);

        // Chunked: StreamContent without a known length makes HttpClient use Transfer-Encoding: chunked.
        var chunked = new StreamContent(new NonSeekable(Encoding.UTF8.GetBytes("second-upload")));
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = chunked };
        req.Headers.TransferEncodingChunked = true;
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(req)).StatusCode);

        var dir = host.Settings.MobileBridge.UploadDirectory!;
        var files = Directory.GetFiles(dir).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["photo (1).jpg_hidden", "photo.jpg_hidden"], files);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(dir, "photo.jpg_hidden")));
        Assert.Equal("second-upload", await File.ReadAllTextAsync(Path.Combine(dir, "photo (1).jpg_hidden")));
    });

    [Theory]
    [InlineData(@"C:\Users\me\file.txt", true)]
    [InlineData(@"C:/Users/me/../me/file.txt", true)]
    [InlineData(@"\\attacker\share\x", false)]
    [InlineData(@"\\?\C:\Windows\win.ini", false)]
    [InlineData(@"C:\file.txt:stream", false)]
    [InlineData(@"relative\file.txt", false)]
    [InlineData(@"C:", false)]
    [InlineData("", false)]
    public void ResolveLocalPath_accepts_only_plain_drive_paths(string raw, bool ok) =>
        Assert.Equal(ok, LocalMobileBridgeService.ResolveLocalPath(raw) != null);

    [Theory]
    [InlineData("bytes=0-63", 1024, 0, 63)]
    [InlineData("bytes=1000-", 1024, 1000, 1023)]
    [InlineData("bytes=-100", 1024, 924, 1023)]
    [InlineData("bytes=-5000", 1024, 0, 1023)]
    [InlineData("bytes=10-99999", 1024, 10, 1023)]
    public void ParseRange_handles_all_single_range_forms(string header, long length, long start, long end) =>
        Assert.Equal((start, end), LocalMobileBridgeService.ParseRange(header, length));

    [Theory]
    [InlineData("bytes=2000-", 1024)]
    [InlineData("bytes=50-10", 1024)]
    [InlineData("bytes=-0", 1024)]
    [InlineData("items=0-1", 1024)]
    public void ParseRange_rejects_unsatisfiable(string header, long length) =>
        Assert.Null(LocalMobileBridgeService.ParseRange(header, length));

    [Theory]
    [InlineData("photo.jpg", "photo.jpg")]
    [InlineData(@"..\..\Windows\evil.dll", "evil.dll")]
    [InlineData("a.txt:ads", "a.txt_ads")]
    [InlineData("CON.txt", null)]
    [InlineData("", null)]
    public void SafeUploadName_strips_paths_streams_and_device_names(string requested, string? expected)
    {
        var name = LocalMobileBridgeService.SafeUploadName(requested);
        if (expected != null) Assert.Equal(expected, name);
        else Assert.StartsWith("upload_", name);
    }

    [Theory]
    [InlineData("192.168.1.5:8090", true)]
    [InlineData("127.0.0.1:8090", true)]
    [InlineData("[::1]:8090", true)]
    [InlineData("localhost:8090", true)]
    [InlineData("evil.example:8090", false)]
    [InlineData("192.168.1.5.nip.io", false)]
    public void IsAllowedHost_only_accepts_ip_literals_and_this_pc(string host, bool ok) =>
        Assert.Equal(ok, LocalMobileBridgeService.IsAllowedHost(host));

    /// <summary>A stream with no length, so HttpClient cannot send Content-Length.</summary>
    private sealed class NonSeekable(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
