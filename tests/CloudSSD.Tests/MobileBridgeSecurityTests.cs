using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.Games;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>Who may use the phone bridge and what they can reach: device keys, signed links, switches, shared places, Tailscale, saves.</summary>
public class MobileBridgeSecurityTests
{
    private const string Shared = "0123456789abcdef0123456789abcdef";

    private sealed record Env(HttpClient Http, string Base, AppHost Host, LocalMobileBridgeService Bridge, string Temp, string Cloud, string Uploads)
    {
        public Task<HttpResponseMessage> Get(string url, string? token = Shared) => Http.GetAsync(Base + url + (token is null ? "" : (url.Contains('?') ? "&" : "?") + "token=" + token));
        public Task<HttpResponseMessage> Post(string url, string? token = Shared) => Http.PostAsync(Base + url + (token is null ? "" : (url.Contains('?') ? "&" : "?") + "token=" + token), null);
        public async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

        public async Task<string> PairNew(string name, string installId)
        {
            var r = await Http.PostAsync($"{Base}/api/pair?code={Bridge.PairingCode}&name={name}&installId={installId}", null);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return (await Json(r)).GetProperty("token").GetString()!;
        }
    }

    private static string E(string s) => Uri.EscapeDataString(s);

    private static async Task With(Func<Env, Task> body, Action<LocalMobileBridgeService>? setup = null)
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        var temp = Path.Combine(Path.GetTempPath(), "cpx_sec_test_" + Guid.NewGuid().ToString("N"));
        var host = AppHost.Create(new AppPaths(temp));
        var b = host.Settings.MobileBridge;
        b.Port = port;
        b.PairingToken = Shared;
        b.LegacyTokenAccepted = true;
        var cloud = Directory.CreateDirectory(Path.Combine(temp, "cloud")).FullName;
        var uploads = Directory.CreateDirectory(Path.Combine(temp, "uploads")).FullName;
        b.UploadDirectory = uploads;
        var bridge = new LocalMobileBridgeService(host, NullLogger<LocalMobileBridgeService>.Instance)
        {
            CloudRootsOverride = () => [new CloudRoot(AppHost.CombinedMountId, cloud, false, false, temp, "All Accounts")]
        };
        setup?.Invoke(bridge);
        try
        {
            bridge.Start();
            using var http = new HttpClient();
            await body(new Env(http, $"http://127.0.0.1:{port}", host, bridge, temp, cloud, uploads));
        }
        finally
        {
            await bridge.DisposeAsync();
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    // ---- shared places ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\up\a.txt", @"C:\up", true)]
    [InlineData(@"C:\up", @"C:\up\", true)]
    [InlineData(@"C:\uploads2\a.txt", @"C:\up", false)]          // a longer name that starts the same is not inside
    [InlineData(@"C:\up\..\secret.txt", @"C:\up", false)]
    [InlineData(@"\\?\C:\up\a.txt", @"C:\UP", true)]
    [InlineData(@"D:\up\a.txt", @"C:\up", false)]
    public void IsInsideAny_compares_whole_folders(string path, string root, bool inside) =>
        Assert.Equal(inside, LocalMobileBridgeService.IsInsideAny(path, [root]));

    [Fact]
    public Task Only_shared_places_can_be_read() => With(async env =>
    {
        var outside = Path.Combine(env.Temp, "private.txt");
        await File.WriteAllTextAsync(outside, "secret");
        var shared = Path.Combine(env.Uploads, "clip.mp4");
        await File.WriteAllTextAsync(shared, "video");
        var inCloud = Path.Combine(env.Cloud, "photo.jpg");
        await File.WriteAllTextAsync(inCloud, "jpg");

        foreach (var route in new[] { "/api/files?path=", "/api/stream?path=", "/api/download?path=", "/api/preview?path=", "/api/open-pc?path=" })
        {
            var r = await env.Get(route + E(outside));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("not-shared", (await env.Json(r)).GetProperty("code").GetString());
        }
        Assert.Equal("video", await (await env.Get("/api/stream?path=" + E(shared))).Content.ReadAsStringAsync());
        Assert.Equal("jpg", await (await env.Get("/api/download?path=" + E(inCloud))).Content.ReadAsStringAsync());

        env.Host.Settings.MobileBridge.AllowPcDisks = true;
        Assert.Equal("secret", await (await env.Get("/api/download?path=" + E(outside))).Content.ReadAsStringAsync());
    });

    // ---- device keys ------------------------------------------------------------------------------------------------

    [Fact]
    public Task Each_phone_has_its_own_key_and_can_be_removed_alone() => With(async env =>
    {
        var a = await env.PairNew("PhoneA", "install-a-0001");
        var b = await env.PairNew("PhoneB", "install-b-0002");
        Assert.NotEqual(a, b);
        Assert.Equal(HttpStatusCode.OK, (await env.Get("/api/hud", a)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Get("/api/hud", b)).StatusCode);

        // Re-pairing the same install replaces its entry instead of adding one.
        var a2 = await env.PairNew("PhoneA", "install-a-0001");
        Assert.Equal(2, env.Host.Settings.MobileBridge.PairedDevices.Count);
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Get("/api/hud", a)).StatusCode);

        var devA = env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "PhoneA");
        env.Bridge.RemoveDevice(devA.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Get("/api/hud", a2)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Get("/api/hud", b)).StatusCode);

        env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "PhoneB").IsBlocked = true;
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Get("/api/hud", b)).StatusCode);
    });

    [Fact]
    public Task Pairing_never_hands_out_the_shared_key_and_the_old_key_can_be_retired() => With(async env =>
    {
        var r = await env.Http.PostAsync($"{env.Base}/api/pair?token={Shared}&name=Old", null);
        var text = await r.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.DoesNotContain(Shared, text);

        // An old phone upgrades itself to a key of its own.
        var up = await env.Post("/api/pair/upgrade?name=Old&installId=old-install-1");
        var own = (await env.Json(up)).GetProperty("token").GetString()!;
        using (var st = JsonDocument.Parse(await (await env.Get("/api/status", own)).Content.ReadAsStringAsync()))
            Assert.Equal("device", st.RootElement.GetProperty("auth").GetString());

        env.Bridge.RetireLegacyToken();
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Get("/api/hud")).StatusCode);   // shared key no longer works
        Assert.Equal(HttpStatusCode.OK, (await env.Get("/api/hud", own)).StatusCode);       // its own key does
    });

    [Fact]
    public Task The_QR_link_pairs_once_and_only_on_the_home_network() => With(async env =>
    {
        var secret = env.Bridge.PairingLinkSecret;
        Assert.False(env.Bridge.TryPairingLink(secret, "8.8.8.8"));
        var ok = await env.Http.PostAsync($"{env.Base}/api/pair?pair={secret}&name=QR", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.NotEqual(secret, env.Bridge.PairingLinkSecret);
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Http.PostAsync($"{env.Base}/api/pair?pair={secret}&name=QR2", null)).StatusCode);
    });

    [Fact]
    public Task Device_list_shows_no_keys_and_a_phone_may_only_remove_itself() => With(async env =>
    {
        var a = await env.PairNew("PhoneA", "install-a-0001");
        await env.PairNew("PhoneB", "install-b-0002");
        var text = await (await env.Get("/api/devices", a)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("tokenHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", text);
        var other = env.Host.Settings.MobileBridge.PairedDevices.Single(d => d.Name == "PhoneB").Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Post($"/api/devices/remove?id={other}", a)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Post("/api/devices/remove?id=self", a)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Get("/api/hud", a)).StatusCode);
    });

    // ---- signed links -----------------------------------------------------------------------------------------------

    [Fact]
    public Task Signed_links_work_without_the_key_for_one_file_only() => With(async env =>
    {
        var token = await env.PairNew("Phone", "install-sign-01");
        var file = Path.Combine(env.Cloud, "movie.mp4");
        await File.WriteAllBytesAsync(file, Enumerable.Range(0, 200).Select(i => (byte)i).ToArray());

        var made = await env.Json(await env.Post("/api/media-url?path=" + E(file), token));
        var url = made.GetProperty("url").GetString()!;
        Assert.DoesNotContain(token, url);

        using var req = new HttpRequestMessage(HttpMethod.Get, env.Base + url);
        req.Headers.Range = new RangeHeaderValue(10, 19);
        using var part = await env.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal(Enumerable.Range(10, 10).Select(i => (byte)i).ToArray(), await part.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Http.GetAsync(env.Base + url.Replace("movie.mp4", "other.mp4"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Http.GetAsync(env.Base + url.Replace("/api/stream", "/api/hud"))).StatusCode);
        var exp = System.Text.RegularExpressions.Regex.Match(url, "exp=(\\d+)").Groups[1].Value;
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Http.GetAsync(env.Base + url.Replace("exp=" + exp, "exp=" + (long.Parse(exp) + 1)))).StatusCode);

        // By drive id too (the phone's own file manager names files that way).
        var byId = await env.Json(await env.Post("/api/media-url?kind=download&id=" + E("cloudssd:movie.mp4"), token));
        Assert.Equal(HttpStatusCode.OK, (await env.Http.GetAsync(env.Base + byId.GetProperty("url").GetString())).StatusCode);

        env.Host.Settings.MobileBridge.AllowStreaming = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Http.GetAsync(env.Base + url)).StatusCode);
        env.Host.Settings.MobileBridge.AllowStreaming = true;

        env.Bridge.RemoveDevice(env.Host.Settings.MobileBridge.PairedDevices.Single().Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Http.GetAsync(env.Base + url)).StatusCode);
    });

    // ---- switches ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("POST", "/api/drives/mount?id=x", "pc")]
    [InlineData("POST", "/api/drives/unmount?id=x", "pc")]
    [InlineData("POST", "/api/clean", "pc")]
    [InlineData("POST", "/api/storage/offload-now", "pc")]
    [InlineData("POST", "/api/storage/offload-config?enabled=false", "pc")]
    [InlineData("POST", "/api/transfers/clear", "pc")]
    [InlineData("POST", "/api/saves/restore?gameId=x", "pc")]
    [InlineData("GET", "/api/open-pc?path=C%3A%5Cx.txt", "pc")]
    [InlineData("POST", "/api/upload?filename=a.txt", "uploads")]
    [InlineData("POST", "/api/fs/write?path=cloudssd%3Aa.txt", "uploads")]
    [InlineData("POST", "/api/backup/upload?name=a.jpg", "uploads")]
    [InlineData("GET", "/api/saves", "hud")]
    [InlineData("GET", "/api/media-url?path=x", "streaming")]
    [InlineData("GET", "/api/space/summary", "space")]
    [InlineData("GET", "/api/space/folder?path=C%3A%5C", "space")]
    [InlineData("POST", "/api/space/action?kind=rescan", "space")]
    [InlineData("POST", "/api/space/action?kind=rescan", "pc")]
    public Task Every_route_has_a_switch(string method, string url, string which) => With(async env =>
    {
        var b = env.Host.Settings.MobileBridge;
        switch (which)
        {
            case "pc": b.AllowPcControl = false; break;
            case "uploads": b.AllowUploads = false; break;
            case "hud": b.AllowGamingHud = false; break;
            case "streaming": b.AllowStreaming = false; break;
            case "space": b.AllowSpace = false; break;
        }
        var withToken = url + (url.Contains('?') ? "&" : "?") + "token=" + Shared;
        var r = method == "POST" ? await env.Http.PostAsync(env.Base + withToken, new StringContent("data"))
            : await env.Get(url);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Empty(Directory.GetFiles(env.Uploads));
        Assert.Empty(Directory.GetFiles(env.Cloud, "*", SearchOption.AllDirectories));
    });

    [Fact]
    public Task Clearing_history_needs_POST_and_offload_settings_are_checked() => With(async env =>
    {
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await env.Get("/api/transfers/clear")).StatusCode);
        var before = env.Host.Settings.SmartOffload.TargetMountId;
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Post("/api/storage/offload-config?targetMountId=nope&enabled=false")).StatusCode);
        Assert.Equal(before, env.Host.Settings.SmartOffload.TargetMountId);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Post("/api/storage/offload-config?thresholdGb=0")).StatusCode);
    });

    [Fact]
    public Task Settings_show_the_switches_and_this_phone() => With(async env =>
    {
        var token = await env.PairNew("Mine", "install-mine-01");
        var s = await env.Json(await env.Get("/api/settings", token));
        Assert.True(s.GetProperty("permissions").GetProperty("uploads").GetBoolean());
        Assert.False(s.GetProperty("permissions").GetProperty("pcDisks").GetBoolean());
        Assert.Equal("Mine", s.GetProperty("device").GetProperty("name").GetString());
        Assert.Contains(s.GetProperty("devices").EnumerateArray(), d => d.GetProperty("isThisPhone").GetBoolean());
        Assert.Equal(LocalMobileBridgeService.AppVersion, s.GetProperty("version").GetString());
    });

    // ---- Tailscale ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.114.192.41", true)]
    [InlineData("100.127.255.254", true)]
    [InlineData("100.63.255.255", false)]
    [InlineData("100.128.0.1", false)]
    [InlineData("fd7a:115c:a1e0::5", true)]
    [InlineData("fd00::1", false)]
    [InlineData("192.168.1.5", false)]
    public void Tailnet_addresses_are_recognised(string ip, bool tailnet) =>
        Assert.Equal(tailnet, TailscaleInfo.IsTailnetAddress(IPAddress.Parse(ip)));

    [Fact]
    public void The_QR_code_never_shows_a_Tailscale_address()
    {
        Assert.True(LocalMobileBridgeService.ScoreAddress(NetworkInterfaceType.Wireless80211, IPAddress.Parse("100.100.1.1")) < 0);
        Assert.True(LocalMobileBridgeService.ScoreAddress(NetworkInterfaceType.Wireless80211, IPAddress.Parse("192.168.1.5"))
                    > LocalMobileBridgeService.ScoreAddress(NetworkInterfaceType.Ethernet, IPAddress.Parse("10.0.0.5")));
        Assert.False(LocalMobileBridgeService.IsLocalNetworkAddress("100.114.192.41")); // no code pairing over Tailscale
    }

    [Fact]
    public void Only_this_PCs_own_MagicDNS_name_is_accepted()
    {
        Assert.True(LocalMobileBridgeService.IsAllowedHost("roshan.tail1234.ts.net:8090", "roshan.tail1234.ts.net"));
        Assert.False(LocalMobileBridgeService.IsAllowedHost("other.tail1234.ts.net:8090", "roshan.tail1234.ts.net"));
        Assert.False(LocalMobileBridgeService.IsAllowedHost("roshan.tail1234.ts.net:8090"));
    }

    [Fact]
    public Task Tailscale_connections_follow_the_switch_and_never_pair() => With(async env =>
    {
        TailscaleInfo.Override = () => new TailscaleInfo.Snapshot(["100.100.1.2"], "pc.tail1234.ts.net");
        try { await TailscaleChecks(env); }
        finally { TailscaleInfo.Override = null; }
    });

    private static async Task TailscaleChecks(Env env)
    {
        var token = await env.PairNew("Phone", "install-ts-0001");
        env.Bridge.RouteOverride = (_, _) => LocalMobileBridgeService.NetRoute.Tailnet;
        var off = await env.Get("/api/status", token);
        Assert.Equal(HttpStatusCode.Forbidden, off.StatusCode);
        Assert.Equal("tailnet-off", (await env.Json(off)).GetProperty("code").GetString());

        env.Host.Settings.MobileBridge.AllowTailscale = true;
        var on = await env.Json(await env.Get("/api/status", token));
        Assert.Equal("tailnet", on.GetProperty("route").GetString());
        Assert.True(on.GetProperty("away").GetProperty("allowed").GetBoolean());
        Assert.Equal("100.100.1.2", on.GetProperty("away").GetProperty("ips")[0].GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Http.PostAsync($"{env.Base}/api/pair?code={env.Bridge.PairingCode}", null)).StatusCode);

        env.Bridge.RouteOverride = (_, _) => LocalMobileBridgeService.NetRoute.Other;
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Get("/api/status", token)).StatusCode);
    }

    // ---- game saves -------------------------------------------------------------------------------------------------

    [Fact]
    public Task Save_backups_can_be_listed_downloaded_and_restored() => With(async env =>
    {
        var saveDir = Directory.CreateDirectory(Path.Combine(env.Temp, "game-saves")).FullName;
        await File.WriteAllTextAsync(Path.Combine(saveDir, "slot1.sav"), "current");
        var game = new GameEntry { Name = "Demo Game", MountId = "none", CustomSavePath = saveDir };
        env.Host.Library.Save(game);
        var backups = Directory.CreateDirectory(Path.Combine(env.Host.Paths.DataRoot, "Backups", game.Id)).FullName;
        var zipPath = Path.Combine(backups, $"save_{game.Id}_20260101_120000.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("slot1.sav").Open())) await w.WriteAsync("from backup");

        var list = await env.Json(await env.Get("/api/saves"));
        var row = Assert.Single(list.EnumerateArray());
        Assert.Equal(game.Id, row.GetProperty("gameId").GetString());
        var file = row.GetProperty("file").GetString()!;

        var dl = await env.Get($"/api/saves/download?gameId={game.Id}&file={E(file)}");
        Assert.Equal(HttpStatusCode.OK, dl.StatusCode);
        Assert.Equal(new FileInfo(zipPath).Length, (await dl.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Get($"/api/saves/download?gameId={game.Id}&file={E(@"..\..\settings.json")}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Get($"/api/saves/download?gameId=nope&file={E(file)}")).StatusCode);

        var restored = await env.Post($"/api/saves/restore?gameId={game.Id}&file={E(file)}");
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal("from backup", await File.ReadAllTextAsync(Path.Combine(saveDir, "slot1.sav")));
        Assert.Contains(Directory.GetFiles(backups), f => Path.GetFileName(f).StartsWith("safety_pre_restore_"));
    });

    // ---- the two copies of the phone app ----------------------------------------------------------------------------

    [Fact]
    public void Phone_app_assets_match_the_PC_copy()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CloudSSD.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var app = Path.Combine(dir!.FullName, "mobile-android", "src", "main", "assets");
        var pc = Path.Combine(dir.FullName, "src", "CloudSSD.Desktop", "wwwroot", "mobile");
        foreach (var f in Directory.GetFiles(app))
        {
            var twin = Path.Combine(pc, Path.GetFileName(f));
            Assert.True(File.Exists(twin), "missing in wwwroot/mobile: " + Path.GetFileName(f));
            Assert.True(File.ReadAllBytes(f).AsSpan().SequenceEqual(File.ReadAllBytes(twin)), "different: " + Path.GetFileName(f));
        }
    }
}
