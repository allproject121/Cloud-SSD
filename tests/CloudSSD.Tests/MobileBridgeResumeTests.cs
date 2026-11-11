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

/// <summary>Uploads that continue after a broken connection, resumable downloads, thumbnails, deeper search and folder details.</summary>
public class MobileBridgeResumeTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";
    private const string Id = "0123456789abcdef0123456789abcdee";

    private sealed record Env(HttpClient Http, string Base, int Port, AppHost Host, LocalMobileBridgeService Bridge, string Temp, string Cloud)
    {
        public async Task<(HttpStatusCode Status, JsonElement Json)> Send(HttpMethod m, string url, HttpContent? body = null)
        {
            using var r = await Http.SendAsync(new HttpRequestMessage(m, Base + url) { Content = body });
            var text = await r.Content.ReadAsStringAsync();
            return (r.StatusCode, text.StartsWith('{') || text.StartsWith('[') ? JsonDocument.Parse(text).RootElement : default);
        }

        /// <summary>Sends headers and only part of the body, then drops the connection (a phone losing Wi-Fi).</summary>
        public async Task CutOff(string url, long contentLength, byte[] partial)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, Port);
            var head = $"POST {url} HTTP/1.1\r\nHost: 127.0.0.1:{Port}\r\nX-Pairing-Token: {Token}\r\nContent-Length: {contentLength}\r\n\r\n";
            await tcp.GetStream().WriteAsync(Encoding.ASCII.GetBytes(head));
            await tcp.GetStream().WriteAsync(partial);
            await Task.Delay(300);
        }
    }

    private static string E(string s) => Uri.EscapeDataString(s);

    private static async Task With(Func<Env, Task> body, Action<LocalMobileBridgeService>? setup = null)
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        var temp = Path.Combine(Path.GetTempPath(), "cpx_resume_test_" + Guid.NewGuid().ToString("N"));
        var host = AppHost.Create(new AppPaths(temp));
        host.Settings.MobileBridge.Port = port;
        host.Settings.MobileBridge.PairingToken = Token;
        host.Settings.MobileBridge.LegacyTokenAccepted = true;
        host.Settings.MobileBridge.UploadDirectory = Path.Combine(temp, "uploads");
        var cloud = Directory.CreateDirectory(Path.Combine(temp, "cloud")).FullName;
        var bridge = new LocalMobileBridgeService(host, NullLogger<LocalMobileBridgeService>.Instance)
        {
            CloudRootsOverride = () => [new CloudRoot(AppHost.CombinedMountId, cloud, false, false, temp, "All Accounts")]
        };
        setup?.Invoke(bridge);
        try
        {
            bridge.Start();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("X-Pairing-Token", Token);
            await body(new Env(http, $"http://127.0.0.1:{port}", port, host, bridge, temp, cloud));
        }
        finally
        {
            await bridge.DisposeAsync();
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    private static byte[] Bytes(int n) => Enumerable.Range(0, n).Select(i => (byte)(i * 7)).ToArray();

    private static async Task<long> WaitForOffset(Env env, long atLeast)
    {
        long offset = 0;
        for (int i = 0; i < 50; i++)
        {
            var (_, st) = await env.Send(HttpMethod.Get, $"/api/upload/status?uploadId={Id}");
            offset = st.GetProperty("offset").GetInt64();
            if (offset >= atLeast) break;
            await Task.Delay(100);
        }
        return offset;
    }

    [Fact]
    public Task A_cut_off_write_continues_from_where_it_stopped() => With(async env =>
    {
        var data = Bytes(30_000);
        string url = $"/api/fs/write?path={E("cloudssd:clip.bin")}&uploadId={Id}&total={data.Length}";
        await env.CutOff(url + "&offset=0", data.Length, data[..10_000]);
        long have = await WaitForOffset(env, 10_000);
        Assert.Equal(10_000, have);
        Assert.False(File.Exists(Path.Combine(env.Cloud, "clip.bin")));   // nothing half-done in the cloud drive
        Assert.Empty(Directory.GetFiles(env.Cloud));

        var (status, done) = await env.Send(HttpMethod.Post, url + $"&offset={have}", new ByteArrayContent(data[(int)have..]));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("cloudssd:clip.bin", done.GetProperty("id").GetString());
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(env.Cloud, "clip.bin")));
        Assert.Empty(Directory.GetFiles(Path.Combine(env.Host.Paths.DataRoot, "incoming")));
        Assert.Empty(Directory.GetFiles(env.Cloud, "*" + LocalMobileBridgeService.PartSuffix));
    });

    [Fact]
    public Task A_wrong_offset_gets_the_real_one() => With(async env =>
    {
        var data = Bytes(1000);
        string url = $"/api/upload?filename=a.bin&uploadId={Id}&total={data.Length}";
        var (s1, first) = await env.Send(HttpMethod.Post, url + "&offset=0", new ByteArrayContent(data[..400]));
        Assert.Equal(HttpStatusCode.OK, s1);
        Assert.False(first.GetProperty("complete").GetBoolean());
        Assert.Equal(400, first.GetProperty("offset").GetInt64());

        var (s2, ahead) = await env.Send(HttpMethod.Post, url + "&offset=700", new ByteArrayContent(data[700..]));
        Assert.Equal(HttpStatusCode.Conflict, s2);
        Assert.Equal(400, ahead.GetProperty("offset").GetInt64());

        // Going back drops what came after that point.
        var (s3, _) = await env.Send(HttpMethod.Post, url + "&offset=200", new ByteArrayContent(data[200..]));
        Assert.Equal(HttpStatusCode.OK, s3);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(env.Host.Settings.MobileBridge.UploadDirectory!, "a.bin")));
    });

    [Fact]
    public Task A_different_file_under_the_same_id_starts_over() => With(async env =>
    {
        string url = $"/api/upload?filename=a.bin&uploadId={Id}";
        await env.Send(HttpMethod.Post, url + "&total=100&offset=0", new ByteArrayContent(Bytes(50)));
        var (status, json) = await env.Send(HttpMethod.Post, url + "&total=200&offset=50", new ByteArrayContent(Bytes(150)));
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(0, json.GetProperty("offset").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Send(HttpMethod.Get, "/api/upload/status?uploadId=NOT-HEX")).Status);
    });

    [Fact]
    public Task Uploads_without_an_id_still_drop_a_cut_off_file() => With(async env =>
    {
        await env.CutOff($"/api/fs/write?path={E("cloudssd:x.txt")}", 1000, Encoding.ASCII.GetBytes("only ten b"));
        await Task.Delay(500);
        Assert.Empty(Directory.GetFiles(env.Cloud));
        var incoming = Path.Combine(env.Host.Paths.DataRoot, "incoming");
        Assert.True(!Directory.Exists(incoming) || Directory.GetFiles(incoming).Length == 0);
    });

    [Fact]
    public Task Camera_backup_resumes_and_uses_the_phones_month() => With(async env =>
    {
        long march31Utc = new DateTimeOffset(2024, 3, 31, 23, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var data = Bytes(5000);
        string url = $"/api/backup/upload?device=Pixel&folder=Camera&name=IMG_1.jpg&modified={march31Utc}&month=2024-03&uploadId={Id}&total={data.Length}";
        var (s1, part) = await env.Send(HttpMethod.Post, url + "&offset=0", new ByteArrayContent(data[..2000]));
        Assert.Equal(HttpStatusCode.OK, s1);
        Assert.False(part.GetProperty("complete").GetBoolean());
        var (s2, done) = await env.Send(HttpMethod.Post, url + "&offset=2000", new ByteArrayContent(data[2000..]));
        Assert.Equal(HttpStatusCode.OK, s2);
        Assert.True(done.GetProperty("complete").GetBoolean());
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(env.Cloud, "Phone Backup", "Pixel", "Camera", "2024-03", "IMG_1.jpg")));

        // Same name and size in another month is a different photo: it is saved, not skipped.
        var (_, april) = await env.Send(HttpMethod.Post, "/api/backup/upload?device=Pixel&folder=Camera&name=IMG_1.jpg&month=2024-04", new ByteArrayContent(data));
        Assert.False(april.GetProperty("skipped").GetBoolean());
        Assert.True(File.Exists(Path.Combine(env.Cloud, "Phone Backup", "Pixel", "Camera", "2024-04", "IMG_1.jpg")));
    });

    [Fact]
    public Task Old_unfinished_uploads_are_cleaned() => With(async env =>
    {
        await env.Send(HttpMethod.Post, $"/api/upload?filename=a.bin&uploadId={Id}&total=100&offset=0", new ByteArrayContent(Bytes(10)));
        var incoming = Path.Combine(env.Host.Paths.DataRoot, "incoming");
        foreach (var f in Directory.GetFiles(incoming)) File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-5));
        env.Bridge.CleanStaging(TimeSpan.FromHours(72));
        Assert.Empty(Directory.GetFiles(incoming));
    });

    [Fact]
    public Task Downloads_resume_with_range_and_restart_when_the_file_changed() => With(async env =>
    {
        var file = Path.Combine(env.Cloud, "big.bin");
        var data = Bytes(4096);
        File.WriteAllBytes(file, data);
        string url = $"{env.Base}/api/download?path={E(file)}";

        using var first = await env.Http.GetAsync(url);
        var etag = first.Headers.ETag!.Tag;
        Assert.Contains("attachment", first.Content.Headers.ContentDisposition!.ToString());

        using var resume = new HttpRequestMessage(HttpMethod.Get, url);
        resume.Headers.Range = new RangeHeaderValue(1000, null);
        resume.Headers.TryAddWithoutValidation("If-Range", etag);
        using var part = await env.Http.SendAsync(resume);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal(data[1000..], await part.Content.ReadAsByteArrayAsync());

        File.WriteAllBytes(file, Bytes(5000));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
        using var stale = new HttpRequestMessage(HttpMethod.Get, url);
        stale.Headers.Range = new RangeHeaderValue(1000, null);
        stale.Headers.TryAddWithoutValidation("If-Range", etag);
        using var whole = await env.Http.SendAsync(stale);
        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Equal(5000, (await whole.Content.ReadAsByteArrayAsync()).Length);
    });

    [Fact]
    public async Task Thumbnails_are_made_once_and_cached()
    {
        int made = 0;
        await With(async env =>
        {
            File.WriteAllText(Path.Combine(env.Cloud, "a.jpg"), "pretend");
            File.WriteAllText(Path.Combine(env.Cloud, "b.jpg"), "pretend");
            File.WriteAllText(Path.Combine(env.Cloud, "notes.txt"), "x");
            var (s1, _) = await env.Send(HttpMethod.Get, $"/api/fs/thumb?path={E("cloudssd:a.jpg")}&w=128");
            var (s2, _) = await env.Send(HttpMethod.Get, $"/api/fs/thumb?path={E("cloudssd:a.jpg")}&w=128");
            Assert.Equal(HttpStatusCode.OK, s1);
            Assert.Equal(HttpStatusCode.OK, s2);
            Assert.Equal(1, made);
            Assert.Equal(HttpStatusCode.NotFound, (await env.Send(HttpMethod.Get, $"/api/fs/thumb?path={E("cloudssd:b.jpg")}")).Status);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await env.Send(HttpMethod.Get, $"/api/fs/thumb?path={E("cloudssd:notes.txt")}")).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await env.Send(HttpMethod.Get, $"/api/fs/thumb?path={E("cloudssd:../x.jpg")}")).Status);
            var (_, status) = await env.Send(HttpMethod.Get, "/api/status");
            Assert.Contains("fs-thumb", status.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        }, bridge => bridge.ThumbnailFactory = (path, w, _) =>
        {
            if (path.EndsWith("b.jpg")) return Task.FromResult<byte[]?>(null);
            Interlocked.Increment(ref made);
            return Task.FromResult<byte[]?>([0xFF, 0xD8, 0xFF, 0xD9]);
        });
    }

    [Fact]
    public Task Without_a_thumbnail_maker_the_feature_is_not_offered() => With(async env =>
    {
        File.WriteAllText(Path.Combine(env.Cloud, "a.jpg"), "pretend");
        Assert.Equal(HttpStatusCode.NotFound, (await env.Send(HttpMethod.Get, $"/api/fs/thumb?path={E("cloudssd:a.jpg")}")).Status);
        var (_, status) = await env.Send(HttpMethod.Get, "/api/status");
        Assert.DoesNotContain("fs-thumb", status.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
    });

    [Fact]
    public Task Search_finds_files_and_folders_deeply() => With(async env =>
    {
        var deep = Directory.CreateDirectory(Path.Combine(env.Cloud, "A", "B", "C", "D", "Holiday Photos")).FullName;
        File.WriteAllText(Path.Combine(deep, "beach holiday.jpg"), "x");
        File.WriteAllText(Path.Combine(env.Cloud, "holiday plan.txt"), "x");

        var (_, fs) = await env.Send(HttpMethod.Get, $"/api/fs/search?path={E("cloudssd:")}&q=holiday&depth=6");
        var ids = fs.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("entry").GetProperty("id").GetString()).ToList();
        Assert.Contains("cloudssd:A/B/C/D/Holiday Photos", ids);
        Assert.Contains("cloudssd:A/B/C/D/Holiday Photos/beach holiday.jpg", ids);
        Assert.Contains("cloudssd:holiday plan.txt", ids);

        // Every word must appear, in any order.
        var (_, two) = await env.Send(HttpMethod.Get, $"/api/fs/search?path={E("cloudssd:")}&q={E("holiday beach")}&depth=6");
        Assert.Single(two.GetProperty("entries").EnumerateArray());

        var (_, files) = await env.Send(HttpMethod.Get, $"/api/files?path={E(env.Cloud)}&search=holiday&folders=1&depth=6");
        Assert.Contains(files.GetProperty("folders").EnumerateArray(), f => f.GetProperty("name").GetString() == "Holiday Photos");
        var (_, everywhere) = await env.Send(HttpMethod.Get, "/api/files?search=plan&scope=all");
        Assert.Contains(everywhere.GetProperty("files").EnumerateArray(), f => f.GetProperty("name").GetString() == "holiday plan.txt");
    });

    [Fact]
    public Task Details_and_folder_size() => With(async env =>
    {
        var dir = Directory.CreateDirectory(Path.Combine(env.Cloud, "Docs", "Inner")).FullName;
        File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(env.Cloud, "Docs", "b.bin"), new byte[50]);
        var (_, usage) = await env.Send(HttpMethod.Get, $"/api/fs/usage?path={E("cloudssd:Docs")}");
        Assert.Equal(2, usage.GetProperty("files").GetInt64());
        Assert.Equal(1, usage.GetProperty("folders").GetInt64());
        Assert.Equal(150, usage.GetProperty("bytes").GetInt64());

        var (_, stat) = await env.Send(HttpMethod.Get, $"/api/fs/stat?path={E("cloudssd:Docs/b.bin")}&details=1");
        Assert.True(stat.GetProperty("entry").GetProperty("created").GetInt64() > 0);
        Assert.EndsWith(Path.Combine("Docs", "b.bin"), stat.GetProperty("fullPath").GetString());
    });
}
