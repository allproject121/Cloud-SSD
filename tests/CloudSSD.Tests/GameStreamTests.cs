using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CloudSSD.App;
using CloudSSD.Core;
using CloudSSD.GameStream;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CloudSSD.Tests;

/// <summary>Play on phone: the wire format, touch placement, bitrate steps, and who may use /api/game/*.</summary>
public class GameStreamTests
{
    private const string Shared = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Touch_messages_survive_the_trip()
    {
        var bytes = StreamProtocol.TouchFrame(
            new StreamProtocol.Finger(0, StreamProtocol.FingerDown, 0, 1),
            new StreamProtocol.Finger(7, StreamProtocol.FingerMove, 0.25, 0.5));
        var message = await StreamProtocol.ReadAsync(new MemoryStream(bytes), StreamProtocol.MaxFromPhone, CancellationToken.None);
        Assert.NotNull(message);
        Assert.Equal(StreamProtocol.Touch, message.Value.Type);
        var fingers = StreamProtocol.ParseTouch(message.Value.Payload)!;
        Assert.Equal(2, fingers.Length);
        Assert.Equal((byte)7, fingers[1].Id);
        Assert.Equal(StreamProtocol.FingerMove, fingers[1].State);
        Assert.Equal(0.25, fingers[1].X, 3);
        Assert.Equal(1.0, fingers[0].Y, 3);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 0, 1, 0, 0 })]            // too short
    [InlineData(new byte[] { 1, 0, 9, 0, 0, 0, 0 })]      // unknown finger state
    [InlineData(new byte[] { 11 })]                       // more than ten fingers
    public void Bad_touch_messages_are_refused(byte[] payload) => Assert.Null(StreamProtocol.ParseTouch(payload));

    [Fact]
    public async Task Oversized_messages_from_the_phone_are_refused()
    {
        var bytes = StreamProtocol.Frame(StreamProtocol.Touch, default, new byte[StreamProtocol.MaxFromPhone + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => StreamProtocol.ReadAsync(new MemoryStream(bytes), StreamProtocol.MaxFromPhone, CancellationToken.None));
    }

    [Fact]
    public void Video_messages_carry_time_and_keyframe_flag()
    {
        var bytes = StreamProtocol.VideoFrame(123456789, true, [0, 0, 0, 1, 0x65]);
        Assert.Equal(StreamProtocol.Video, bytes[0]);
        Assert.Equal(9 + 5, BitConverter.ToInt32(bytes, 1));
        Assert.Equal(123456789, BitConverter.ToInt64(bytes, 5));
        Assert.Equal(1, bytes[13]);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0, 0)]
    [InlineData(1.0, 1.0, 1919, 1079)]
    [InlineData(0.5, 0.5, 960, 540)]
    [InlineData(-1.0, 2.0, 0, 1079)]                     // clamped to the screen
    public void Fingers_land_on_the_captured_screen(double x, double y, int px, int py)
    {
        var (mx, my) = TouchInjector.Map((0, 0, 1920, 1080), x, y);
        Assert.Equal(px, mx);
        Assert.Equal(py, my);
    }

    [Fact]
    public void Bitrate_drops_when_behind_and_climbs_back_slowly()
    {
        int calm = 0;
        Assert.Equal(7500, GameStreamSession.NextBitrate(10_000, 10, ref calm, 2000, 10_000));
        Assert.Equal(2000, GameStreamSession.NextBitrate(2200, 10, ref calm, 2000, 10_000));
        int rate = 4000;
        for (int i = 0; i < 4; i++) rate = GameStreamSession.NextBitrate(rate, 0, ref calm, 2000, 10_000);
        Assert.Equal(4000, rate);
        Assert.Equal(4400, GameStreamSession.NextBitrate(rate, 0, ref calm, 2000, 10_000));
        Assert.Equal(10_000, GameStreamSession.NextBitrate(10_000, 0, ref calm, 2000, 10_000));
    }

    // ---- who may play ---------------------------------------------------------------------------------------------

    private sealed record Env(HttpClient Http, string Base, AppHost Host, LocalMobileBridgeService Bridge)
    {
        public Task<HttpResponseMessage> Get(string url, string token) => Http.GetAsync($"{Base}{url}?token={token}");

        public async Task<string> PairNew()
        {
            var r = await Http.PostAsync($"{Base}/api/pair?code={Bridge.PairingCode}&name=Phone&installId=test-install-01", null);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
        }
    }

    private static async Task With(Func<Env, Task> body, Action<LocalMobileBridgeService>? setup = null)
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        var temp = Path.Combine(Path.GetTempPath(), "cpx_game_test_" + Guid.NewGuid().ToString("N"));
        var host = AppHost.Create(new AppPaths(temp));
        var b = host.Settings.MobileBridge;
        b.Port = port;
        b.PairingToken = Shared;
        b.LegacyTokenAccepted = true;
        var bridge = new LocalMobileBridgeService(host, NullLogger<LocalMobileBridgeService>.Instance);
        setup?.Invoke(bridge);
        try
        {
            bridge.Start();
            using var http = new HttpClient();
            await body(new Env(http, $"http://127.0.0.1:{port}", host, bridge));
        }
        finally
        {
            await bridge.DisposeAsync();
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    [Fact]
    public Task A_paired_phone_on_the_home_network_sees_the_game() => With(async env =>
    {
        var token = await env.PairNew();
        var r = await env.Get("/api/game/status", token);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Free Fire", json.GetProperty("game").GetString());
        Assert.False(json.GetProperty("playing").GetBoolean());
    });

    [Fact]
    public Task Needs_a_key_of_its_own() => With(async env =>
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Http.GetAsync(env.Base + "/api/game/status")).StatusCode);
        var r = await env.Get("/api/game/stream", Shared);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("device-key-needed", await r.Content.ReadAsStringAsync());
    });

    [Fact]
    public Task Needs_the_switch() => With(async env =>
    {
        var token = await env.PairNew();
        env.Host.Settings.MobileBridge.AllowGameStream = false;
        var r = await env.Get("/api/game/stream", token);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("switched-off", await r.Content.ReadAsStringAsync());
    });

    [Fact]
    public Task Home_network_only_for_now() => With(async env =>
    {
        var token = await env.PairNew();
        env.Host.Settings.MobileBridge.AllowTailscale = true;
        env.Bridge.RouteOverride = (_, _) => LocalMobileBridgeService.NetRoute.Tailnet;
        var r = await env.Get("/api/game/stream", token);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("home-only", await r.Content.ReadAsStringAsync());
    });
}
